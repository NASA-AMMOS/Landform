using Newtonsoft.Json;
using log4net;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class CloudPipelineConfig : SingletonConfig<CloudPipelineConfig>
    {
        [ConfigEnvironmentVariable("LANDFORM_VENUE")]
        public string Venue;

        [ConfigEnvironmentVariable("LANDFORM_AWS_REGION")]
        public string AWSRegion;

        [ConfigEnvironmentVariable("LANDFORM_AWS_PROFILE")]
        public string AWSProfile;

        [ConfigEnvironmentVariable("LANDFORM_S3_URL")]
        public string S3Url;

        //TODO MSL specific
        [ConfigEnvironmentVariable("LANDFORM_MSLICE_AWS_PROFILE")]
        public string MSLICEAWSProfile;

        //TODO MSL specific
        [ConfigEnvironmentVariable("LANDFORM_MSLICE_AWS_REGION")]
        public string MSLICEAWSRegion;

        //TODO MSL specific
        [ConfigEnvironmentVariable("LANDFORM_MSLICE_S3_URL")]
        public string MSLICES3Url;

        //0 to use all available cores, N to use up to N, -M to reserve M
        [ConfigEnvironmentVariable("LANDFORM_MAX_CORES")]
        public int MaxCores;

        //negative to use a time-dependent random seed
        [ConfigEnvironmentVariable("LANDFORM_RANDOM_SEED")]
        public int RandomSeed = -1; //default to -1 not 0

        //enable legacy compatibility (read only)
        [ConfigEnvironmentVariable("LANDFORM_LEGACY_COMPAT")]
        public bool LegacyCompat;

        protected override string ConfigFilename()
        {
            return "landform-cloud";
        }

        public override void Validate()
        {
            if (string.IsNullOrEmpty(Venue))
            {
                throw new Exception("undefined venue name in config");
            }
            if (string.IsNullOrEmpty(AWSRegion))
            {
                throw new Exception("undefined AWS region in config"); 
            }
            if (string.IsNullOrEmpty(S3Url))
            {
                throw new Exception("undefined S3 url in config");
            }
        }
    }
}
