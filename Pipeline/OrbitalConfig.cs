using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class OrbitalConfig : SingletonConfig<OrbitalConfig>
    {
        public const string CONFIG_FILENAME = "orbital"; //config file will be ~/.landform/orbital.json
        public override string ConfigFileName()
        {
            return CONFIG_FILENAME;
        }

        //path below LocalPipelineConfig.Instance.StorageDir containing the orbital DEM
        //default is empty which disables orbital DEM
        //overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_STORAGE_PATH")]
        public string OrbitalDEMStoragePath { get; set; }

        //path below LocalPipelineConfig.Instance.StorageDir containing the orbital ortho image
        //default is empty which disables orbital texturing
        //overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_STORAGE_PATH")]
        public string OrbitalImageStoragePath { get; set; }

        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_METERS_PER_PIXEL")]
        public double OrbitalDEMMetersPerPixel { get; set; } = 1;

        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_FRAME_NAME")]
        public string OrbitalFrameName { get; set; } = "Orbital";
    }
}

