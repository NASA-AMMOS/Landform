using System;
using System.Collections.Generic;
using JPLOPS.Util;
using JPLOPS.Imaging;

namespace JPLOPS.Pipeline
{
    public class MissionMSLConfig : SingletonConfig<MissionMSLConfig>
    {
        public const string CONFIG_FILENAME = "mission-msl"; //config file will be ~/.landform/mission-msl.json
        public override string ConfigFileName()
        {
            return CONFIG_FILENAME;
        }
        
        [ConfigEnvironmentVariable("LANDFORM_USE_UNIFIED_MESHES")]
        public bool UseUnifiedMeshes { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_PDS_LABEL_FILES")]
        public bool AllowPDSLabelFiles { get; set; } = true;
        
        [ConfigEnvironmentVariable("LANDFORM_ALLOW_LOCATIONS_DB")]
        public bool AllowLocationsDB { get; set; } = true;
        
        [ConfigEnvironmentVariable("LANDFORM_ALLOW_LEGACY_MANIFEST_DB")]
        public bool AllowLegacyManifestDB { get; set; } = true;

        //comma separated list of producers to allow
        //must match RoverProductProducer enum values
        //sorted in order of preference (best last)
        [ConfigEnvironmentVariable("LANDFORM_ALLOWED_PRODUCERS")]
        public string AllowedProducers { get; set; } = "OPGS";  //"OPGS,MSSS"
    }

    public class MissionMSL : MissionSpecific
    {
        public const int MIN_HAZ_EXPOSURE = 80;
        public const int MIN_MASTCAM_FOCUS_CUTOFF = 3;
        public const int MAX_MASTCAM_WIDTH = 1344; //TODO this is unused

        private readonly string[] ARMCAM_RDR_SUBDIRS = new string[] { "mhli" };

        public MissionMSL(string venue = null) : base(venue) { }

        public override Mission GetMission()
        {
            return Mission.MSL;
        }

        public override bool IsGeometricallyLinearlyCorrected(PDSParser parser)
        {
            //some msss msl images are labelled incorrectly: reporting raw in the metadata, 
            //when they are linearized and labelled correctly in the filename
            //example 0609MR0025690030401020E01_DRCL
            return (parser.GeometricProjection == RoverProductGeometry.Linearized) ||
                ((parser.ProducingInstitution == RoverProductProducer.MSSS) &&
                 (ParseProductId(parser.ProductIdString).Geometry == RoverProductGeometry.Linearized));
        }

        public override double GetSensorPixelSizeMM(RoverProductCamera camera)
        {
            switch (camera)
            {
                case RoverProductCamera.NavcamLeft:
                    return 0.012; //source Maki, J.N., et al., Mars Exploration Rover Engineering Cameras, J. Geophys. Res., 108(E12), 8071, doi:10.1029/2003JE002077, 2003. (navcam uses same CCD)
                case RoverProductCamera.NavcamRight:
                    return 0.012; //source Maki, J.N., et al., Mars Exploration Rover Engineering Cameras, J. Geophys. Res., 108(E12), 8071, doi:10.1029/2003JE002077, 2003. (navcam uses same CCD)
                case RoverProductCamera.MastcamLeft:
                    return 0.0074; //calculated
                case RoverProductCamera.MastcamRight:
                    return 0.0074; //calculated
                default:
                    throw new NotImplementedException("sensor pixel size for camera " + camera + " not added yet");
            }
        }

        public override double GetFocalLengthMM(RoverProductCamera camera)
        {
            switch (camera)
            {
                case RoverProductCamera.NavcamLeft:
                    return 14.67; //source SIS: https://pds-imaging.jpl.nasa.gov/data/msl/MSLNAV_0XXX/DOCUMENT/MSL_CAMERA_SIS_latest.PDF
                case RoverProductCamera.NavcamRight:
                    return 14.67; //source SIS: https://pds-imaging.jpl.nasa.gov/data/msl/MSLNAV_0XXX/DOCUMENT/MSL_CAMERA_SIS_latest.PDF
                case RoverProductCamera.MastcamLeft:
                    return 34.0; //https://www.lpi.usra.edu/meetings/lpsc2010/pdf/1123.pdf
                case RoverProductCamera.MastcamRight:
                    return 10.0; //https://www.lpi.usra.edu/meetings/lpsc2010/pdf/1123.pdf
                default:
                    throw new NotImplementedException("focal length for camera " + camera + " not added yet");
            }
        }

