using System;
using System.IO;
using JPLOPS.Util;

namespace JPLOPS.Pipeline
{
    public class LocalPipelineConfig : SingletonConfig<LocalPipelineConfig>
    {
        [ConfigEnvironmentVariable("LANDFORM_VENUE")]
        public string Venue = "local";

        [ConfigEnvironmentVariable("LANDFORM_STORAGE_DIR")]
        public string StorageDir; //see GetDefaultStorageDir()

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

        public LocalPipelineConfig()
        {
            if (string.IsNullOrEmpty(StorageDir)) //could be set by base class constructor
            {
                StorageDir = GetDefaultStorageDir();
            }
        }

        public static string GetDefaultStorageDir()
        {
            return Path.Combine(PathHelper.GetDocDir(), "landform-storage");
        }

        public override string ConfigFileName()
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
