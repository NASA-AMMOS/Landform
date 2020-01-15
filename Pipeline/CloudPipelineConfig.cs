using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using OPS.Util;

namespace OPS.Pipeline
{
    public class CloudPipelineConfig : SingletonConfig<CloudPipelineConfig>
    {
        [ConfigEnvironmentVariable("LANDFORM_VENUE")]
        public string Venue; //see GetDefaultVenue()

        [ConfigEnvironmentVariable("LANDFORM_AWS_REGION")]
        public string AWSRegion = null; //use default system region

        [ConfigEnvironmentVariable("LANDFORM_AWS_PROFILE")]
        public string AWSProfile = null; //use default system credentials (e.g. from IAM profile on EC2 instance)

        [ConfigEnvironmentVariable("LANDFORM_S3_URL")]
        public string S3Url = "s3://landform/cloud-pipeline";

        //TODO MSL specific
        [ConfigEnvironmentVariable("LANDFORM_MSLICE_AWS_PROFILE")]
        public string MSLICEAWSProfile = "mslice";

        //TODO MSL specific
        [ConfigEnvironmentVariable("LANDFORM_MSLICE_AWS_REGION")]
        public string MSLICEAWSRegion = "us-west-1";

        //TODO MSL specific
        [ConfigEnvironmentVariable("LANDFORM_MSLICE_S3_URL")]
        public string MSLICES3Url = "s3://red-product";

        [ConfigEnvironmentVariable("LANDFORM_IMAGE_MEM_CACHE")]
        public int ImageMemCache = 100;

        [ConfigEnvironmentVariable("LANDFORM_DATA_PRODUCT_MEM_CACHE")]
        public int DataProductMemCache = 100;

        //0 to use all available cores, N to use up to N, -M to reserve M
        [ConfigEnvironmentVariable("LANDFORM_MAX_CORES")]
        public int MaxCores = 0;

        //negative to use a time-dependent random seed
        [ConfigEnvironmentVariable("LANDFORM_RANDOM_SEED")]
        public int RandomSeed = -1;

        //enable legacy compatibility (read only)
        [ConfigEnvironmentVariable("LANDFORM_LEGACY_COMPAT")]
        public bool LegacyCompat = false;

        public CloudPipelineConfig()
        {
            Venue = GetDefaultVenue();
        }

        public static string GetDefaultVenue()
        {
            return string.Format("landform-dev-{0}-{1}",
                                 Environment.UserName.ToLower(), Environment.MachineName.ToLower());
        }

        public override string ConfigFileName()
        {
            return "landform-cloud";
        }

        public override void Validate()
        {
            if (string.IsNullOrEmpty(Venue))
            {
                throw new Exception("undefined venue name in config");
            }
            if (string.IsNullOrEmpty(S3Url))
            {
                throw new Exception("undefined storage URL in config");
            }
        }
    }
}