        public override double GetMinimumFocusDistance(PDSMetadata metadata)
        {
            if (metadata.ReadAsString("INSTRUMENT_HOST_ID") == "MSL")
            {
                if (metadata.HasKey("DERIVED_IMAGE_PARMS", "MSL:MINIMUM_FOCUS_DISTANCE"))
                {
                    double nearFocus = metadata.ReadAsDouble("DERIVED_IMAGE_PARMS", "MSL:MINIMUM_FOCUS_DISTANCE");

                    if (metadata.HasKey("INSTRUMENT_ID"))
                    {
                        string instrumentId = metadata.ReadAsString("INSTRUMENT_ID");

                        if (instrumentId.StartsWith("MAHLI"))
                        {
                            nearFocus /= 1000.0; //mahli is in millimeters
                        }
                    }
                    return nearFocus;
                }
            }
            return 0;
        }

        // Mastcam only
        public override double? GetMaximumFocusDistance(PDSMetadata metadata)
        {
            if (metadata.HasKey("DERIVED_IMAGE_PARMS", "MSL:MAXIMUM_FOCUS_DISTANCE"))
            {
                return metadata.ReadAsDouble("DERIVED_IMAGE_PARMS", "MSL:MAXIMUM_FOCUS_DISTANCE");
            }
            return null;
        }

        public override RoverMasker GetMasker()
        {
            return new MSLRoverMasker(this);
        }

        public override bool IsArmcam(RoverProductCamera cam)
        {
            return cam == RoverProductCamera.MAHLI;
        }

        public override string[] GetArmcamRDRSubdirs()
        {
            return ARMCAM_RDR_SUBDIRS;
        }

        public override bool UseUnifiedMeshes()
        {
            return MissionMSLConfig.Instance.UseUnifiedMeshes;
        }

        public override bool AllowPDSLabelFiles()
        {
            return MissionMSLConfig.Instance.AllowPDSLabelFiles;
        }

        public override bool AllowLocationsDB()
        {
            return MissionMSLConfig.Instance.AllowLocationsDB;
        }

        public override bool AllowLegacyManifestDB()
        {
            return MissionMSLConfig.Instance.AllowLegacyManifestDB;
        }

        public override RoverProductId ParseProductId(string id)
        {
            id = StringHelper.GetLastUrlPathSegment(id, stripExtension: true);

            //MSL unified mesh IDs can be from 32 to 36 chars long
            //Unfortunately regular MSL IDs are 36 chars long - first try as unified
            if (id.Length >= MSLUnifiedMeshProductId.MIN_LENGTH && id.Length <= MSLUnifiedMeshProductId.MAX_LENGTH)
            {
                var unified = MSLUnifiedMeshProductId.Parse(id);
                if (unified != null)
                {
                    return unified;
                }
            }

            switch (id.Length)
            {
                case MSLOPGSProductId.LENGTH: return MSLOPGSProductId.Parse(id);
                case MSLMSSSProductId.LENGTH: return MSLMSSSProductId.Parse(id);
                default: throw new Exception("unexpected length for MSL product id");
            }
        }

        public override bool CheckProductId(RoverProductId id, out string reason)
        {
            if (!base.CheckProductId(id, out reason))
            {
                return false;
            }

            if (id is MSLOPGSProductId)
            {
                MSLOPGSProductId opgsId = (MSLOPGSProductId)id;
                string spec = opgsId.Spec.ToUpper();
                if (spec != "T" && spec != "_")
                {
                    reason = "special processing " + spec;
                    return false;
                }
                    
                string cfg = opgsId.Config.ToUpper();
                if (IsMastcam(id.Camera) && id.ProductType == RoverProductType.Image && cfg != "F")
                {
                    reason = "mastcam raster config " + cfg;
                    return false;
                }

                if (id.Camera == RoverProductCamera.MAHLI && id.ProductType == RoverProductType.Image && cfg != "F")
                {
                    reason = "MAHLI raster config " + cfg;
                    return false;
                }
            }

            if (id is MSLMSSSProductId)
            {
                MSLMSSSProductId msssId = (MSLMSSSProductId)id;
                if (!msssId.RadiometricallyCalibrated || !msssId.ColorCorrected || !msssId.Decompressed)
                {
                    reason = "MSSS non-DCX files not allowed";
                    return false;
                }

                // check this is color or grayscale and not a thumbnail
                if (msssId.Color == RoverProductColor.Unknown)
                {
                    reason = "MSSS product color or size not allowed";
                    return false;
                }
            }

            return true;
        }

