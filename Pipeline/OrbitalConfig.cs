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

        //s3 or https URL of orbital DEM
        //default is null which disables download of orbital DEM
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_URL")]
        public string OrbitalDEMURL { get; set; }

        //s3 or https URL of orbital texture image
        //default is null which disables download of orbital texture
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_URL")]
        public string OrbitalImageURL { get; set; }

        //path below LocalPipelineConfig.Instance.StorageDir containing the orbital DEM
        //default is null which disables orbital DEM
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_STORAGE_PATH")]
        public string OrbitalDEMStoragePath { get; set; }

        //path below LocalPipelineConfig.Instance.StorageDir containing the orbital ortho image
        //default is null which disables orbital texturing
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_STORAGE_PATH")]
        public string OrbitalImageStoragePath { get; set; }

        //Mars or Earth
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_BODY_NAME")]
        public string OrbitalBodyName { get; set; } = "Mars";

        //meters per pixel for obital DEM
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_METERS_PER_PIXEL")]
        public double OrbitalDEMMetersPerPixel { get; set; } = 1;

        //elevation scale for obital DEM
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_ELEVATION_SCALE")]
        public double OrbitalDEMElevationScale { get; set; } = 1;

        //meters per pixel for obital texture image
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_METERS_PER_PIXEL")]
        public double OrbitalImageMetersPerPixel { get; set; } = 1;

        //name for orbital adjusted frame transform
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_FRAME_NAME")]
        public string OrbitalFrameName { get; set; } = "Orbital";
    }
}

