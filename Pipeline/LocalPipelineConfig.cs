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

        //0 to use all available cores, N to use up to N, -M to reserve M
        [ConfigEnvironmentVariable("LANDFORM_MAX_CORES")]
        public int MaxCores;

        //negative to use a time-dependent random seed
        [ConfigEnvironmentVariable("LANDFORM_RANDOM_SEED")]
        public int RandomSeed = -1; //default to -1 not 0

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
