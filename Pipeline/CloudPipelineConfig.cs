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
        [ConfigEnvironmentVariable("LANDFORM_VENUE")] //was TILE_SERVER_VENUE_NAME
        public string Venue; //was VenueName

        [ConfigEnvironmentVariable("LANDFORM_AWS_REGION")] //was TILE_SERVER_REGION
        public string AWSRegion; //was Region

        [ConfigEnvironmentVariable("LANDFORM_AWS_PROFILE")] //was TILE_SERVER_PROFILE
        public string AWSProfile; //was Profile

        [ConfigEnvironmentVariable("LANDFORM_S3_URL")] //was TILE_SERVER_S3_URL
        public string S3Url;

        [ConfigEnvironmentVariable("LANDFORM_DYNAMO_URL")] //was absent
        public string DynamoUrl; //was absent

        //TODO MSL specific
        [ConfigEnvironmentVariable("LANDFORM_MSLICE_AWS_PROFILE")] //was TILE_SERVER_MSLICE_PROFILE
        public string MSLICEAWSProfile; //was MSLICEProfile

        //TODO MSL specific
        [ConfigEnvironmentVariable("LANDFORM_MSLICE_S3_URL")] //was TILE_SERVER_MSLICE_S3_URL
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
