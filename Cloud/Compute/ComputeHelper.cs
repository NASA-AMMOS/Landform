using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.IO;
using Amazon;
using Amazon.Runtime;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.AutoScaling;
using Amazon.AutoScaling.Model;
using JPLOPS.Util;

namespace JPLOPS.Cloud
{
    public class ComputeHelper : IDisposable
    {
        public enum InstanceState { unknown, pending, running, shutting_down, terminated, stopping, stopped };

        private AmazonEC2Client ec2Client;
        private AmazonAutoScalingClient asClient;

        private ILogger logger;

        public static string GetSelfInstanceID(ILogger logger = null)
        {
            //https://stackoverflow.com/a/9648259
            string id = null;
            try
            {
                var req = HttpWebRequest.Create("http://169.254.169.254/latest/meta-data/instance-id");
                using (var resp = req.GetResponse() as HttpWebResponse)
                {
                    if (resp.StatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception($"HTTP {resp.StatusCode} {resp.StatusDescription}");
                    }
                    id = new StreamReader(resp.GetResponseStream()).ReadToEnd();
                    if (logger != null)
                    {
                        logger.LogVerbose("self EC2 instance ID: {0}", id);
                    }
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
                    logger.LogVerbose("creating AWS EC2 client for profile \"{0}\" in region \"{1}\"",
                                      awsProfileName, awsRegionName);
                }
                ec2Client = new AmazonEC2Client(awsCredentials, awsRegion);
                asClient = new AmazonAutoScalingClient(awsCredentials, awsRegion);
            }
            else if (awsCredentials != null)
            {
                if (logger != null)
                {
                    logger.LogVerbose("creating AWS EC2 client for profile \"{0}\" in default region", awsProfileName);
                }
                ec2Client = new AmazonEC2Client(awsCredentials);
                asClient = new AmazonAutoScalingClient(awsCredentials);
            }
            else if (awsRegion != null)
            {
                if (logger != null)
                {
                    logger.LogVerbose("creating AWS EC2 client for default profile in region \"{0}\"", awsRegion);
                }
                ec2Client = new AmazonEC2Client(awsRegion);
                asClient = new AmazonAutoScalingClient(awsRegion);
            }
            else
            {
                if (logger != null)
                {
                    logger.LogVerbose("creating AWS EC2 client for default profile and region");
                }
                ec2Client = new AmazonEC2Client();
                asClient = new AmazonAutoScalingClient();
            }
        }

        public List<string> InstanceNamePatternToIDs(string namePattern, InstanceState state = InstanceState.unknown)
        {
            var ret = new List<string>();
            string msg = (state != InstanceState.unknown ? (state + " ") : "") +
                "EC2 instances named \"" + namePattern + "\"";
            try
            {
                var req = new DescribeInstancesRequest();
                req.Filters = new List<Amazon.EC2.Model.Filter>();
                req.Filters.Add(new Amazon.EC2.Model.Filter() {
                        Name = "tag:Name", Values = new List<string> { namePattern }
                    });

                string stateName = null;
                if (state != InstanceState.unknown)
                {
                    stateName = state.ToString().Replace('_', '-');
                    req.Filters.Add(new Amazon.EC2.Model.Filter {
                            Name = "instance-state-name", Values = new List<string> { stateName }
                        });
                }
                
                if (logger != null)
                {
                    logger.LogVerbose("finding " + msg);
                }

                do
                {
                    var resp = ec2Client.DescribeInstances(req);
                    if (resp.HttpStatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception($"HTTP {resp.HttpStatusCode}");
                    }
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
                    logger.LogVerbose("found {0} {1}: {2}{3}", ret.Count, msg, String.Join(", ", ret.Take(100)),
                                      ret.Count > 100 ? ", ..." :"");
                }
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogException(ex, "finding " + msg);
                }
            }
            return ret;
        }

