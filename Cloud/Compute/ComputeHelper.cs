using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.IO;
using Amazon;
using Amazon.Runtime;
using Amazon.EC2;
using Amazon.EC2.Model;
using OPS.Util;

namespace OPS.Cloud
{
    public class ComputeHelper : IDisposable
    {
        public enum InstanceState { unknown, pending, running, shutting_down, terminated, stopping, stopped };

        private AmazonEC2Client client;
        private ILogger logger;

        public static string GetSelfInstanceID(ILogger logger = null)
        {
            //https://stackoverflow.com/a/9648259
            string id = null;
            try
            {
                var req = HttpWebRequest.Create("http://169.254.169.254/latest/meta-data/instance-id");
                var resp = req.GetResponse();
                id = new StreamReader(resp.GetResponseStream()).ReadToEnd();
                if (logger != null)
                {
                    logger.LogInfo("self EC2 instance ID: {0}", id);
                }
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogWarn("failed to get self EC2 instance ID (maybe not running on EC2): {0}", ex.Message);
                }
            }
            return id;
        }

        public ComputeHelper(string awsProfileName = null, string awsRegionName = null, ILogger logger = null)
        {
            this.logger = logger;

            string[] nulls = { "", "null", "none", "auto" };
            Func<string, string> convertNull = s => s == null || nulls.Any(n => n == s.ToLower()) ? null : s;
            awsProfileName = convertNull(awsProfileName);
            awsRegionName = convertNull(awsRegionName);

            AWSCredentials awsCredentials = awsProfileName != null ? Credentials.Get(awsProfileName) : null;
            RegionEndpoint awsRegion = awsRegionName != null ? RegionEndpoint.GetBySystemName(awsRegionName) : null;

            if (awsCredentials != null && awsRegion != null)
            {
                if (logger != null)
                {
                    logger.LogInfo("creating AWS EC2 client for profile \"{0}\" in region \"{1}\"",
                                   awsProfileName, awsRegionName);
                }
                client = new AmazonEC2Client(awsCredentials, awsRegion);
            }
            else if (awsCredentials != null)
            {
                if (logger != null)
                {
                    logger.LogInfo("creating AWS EC2 client for profile \"{0}\" in default region", awsProfileName);
                }
                client = new AmazonEC2Client(awsCredentials);
            }
            else if (awsRegion != null)
            {
                if (logger != null)
                {
                    logger.LogInfo("creating AWS EC2 client for default profile in region \"{0}\"", awsRegion);
                }
                client = new AmazonEC2Client(awsRegion);
            }
            else
            {
                if (logger != null)
                {
                    logger.LogInfo("creating AWS EC2 client for default profile and region");
                }
                client = new AmazonEC2Client();
            }
        }

        public List<string> InstanceNamePatternToIDs(string namePattern, InstanceState state = InstanceState.unknown)
        {
            string msg = (state != InstanceState.unknown ? (state + " ") : "") +
                "EC2 instances named \"" + namePattern + "\"";
            try
            {
                var req = new DescribeInstancesRequest();
                req.Filters = new List<Filter>();
                req.Filters.Add(new Filter { Name = "tag:Name", Values = new List<string> { namePattern } });

                string stateName = null;
                if (state != InstanceState.unknown)
                {
                    stateName = state.ToString().Replace('_', '-');
                    req.Filters.Add(new Filter { Name = "instance-state-name",
                                                 Values = new List<string> { stateName } });
                }
                
                if (logger != null)
                {
                    logger.LogInfo("finding " + msg);
                }

                var ret = new List<string>();
                do
                {
                    var resp = client.DescribeInstances(req);
                    if (resp.Reservations != null)
                    {
                        foreach (var reservation in resp.Reservations)
                        {
                            if (reservation.Instances != null)
                            {
                                foreach (var instance in reservation.Instances)
                                {
                                    if (instance != null && !string.IsNullOrEmpty(instance.InstanceId))
                                    {
                                        ret.Add(instance.InstanceId);
                                    }
                                }
                            }
                        }
                    }
                    req.NextToken = resp.NextToken;
                } while (!string.IsNullOrEmpty(req.NextToken));

                if (logger != null)
                {
                    logger.LogInfo("found {0} {1}: {2}{3}", ret.Count, msg, String.Join(", ", ret.Take(100)),
                                   ret.Count > 100 ? ", ..." :"");
                }

                return ret;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogException(ex, "error finding " + msg);
                }
                return null;
            }
        }

        public InstanceState GetInstanceState(string instanceID)
        {
            var ret = InstanceState.unknown;
            try
            {
                var req = new DescribeInstanceStatusRequest() { InstanceIds = new List<string> { instanceID } };
                var resp = client.DescribeInstanceStatus(req);
                if (resp.InstanceStatuses != null && resp.InstanceStatuses.Count == 1 &&
                    string.IsNullOrEmpty(resp.NextToken))
                {
                    string stateName = resp.InstanceStatuses[0].InstanceState.Name.Value.ToLower().Trim();
                    if (!Enum.TryParse<InstanceState>(stateName, out ret))
                    {
                        if (stateName.Equals("shuttingdown"))
                        {
                            ret = InstanceState.shutting_down;
                        }
                    }
                    if (logger != null)
                    {
                        if (ret != InstanceState.unknown)
                        {
                            logger.LogInfo("EC2 instance {0} state: {1}", instanceID, ret);
                        }
                        else
                        {
                            logger.LogError("unrecognized state \"{0}\" for EC2 instance {1}", stateName, instanceID);
                        }
                    }
                }
                else
                {
                    if (logger != null)
                    {
                        logger.LogError("did not receive exactly one status for EC2 instance {0}", instanceID);
                    }
                }
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogException(ex, "error getting status for EC2 instance " + instanceID);
                }
            }
            return ret;
        }

        public bool StartInstances(params string[] instanceIDs)
        {
            try
            {
                var ids = instanceIDs.Where(id => !string.IsNullOrEmpty(id)).ToList();
                if (ids.Count > 0)
                {
                    if (logger != null)
                    {
                        logger.LogInfo("starting EC2 instances {0}", String.Join(", ", instanceIDs));
                    }
                    var req = new StartInstancesRequest() { InstanceIds = ids };
                    var resp = client.StartInstances(req);
                    foreach (var change in resp.StartingInstances)
                    {
                        if (change != null && logger != null)
                        {
                            logger.LogInfo("EC2 instance {0} {1} -> {2}",
                                           change.InstanceId, change.PreviousState.Name, change.CurrentState.Name);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                logger.LogException(ex, "error starting EC2 instances " + String.Join(", ", instanceIDs));
            }
            return false;
        }

        public bool StopInstances(params string[] instanceIDs)
        {
            try
            {
                var ids = instanceIDs.Where(id => !string.IsNullOrEmpty(id)).ToList();
                if (ids.Count > 0)
                {
                    if (logger != null)
                    {
                        logger.LogInfo("stopping EC2 instances {0}", String.Join(", ", instanceIDs));
                    }
                    var req = new StopInstancesRequest() { InstanceIds = ids };
                    var resp = client.StopInstances(req);
                    foreach (var change in resp.StoppingInstances)
                    {
                        if (change != null && logger != null)
                        {
                            logger.LogInfo("EC2 instance {0} {1} -> {2}",
                                           change.InstanceId, change.PreviousState.Name, change.CurrentState.Name);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                logger.LogException(ex, "error stopping EC2 instances " + String.Join(", ", instanceIDs));
            }
            return false;
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }
}
                           
                             
