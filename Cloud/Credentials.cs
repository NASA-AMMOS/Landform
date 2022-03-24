using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;

namespace JPLOPS.Cloud
{
    public class Credentials
    {
        /// <summary>
        /// Load aws credentials using a profile name
        /// These can be defined in ~/.aws/credentials
        /// </summary>
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

        /// <summary>
        /// Reports whether credentials exist for a profile name
        /// </summary>
        public static bool Exists(string awsProfileName)
        {
            var chain = new CredentialProfileStoreChain();
            return chain.TryGetAWSCredentials(awsProfileName, out AWSCredentials awsCredentials);
        }
    }
}