        public override bool CheckMetadata(PDSParser parser, out string reason)
        {
            if (!base.CheckMetadata(parser, out reason))
            {
                return false;
            }

            var cam = GetCamera(parser);

            if (IsHazcam(cam) && parser.ExposureDuration != 0 && parser.ExposureDuration < MIN_HAZ_EXPOSURE)
            {
                reason = "low exposure hazcam";
                return false;
            }

            if (IsMastcam(cam))
            {
                if (parser.FilterNumber != 0)
                {
                    reason = "mastcam with color filter";
                    return false;
                }

                double? maxFocusDistance = GetMaximumFocusDistance(parser.metadata as PDSMetadata);
                if (maxFocusDistance.HasValue && maxFocusDistance < MIN_MASTCAM_FOCUS_CUTOFF)
                {
                    // (probably closeup of rover part with terrain out of focus in background)
                    reason = "mastcam with short focal distance";
                    return false;
                }

                if(parser.ExposureDuration > 50)
                {
                    //images with these long exposures seem to be too dark for use in context mesh
                    reason = "mastcam with long exposure";
                    return false;
                }
            }

            if (IsNavcam(cam) && parser.IsDownsampled)
            {
                reason = "downsampled navcam";
                return false;
            }

            return true;
        }

        public override string GetOrbitalConfigDefaults()
        {
            //TODO possibly switch to newer assets
            //though at least for Windjana the older assets (out_XXX.tif) seem to have more definition
            //s3://bucket/MSL/orbital/MSL_Gale_DEM_Mosaic_1m_v3.tif
            //s3://bucket/MSL/orbital/MSL_Gale_Orthophoto_Mosaic_25cm_v3.tif

            //TODO possibly switch to color orbital
            //MSL_Gale_HiRISE-LRGB_16quads.tif

            //available PlacesDB metadata entries, from "wardialing" https://places-msl.dev.m20.jpl.nasa.gov
            //0 - seems to be generic entry, has ellipsoid_radius, projection, and a few others, but not {x,y}_scale
            //1 - MSL_Gale_MSLICE_HIRISE_Mosaic_25cm.tif
            //2 - MSL_Gale_MSLICE_HIRISE_Mosaic_1m.tif
            //3 - Gale_DTM_geoid_1m.tif (8 bit int)
            //4 - Gale_DTM_geoid_1m.tif (32 bit float)
            //5 - MSL_Gale_Orthophoto_Mosaic_25cm_v3.tif
            //6 - MSL_Gale_DEM_Mosaic_1m_v3.tif
            //7 - MSL_Gale_HiRISE-LRGB_16quads.tif
            //there do not seem to be entries for the old "out_XXX.tif" assets
            
            return "{\n" +
                "\"DEMURL\": \"s3://bucket/MSL/orbital/out_deltaradii_smg_1m.tif\",\n" +
                "\"ImageURL\": \"s3://bucket/MSL/orbital/out_clean_25cm.iGrid.ClipToDEM.tif\",\n" +
                "\"StoragePath\": \"MSL/orbital\",\n" +
                "\"DEMMetersPerPixel\": 1,\n" +
                "\"ImageMetersPerPixel\": 0.25,\n" +
                "\"DEMPlacesDBIndex\": 0,\n" +
                "\"ImagePlacesDBIndex\": 0\n" +
                "}";
        }

        public override string GetPlacesConfigDefaults()
        {
            //MSL mission server - don't use for dev (and requires separate credentials)
            //https://mslplaces.jpl.nasa.gov:9443/msl-ops/places

            //MSL views: telemetry, best_tactical, localized_pos, localized_interp 
            //legacy TerrainTools used localized_interp

            //for a while we had to use this old PLACES instance to get MSL data for M2020 dev
            //Url: https://places-dev.m20-dev.jpl.nasa.gov
            //AuthCookieFile: ~/.cssotoken/dev-old/ssosession

            //per Kevin Grimes on 3/11/20 https://places.dev.m20.jpl.nasa.gov has MSL data in it

            //per Kevin Grimes on 3/18/20 MSL data will soon move to
            //https://places-msl.dev.m20.jpl.nasa.gov

            return "{\n" +
                $"\"Url\": \"https://places-msl.{venue}.m20.jpl.nasa.gov\",\n" +
                "\"View\": \"localized_interp\",\n" +
                "\"AlwaysCheckRMC\": false,\n" +
                "\"AuthCookieName\": \"ssosession\",\n" +
                $"\"AuthCookieFile\": \"~/.cssotoken/{venue}/ssosession\"\n" +
                "}";
        }

        public override RoverProductGeometry GetTacticalMeshGeometry()
        {
            return RoverProductGeometry.Linearized;
        }

        public override List<RoverProductProducer> GetAllowedProducers()
        {
            return GetAllowedProducers(MissionMSLConfig.Instance.AllowedProducers);
        }
    }
}
