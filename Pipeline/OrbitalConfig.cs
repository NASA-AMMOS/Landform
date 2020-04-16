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
        public string DEMURL { get; set; }

        //s3 or https URL of orbital texture image
        //default is null which disables download of orbital texture
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_URL")]
        public string ImageURL { get; set; }

        //path below LocalPipelineConfig.Instance.StorageDir containing the orbital DEM
        //default is null which disables orbital DEM
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_STORAGE_PATH")]
        public string DEMStoragePath { get; set; }

        //path below LocalPipelineConfig.Instance.StorageDir containing the orbital ortho image
        //default is null which disables orbital texturing
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_STORAGE_PATH")]
        public string ImageStoragePath { get; set; }

        //must be recognized by OPS.Imaging.PlanetaryBody.GetByName()
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_BODY_NAME")]
        public string BodyName { get; set; } = "Mars";

        //elevation scale for obital DEM
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_ELEVATION_SCALE")]
        public double DEMElevationScale { get; set; } = 1;

        //DEM values outside these bounds are considered invalid
        //ignored if min >= max (e.g. min = max = 0 disables filtering)
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_MIN_FILTER")]
        public double DEMMinFilter { get; set; } = 0;

        //DEM values outside these bounds are considered invalid
        //ignored if min >= max (e.g. min = max = 0 disables filtering)
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_MAX_FILTER")]
        public double DEMMaxFilter { get; set; } = 0;

        //meters per pixel for obital DEM
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        //if non-positive then use the value from the GeoTIFF
        //otherwise if the DEM is loaded from a GeoTIFF and the metadata doesn't match orbital will be disabled
        //even when the GeoTIFF metadata does match the effective pixel aspect ratio will potentially be adjusted
        //to account for different effective pixel aspect ratio in regions far from the origin latitude
        //see GISCameraModel.CheckLocalGISImageBasisAndGetResolution()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_METERS_PER_PIXEL")]
        public double DEMMetersPerPixel { get; set; } = 1;

        //meters per pixel for obital texture image
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        //if non-positive then use the value from the GeoTIFF
        //otherwise if the image is loaded from a GeoTIFF and the metadata doesn't match orbital will be disabled
        //even when the GeoTIFF metadata does match the effective pixel aspect ratio will potentially be adjusted
        //to account for different effective pixel aspect ratio in regions far from the origin latitude
        //see GISCameraModel.CheckLocalGISImageBasisAndGetResolution()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_METERS_PER_PIXEL")]
        public double ImageMetersPerPixel { get; set; } = 1;

        //index of orbital DEM in PlacesDB
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        //negative disables PlacesDB for orbital DEM, which effectively disables orbital DEM entirely
        //used in PlacesDB queries like:
        //https://<placesdb-venue>/rmc/orbital(DEMPlacesDBIndex)/metadata
        //https://<placesdb-venue>/query/primary/<view>?from=rover(<site>,<drive>)&to=orbital(DEMPlacesDBIndex)
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_PLACESDB_INDEX")]
        public int DEMPlacesDBIndex { get; set; } = 0;

        //index of orbital image in PlacesDB
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        //negative disables PlacesDB for orbital image, which effectively disables orbital image entirely
        //used in PlacesDB queries like:
        //https://<placesdb-venue>/rmc/orbital(DEMPlacesDBIndex)/metadata
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_PLACESDB_INDEX")]
        public int ImagePlacesDBIndex { get; set; } = 0;

        //disable orbital DEM if PlacesDB metadata differs from OrbitalConfig or GeoTIFF
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_ENFORCE_PLACESDB_METADATA")]
        public bool EnforceDEMPlacesDBMetadata { get; set; } = false;

        //disable orbital image if PlacesDB metadata differs from OrbitalConfig or GeoTIFF
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_ENFORCE_PLACESDB_METADATA")]
        public bool EnforceImagePlacesDBMetadata { get; set; } = false;

        //load and use GeoTIFF metadata for orbital DEM
        //at least one of DEMIsGeoTIFF or DEMIsOrthographic must be true
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_IS_GEOTIFF")]
        public bool DEMIsGeoTIFF { get; set; } = true;

        //load and use GeoTIFF metadata for orbital image
        //at least one of ImageIsGeoTIFF or ImageIsOrthographic must be true
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_IS_GEOTIFF")]
        public bool ImageIsGeoTIFF { get; set; } = true;

        //treat orbital DEM as orthographic
        //at least one of DEMIsGeoTIFF or DEMIsOrthographic must be true
        //GeoTIFF metadata is required if not; but even if so, GeoTIFF metadata is used if available
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_IS_ORTHOGRAPHIC")]
        public bool DEMIsOrthographic { get; set; } = true;

        //treat orbital image as orthographic
        //at least one of ImageIsGeoTIFF or ImageIsOrthographic must be true
        //GeoTIFF metadata is required if not; but even if so, GeoTIFF metadata is used if available
        //may be overridden by MissionSpecific.GetOrbitalConfigDefaults()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_IS_ORTHOGRAPHIC")]
        public bool ImageIsOrthographic { get; set; } = true;
    }
}

