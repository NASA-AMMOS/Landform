using JPLOPS.Util;
using System.IO;

namespace JPLOPS.Pipeline
{
    //order of precedence (lower in list = higher precdence)
    //* inline literals below
    //* MissionSpecific.GetOrbitalConfigDefaults()
    //* ~/.landform/orbital.json
    //* env vars
    //* command line options, if available (e.g. --orbitaldem, --orbitalimage)
    public class OrbitalConfig : SingletonConfig<OrbitalConfig>
    {
        public const string CONFIG_FILENAME = "orbital"; //config file will be ~/.landform/orbital.json
        public override string ConfigFileName()
        {
            return CONFIG_FILENAME;
        }

        //get local path to orbital DEM file
        //default dir is LocalPipelineConfig.Instance.StorageDir / StoragePath
        //default url is DEMURL
        //the path is dir + last segment of url
        public string GetDEMFile(string dir = null, string url = null)
        {
            return GetFile(dir, string.IsNullOrEmpty(url) ? DEMURL : url);
        }

        //get local path to orbital image file
        //default dir is LocalPipelineConfig.Instance.StorageDir / StoragePath
        //default url is ImageURL
        //the path is dir + last segment of url
        public string GetImageFile(string dir = null, string url = null)
        {
            return GetFile(dir, string.IsNullOrEmpty(url) ? ImageURL : url);
        }

        private string GetFile(string dir, string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }
            dir = string.IsNullOrEmpty(dir) ? Path.Combine(LocalPipelineConfig.Instance.StorageDir, StoragePath) : dir;
            return string.IsNullOrEmpty(dir) ? url : Path.Combine(dir, StringHelper.GetLastUrlPathSegment(url));
        }

        //s3 or https URL of orbital DEM
        //default is null which disables download of orbital DEM
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_URL")]
        public string DEMURL { get; set; }

        //s3 or https URL of orbital texture image
        //default is null which disables download of orbital texture
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_URL")]
        public string ImageURL { get; set; }

        //default storage path below LocalPipelineConfig.Instance.StorageDir
        //default is "orbital"
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_STORAGE_PATH")]
        public string StoragePath { get; set; } = "orbital";

        //must be recognized by OPS.Imaging.PlanetaryBody.GetByName()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_BODY_NAME")]
        public string BodyName { get; set; } = "Mars";

        //elevation scale for obital DEM
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_ELEVATION_SCALE")]
        public double DEMElevationScale { get; set; } = 1;

        //DEM values outside these bounds are considered invalid
        //ignored if min >= max (e.g. min = max = 0 disables filtering)
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_MIN_FILTER")]
        public double DEMMinFilter { get; set; } = 0;

        //DEM values outside these bounds are considered invalid
        //ignored if min >= max (e.g. min = max = 0 disables filtering)
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_MAX_FILTER")]
        public double DEMMaxFilter { get; set; } = 0;

        //meters per pixel for obital DEM
        //if non-positive then use the value from the GeoTIFF
        //otherwise if the DEM is loaded from a GeoTIFF and the metadata doesn't match orbital will be disabled
        //even when the GeoTIFF metadata does match the effective pixel aspect ratio will potentially be adjusted
        //to account for different effective pixel aspect ratio in regions far from the origin latitude
        //see GISCameraModel.CheckLocalGISImageBasisAndGetResolution()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_METERS_PER_PIXEL")]
        public double DEMMetersPerPixel { get; set; } = 1;

        //meters per pixel for obital texture image
        //if non-positive then use the value from the GeoTIFF
        //otherwise if the image is loaded from a GeoTIFF and the metadata doesn't match orbital will be disabled
        //even when the GeoTIFF metadata does match the effective pixel aspect ratio will potentially be adjusted
        //to account for different effective pixel aspect ratio in regions far from the origin latitude
        //see GISCameraModel.CheckLocalGISImageBasisAndGetResolution()
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_METERS_PER_PIXEL")]
        public double ImageMetersPerPixel { get; set; } = 1;

        //index of orbital DEM in PlacesDB
        //negative disables PlacesDB for orbital DEM
        //which effectively disables orbital entirely because the DEM metadata is always used to get sitedrive lon, lat
        //used in PlacesDB queries like:
        //https://<placesdb-venue>/rmc/orbital(DEMPlacesDBIndex)/metadata
        //https://<placesdb-venue>/query/primary/<view>?from=rover(<site>,<drive>)&to=orbital(DEMPlacesDBIndex)
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_PLACESDB_INDEX")]
        public int DEMPlacesDBIndex { get; set; } = 0;

        //index of orbital image in PlacesDB
        //negative disables PlacesDB for orbital image
        //but the orbital image may still be usable if it has GeoTIFF metadata (and there is a usable DEM)
        //used in PlacesDB queries like:
        //https://<placesdb-venue>/rmc/orbital(ImagePlacesDBIndex)/metadata
        //https://<placesdb-venue>/query/primary/<view>?from=rover(<site>,<drive>)&to=orbital(ImagePlacesDBIndex)
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_PLACESDB_INDEX")]
        public int ImagePlacesDBIndex { get; set; } = 0;

        //disable orbital DEM if PlacesDB metadata differs from OrbitalConfig or GeoTIFF
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_ENFORCE_PLACESDB_METADATA")]
        public bool EnforceDEMPlacesDBMetadata { get; set; } = false;

        //disable orbital image if PlacesDB metadata differs from OrbitalConfig or GeoTIFF
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_ENFORCE_PLACESDB_METADATA")]
        public bool EnforceImagePlacesDBMetadata { get; set; } = false;

        //load and use GeoTIFF metadata for orbital DEM
        //at least one of DEMIsGeoTIFF or DEMIsOrthographic must be true
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_IS_GEOTIFF")]
        public bool DEMIsGeoTIFF { get; set; } = true;

        //load and use GeoTIFF metadata for orbital image
        //at least one of ImageIsGeoTIFF or ImageIsOrthographic must be true
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_IS_GEOTIFF")]
        public bool ImageIsGeoTIFF { get; set; } = true;

        //if the orbital image is 8 bit then consider it to be in the sRGB colorspace
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_BYTE_IMAGE_IS_SRGB")]
        public bool ByteImageIsSRGB { get; set; } = true;

        //treat orbital DEM as orthographic
        //at least one of DEMIsGeoTIFF or DEMIsOrthographic must be true
        //GeoTIFF metadata is required if not; but even if so, GeoTIFF metadata is used if available
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_DEM_IS_ORTHOGRAPHIC")]
        public bool DEMIsOrthographic { get; set; } = true;

        //treat orbital image as orthographic
        //at least one of ImageIsGeoTIFF or ImageIsOrthographic must be true
        //GeoTIFF metadata is required if not; but even if so, GeoTIFF metadata is used if available
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_IMAGE_IS_ORTHOGRAPHIC")]
        public bool ImageIsOrthographic { get; set; } = true;

        //allow using expected (lon, lat) for landing site instead of value from PlacesDB
        [ConfigEnvironmentVariable("LANDFORM_ORBITAL_ALLOW_EXPECTED_LANDING_LON_LAT")]
        public bool AllowExpectedLandingLonLat { get; set; } = false;
    }
}

