using System;
using System.Net;
using Amazon;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

namespace JPLOPS.Cloud
{
    public class ParameterStore : IDisposable
    {
        private AmazonSimpleSystemsManagementClient client;

        public ParameterStore(string awsProfileName = null, string awsRegionName = null)
        {
            var nulls = new string[] { "", "null", "none", "auto" };

            if (awsProfileName != null && Array.IndexOf(nulls, awsProfileName.ToLower()) >= 0)
            {
                awsProfileName = null;
            }

            if (awsRegionName != null && Array.IndexOf(nulls, awsRegionName.ToLower()) >= 0)
            {
                awsRegionName = null;
            }

            var awsCredentials = awsProfileName != null ? Credentials.Get(awsProfileName) : null;

            var awsRegion = awsRegionName != null ? RegionEndpoint.GetBySystemName(awsRegionName) : null;

            if (awsCredentials == null && awsRegion == null)
            {
                client = new AmazonSimpleSystemsManagementClient();
            }
            else if (awsRegion == null)
            {
                client = new AmazonSimpleSystemsManagementClient(awsCredentials);
            }
            else if (awsCredentials == null)
            {
                client = new AmazonSimpleSystemsManagementClient(awsRegion);
            }
            else
            {
                client = new AmazonSimpleSystemsManagementClient(awsCredentials, awsRegion);
            }
        }

        public void Dispose()
        {
            client.Dispose();
            client = null;
        }

        public string GetParameter(string name, bool decrypt = false, bool expectExists = true)
        {
            try
            {
                var resp = client.GetParameter(new GetParameterRequest() { Name = name, WithDecryption = decrypt });
                if (resp.HttpStatusCode != HttpStatusCode.OK)
                {
                    throw new CloudException($"failed to get SSM parameter {name}: http status {resp.HttpStatusCode}");
                }
                if (resp.Parameter.DataType != "text")
                {
                    throw new CloudException($"unhandled SSM parameter type for {name}: {resp.Parameter.DataType}");
                }
                return resp.Parameter.Value;
            }
            catch (ParameterNotFoundException)
            {
                if (expectExists)
                {
                    throw;
                }
                return null;
            }
        }
    }
}
