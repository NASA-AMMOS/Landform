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
    public class LocalPipelineConfig : SingletonConfig<LocalPipelineConfig>
    {
        [ConfigEnvironmentVariable("LANDFORM_VENUE")]
        public string Venue;

        [ConfigEnvironmentVariable("LANDFORM_STORAGE_DIR")]
        public string StorageDir;

        protected override string ConfigFilename()
        {
            return "landform-local";
        }

        public override void Validate()
        {
            if (string.IsNullOrEmpty(Venue))
            {
                throw new Exception("undefined venue name in config");
            }
            if (string.IsNullOrEmpty(StorageDir))
            {
                throw new Exception("undefined storage dirctory in config");
            }
        }
    }
}
