using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    public class MissionMSL : MissionSpecific
    {
        public const int MIN_HAZ_EXPOSURE = 80;
        public const int MIN_MASTCAM_FOCUS_CUTOFF = 3;
        public const int MAX_MASTCAM_WIDTH = 1344; //TODO this is unused

        public override Mission GetMission()
        {
            return Mission.MSL;
        }

        public override RoverProductType GetProductType(PDSParser parser)
        {
            var pt = parser.DerivedImageType;
            if (pt == RoverProductType.Unknown && parser.ProducingInstitution == RoverProductProducer.MSSS)
            {
                pt = GetProductType(parser.ProductIdString);
            }
            return pt;
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

        public override bool AllowPDSLabelFiles()
        {
            return true;
        }

        public override bool AllowLocationsDB()
        {
            return true;
        }

        public override bool AllowLegacyManifestDB()
        {
            return true;
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
                if (!parser.FilterNumber.HasValue || parser.FilterNumber != 0)
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
            //TODO switch to newer assets
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/1011#issuecomment-269256
            //s3://m20-ids-g-landform/MSL/orbital/MSL_Gale_DEM_Mosaic_1m_v3.tif
            //s3://m20-ids-g-landform/MSL/orbital/MSL_Gale_Orthophoto_Mosaic_25cm_v3.tif

            //TODO possibly switch to color orbital
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/1017
            //MSL_Gale_HiRISE-LRGB_16quads.tif

            return "{ " +
                "\"OrbitalDEMURL\": \"s3://m20-ids-g-landform/MSL/orbital/out_deltaradii_smg_1m.tif\", " +
                "\"OrbitalImageURL\": \"s3://m20-ids-g-landform/MSL/orbital/out_clean_25cm.iGrid.ClipToDEM.tif\", " +
                "\"OrbitalDEMStoragePath\": \"MSL/orbital/out_deltaradii_smg_1m.tif\", " +
                "\"OrbitalImageStoragePath\": \"MSL/orbital/out_clean_25cm.iGrid.ClipToDEM.tif\"" +
                " }";
        }

        public override string GetPlacesConfigDefaults()
        {
            //MSL mission server - don't use for dev (and requires separate credentials)
            //https://mslplaces.jpl.nasa.gov:9443/msl-ops/places

            //MSL views: best_tactical, localized_pos, localized_interp 
            //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/921
            //currently we default to best_tactical but legacy TerrainTools used localized_interp

            //note https://github.jpl.nasa.gov/OnSight/Landform/wiki/ZZZ-OLD-Credss-workaround-for-MSL-PLACES
            //for a while we had to use this old PLACES instance to get MSL data for M2020 dev
            //Url: https://places-dev.m20-dev.jpl.nasa.gov
            //AuthCookieFile: ~/.cssotoken/dev-old/ssosession

            //https://github.jpl.nasa.gov/OnSight/Landform/issues/725#issuecomment-265961
            //per Kevin Grimes on 3/11/20 https://places.dev.m20.jpl.nasa.gov has MSL data in it

            //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/725#issuecomment-267319
            //per Kevin Grimes on 3/18/20 MSL data will soon move to
            //https://places-msl.dev.m20.jpl.nasa.gov

            return
                "{ " +
                "\"Url\": \"https://places.dev.m20.jpl.nasa.gov\", " +
                "\"View\": \"best_tactical\", " +
                "\"AuthCookieName\": \"ssosession\", " +
                "\"AuthCookieFile\": \"~/.cssotoken/dev/ssosession\"" +
                "}";
        }
    }
}
