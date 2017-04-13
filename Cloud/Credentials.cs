using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    public class Credentials
    {
        /// <summary>
        /// Load aws credentials using a profle name
        /// These can be defined in ~/.aws/credentials
        /// </summary>
        /// <param name="awsProfileName"></param>
        /// <returns></returns>
        public static AWSCredentials Get(string awsProfileName)
        {
            var chain = new CredentialProfileStoreChain();
            AWSCredentials awsCredentials;
            if (!chain.TryGetAWSCredentials(awsProfileName, out awsCredentials))
            {
                throw new CloudException("Could not find credentials for aws profile: " + awsProfileName);
            }
            return awsCredentials;
        }
    }
}
