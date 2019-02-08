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

        [ConfigEnvironmentVariable("LANDFORM_DYNAMO_URL")]
        public string DynamoUrl;

        //TODO MSL specific
        [ConfigEnvironmentVariable("LANDFORM_MSLICE_AWS_PROFILE")]
        public string MSLICEAWSProfile;

        //TODO MSL specific
        [ConfigEnvironmentVariable("LANDFORM_MSLICE_S3_URL")]
        public string MSLICES3Url;

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