        public InstanceState GetInstanceState(string instanceID)
        {
            var ret = InstanceState.unknown;
            try
            {
                var req = new DescribeInstanceStatusRequest() { InstanceIds = new List<string> { instanceID } };
                var resp = ec2Client.DescribeInstanceStatus(req);
                if (resp.HttpStatusCode != HttpStatusCode.OK)
                {
                    throw new Exception($"HTTP {resp.HttpStatusCode}");
                }
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
                            logger.LogVerbose("EC2 instance {0} state: {1}", instanceID, ret);
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
                    logger.LogException(ex, "getting status for EC2 instance " + instanceID);
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
                        logger.LogVerbose("starting EC2 instances {0}", String.Join(", ", instanceIDs));
                    }
                    var req = new StartInstancesRequest() { InstanceIds = ids };
                    var resp = ec2Client.StartInstances(req);
                    if (resp.HttpStatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception($"HTTP {resp.HttpStatusCode}");
                    }
                    foreach (var change in resp.StartingInstances)
                    {
                        if (change != null && logger != null)
                        {
                            logger.LogVerbose("EC2 instance {0} {1} -> {2}",
                                              change.InstanceId, change.PreviousState.Name, change.CurrentState.Name);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                logger.LogException(ex, "starting EC2 instances " + String.Join(", ", instanceIDs));
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
                        logger.LogVerbose("stopping EC2 instances {0}", String.Join(", ", instanceIDs));
                    }
                    var req = new StopInstancesRequest() { InstanceIds = ids };
                    var resp = ec2Client.StopInstances(req);
                    if (resp.HttpStatusCode != HttpStatusCode.OK)
                    {
                        throw new Exception($"HTTP {resp.HttpStatusCode}");
                    }
                    foreach (var change in resp.StoppingInstances)
                    {
                        if (change != null && logger != null)
                        {
                            logger.LogVerbose("EC2 instance {0} {1} -> {2}",
                                              change.InstanceId, change.PreviousState.Name, change.CurrentState.Name);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                logger.LogException(ex, "stopping EC2 instances " + String.Join(", ", instanceIDs));
            }
            return false;
        }

        public bool SetAutoScalingGroupSize(string name, int min, int desired, int max)
        {
            string msg = $"setting size of auto scaling group {name}: min={min}, desired={desired}, max={max}";
            try
            {
                var req = new UpdateAutoScalingGroupRequest();
                req.AutoScalingGroupName = name;
                if (min >= 0)
                {
                    req.MinSize = min;
                }
                if (desired >= 0)
                {
                    req.DesiredCapacity = desired;
                }
                if (max >= 0)
                {
                    req.MaxSize = max;
                }
                if (logger != null)
                {
                    logger.LogVerbose(msg);
                }
                var resp = asClient.UpdateAutoScalingGroup(req);
                if (resp.HttpStatusCode != HttpStatusCode.OK)
                {
                    throw new Exception($"HTTP {resp.HttpStatusCode}");
                }
                return true;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogException(ex, msg);
                }
            }
            return false;
        }

        public bool SetAutoScalingGroupSize(string name, int size)
        {
            return SetAutoScalingGroupSize(name, -1, size, -1);
        }

        public bool SetInstanceProtection(string asgName, string instanceID, bool enabled)
        {
            string msg = (enabled ? "enabling" : "disabling") +
                $" instance protection for instance {instanceID} in auto scale group {asgName}";
            try
            {
                var req = new SetInstanceProtectionRequest();
                req.AutoScalingGroupName = asgName;
                req.InstanceIds = new List<string>() { instanceID };
                req.ProtectedFromScaleIn = enabled;
                if (logger != null)
                {
                    logger.LogVerbose(msg);
                }
                var resp = asClient.SetInstanceProtection(req);
                if (resp.HttpStatusCode != HttpStatusCode.OK)
                {
                    throw new Exception($"HTTP {resp.HttpStatusCode}");
                }
                return true;
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogException(ex, msg);
                }
            }
            return false;
        }
                
        public void Dispose()
        {
            if (ec2Client != null)
            {
                ec2Client.Dispose();
                ec2Client = null;
            }
            if (asClient != null)
            {
                asClient.Dispose();
                asClient = null;
            }
        }
    }
}
                           
                             
