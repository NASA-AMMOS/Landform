using OPS.Pipeline.TileServer;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.Rover
{
    class MSLCloud
    {
        public static void AddMSLICEProfile(CloudPipeline pipeline)
        {
            //MSL specific: this project does not hold its images within the same s3 bucket as the project
            //future projects  are expected to be within the same bucket
            var config = TileServerConfig.Instance;
            if (!string.IsNullOrEmpty(config.MSLICEProfile) && !string.IsNullOrEmpty(config.MSLICES3Url) &&
                OPS.Cloud.Credentials.Exists(config.MSLICEProfile))
            {
                pipeline.AddAWSProfile(config.MSLICES3Url, config.MSLICEProfile);
            }
        }
    }
}
