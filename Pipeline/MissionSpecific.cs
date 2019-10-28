using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public enum Mission { None, MSL, M2020, ROASTT19 }

    public abstract class MissionSpecific
    {
        public static MissionSpecific GetInstance(Mission mission)
        {
            switch (mission)
            {
                case Mission.None: return null;
                case Mission.MSL: return new MissionMSL();
                case Mission.M2020: return new MissionM2020();
                case Mission.ROASTT19: return new MissionROASTT19();
                default: throw new NotImplementedException("unknown mission");
            }
        }

        public static MissionSpecific GetInstance(string mission)
        {
            return GetInstance((Mission)Enum.Parse(typeof(Mission), mission, ignoreCase: false));
        }

        public virtual string RootFrameName()
        {
            return "root";
        }

        public virtual string RoverMotionCounter(PDSParser parser)
        {
            return parser.RMC;
        }

        public virtual int DayNumber(PDSParser parser)
        {
            return parser.PlanetDayNumber;
        }

        public virtual RoverProductCamera TranslateCamera(RoverProductCamera cam)
        {
            return cam;
        }

        public virtual RoverProductCamera GetCamera(string instrumentId)
        {
            return TranslateCamera(RoverCamera.FromPDSInstrumentID(instrumentId));
        } 

        public virtual RoverProductCamera GetCamera(PDSParser parser)
        {
            return GetCamera(parser.InstrumentId);
        }

        public virtual string GetObservationFrameName(PDSParser parser)
        {
            return string.Format("{0}_{1}", GetCamera(parser), RoverMotionCounter(parser));
        }
        
        public virtual bool IsGeometricallyLinearlyCorrected(PDSParser parser)
        {
            return parser.GeometricProjection == RoverProductGeometry.Linearized;
        }
      
        public abstract double GetSensorPixelSizeMM(RoverProductCamera camera);

        public abstract double GetFocalLengthMM(RoverProductCamera camera);

        public abstract double GetMinimumFocusDistance(PDSMetadata metadata);

        public abstract double? GetMaximumFocusDistance(PDSMetadata metadata);

        /// <summary>
        /// ordering a sequence with this function should put the "better" observations earlier in the list
        /// thus a "better" observation should be *less than* a "worse" observation
        /// uses PreferMSSSToOPGS() and PreferLinearToNonlinear()
        /// so if a mission only differs from the default in one of those respects, just override that
        /// </summary>
        public virtual RoverObservationComparator GetRoverObservationComparator()
        {
            return new RoverObservationComparator(PreferMSSSToOPGS(), PreferLinearToNonlinear());
        }

        public virtual RoverProductGeometry[] GetLinearPreference()
        {
            if (!AllowLinear() && !AllowNonlinear())
            {
                return new RoverProductGeometry[] {}; //yeah...
            }

            if (!AllowLinear())
            {
                return new RoverProductGeometry[] { RoverProductGeometry.Raw };
            }

            if (!AllowNonlinear())
            {
                return new RoverProductGeometry[] { RoverProductGeometry.Linearized };
            }

            if (PreferLinearToNonlinear())
            {
                return new RoverProductGeometry[] { RoverProductGeometry.Linearized, RoverProductGeometry.Raw };
            }

            return new RoverProductGeometry[] { RoverProductGeometry.Raw, RoverProductGeometry.Linearized };
        }

        public abstract RoverMasker GetMasker();

        public virtual bool IsNavcam(RoverProductCamera camera)
        {
           return camera == RoverProductCamera.NavcamLeft || camera == RoverProductCamera.NavcamRight;
        }

        public virtual bool IsHazcam(RoverProductCamera camera)
        {
                return camera == RoverProductCamera.FrontHazcamLeft
                    || camera == RoverProductCamera.FrontHazcamRight
                    || camera == RoverProductCamera.RearHazcamLeft
                    || camera == RoverProductCamera.RearHazcamRight;
        }

        public virtual bool IsMastcam(RoverProductCamera camera)
        {
           return camera == RoverProductCamera.MastcamLeft || camera == RoverProductCamera.MastcamRight;
        }

        public abstract bool IsArmcam(RoverProductCamera camera);

        public virtual string ClassifyCamera(RoverProductCamera cam)
        {
            if (IsHazcam(cam))
            {
                return "hazcam";
            }
            else if (IsNavcam(cam))
            {
                return "navcam";
            }
            else if (IsMastcam(cam))
            {
                return "mastcam";
            }
            else if (IsArmcam(cam))
            {
                return "armcam";
            }
            else
            {
                return cam.ToString();
            }
        }

        public virtual string ClassifyCamera(string cam)
        {
            return ClassifyCamera((RoverProductCamera)Enum.Parse(typeof(RoverProductCamera), cam, ignoreCase: true));
        }

        /// <summary>
        /// whether to allow priors from MSLLocations
        /// </summary>
        public virtual bool AllowLocationsDB()
        {
            return false;
        }

        /// <summary>
        /// whether to allow priors from the Places database
        /// </summary>
        public virtual bool AllowPlacesDB()
        {
            return true;
        }
             
        /// <summary>
        /// whether to allow priors from the OnSight legacy manifest
        /// </summary>
        public virtual bool AllowLegacyManifestDB()
        {
            return false;
        }

        /// <summary>
        /// whether to ingest OPGS images
        /// </summary>
        public virtual bool AllowOPGS()
        {
            return true;
        }

        /// <summary>
        /// whether to ingest MSSS images
        /// </summary>
        public virtual bool AllowMSSS()
        {
            return true;
        }

        /// <summary>
        /// whether to ingest thumbnail images
        /// </summary>
        public virtual bool AllowThumbnails()
        {
            return false;
        }

        /// <summary>
        /// whether to ingest partially downloaded images
        /// </summary>
        public virtual bool AllowPartialDownloads()
        {
            return false;
        }

        /// <summary>
        /// whether to ingest sun finding images
        /// </summary>
        public virtual bool AllowSunFinding()
        {
            return false;
        }

        /// <summary>
        /// whether to ingest linearized images
        /// </summary>
        public virtual bool AllowLinear()
        {
            return true;
        }

        /// <summary>
        /// whether to ingest non-linearized images
        /// ISSUE #353: need to validate that alignment works across cameras with non-linearized images
        /// </summary>
        public virtual bool AllowNonlinear()
        {
            return false;
        }

        /// <summary>
        /// whether to prefer MSSS images to OPGS images when both are available
        /// </summary>
        public virtual bool PreferMSSSToOPGS()
        {
            return false;
        }

        /// <summary>
        /// whether to prefer linear to nonlinear images when both are available
        /// </summary>
        public virtual bool PreferLinearToNonlinear()
        {
            return true;
        }

        public virtual bool AllowRoverMasks()
        {
            return true; //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/755
        }

        public virtual bool AllowErrorMaps()
        {
            return false; //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/500
        }

        public virtual bool UseHazcamForAlignment()
        {
            return true; //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/328
        }

        public virtual bool UseHazcamForMeshing()
        {
            return true;
        }

        public virtual bool UseHazcamForTexturing()
        {
            return true; //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/729
        }

        public virtual bool UseNavcamForAlignment()
        {
            return true;
        }

        public virtual bool UseNavcamForMeshing()
        {
            return true;
        }

        public virtual bool UseNavcamForTexturing()
        {
            return true;
        }

        public virtual bool UseMastcamForAlignment()
        {
            return false; //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/261
        }

        public virtual bool UseMastcamForMeshing()
        {
            return false; //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/261
        }

        public virtual bool UseMastcamForTexturing()
        {
            return true;
        }

        public virtual bool UseArmcamForAlignment()
        {
            return false;
        }

        public virtual bool UseArmcamForMeshing()
        {
            return false;
        }

        public virtual bool UseArmcamForTexturing()
        {
            return false; //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/756
        }

        public virtual bool UseForAlignment(PDSParser parser)
        {
            var cam = GetCamera(parser.InstrumentId);
            return (IsHazcam(cam) && UseHazcamForAlignment()) ||
                (IsNavcam(cam) && UseNavcamForAlignment()) ||
                (IsMastcam(cam) && UseMastcamForAlignment()) ||
                (IsArmcam(cam) && UseArmcamForAlignment());
        }

        public virtual bool UseForMeshing(PDSParser parser)
        {
            var cam = GetCamera(parser.InstrumentId);
            return (IsHazcam(cam) && UseHazcamForMeshing()) ||
                (IsNavcam(cam) && UseNavcamForMeshing()) ||
                (IsMastcam(cam) && UseMastcamForMeshing()) ||
                (IsArmcam(cam) && UseArmcamForMeshing());
        }

        public virtual bool UseForTexturing(PDSParser parser)
        {
            var cam = GetCamera(parser.InstrumentId);
            return (IsHazcam(cam) && UseHazcamForTexturing()) ||
                (IsNavcam(cam) && UseNavcamForTexturing()) ||
                (IsMastcam(cam) && UseMastcamForTexturing()) ||
                (IsArmcam(cam) && UseArmcamForTexturing());
        }

        public virtual bool AllowCamera(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && (UseHazcamForAlignment() || UseHazcamForMeshing() || UseHazcamForTexturing())) ||
                (IsNavcam(cam) && (UseNavcamForAlignment() || UseNavcamForMeshing() || UseNavcamForTexturing())) ||
                (IsMastcam(cam) && (UseMastcamForAlignment() || UseMastcamForMeshing() || UseMastcamForTexturing())) ||
                (IsArmcam(cam) && (UseArmcamForAlignment() || UseArmcamForMeshing() || UseArmcamForTexturing()));
        }

        public virtual bool AllowRasterProducts(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && (UseHazcamForAlignment() || UseHazcamForTexturing())) ||
                (IsNavcam(cam) && (UseNavcamForAlignment() || UseNavcamForTexturing())) ||
                (IsMastcam(cam) && (UseMastcamForAlignment() || UseMastcamForTexturing())) ||
                (IsArmcam(cam) && (UseArmcamForAlignment() || UseArmcamForTexturing()));
        }

        public virtual bool AllowGeometryProducts(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && (UseHazcamForAlignment() || UseHazcamForMeshing())) ||
                (IsNavcam(cam) && (UseNavcamForAlignment() || UseNavcamForMeshing())) ||
                (IsMastcam(cam) && (UseMastcamForAlignment() || UseMastcamForMeshing())) ||
                (IsArmcam(cam) && (UseArmcamForAlignment() || UseArmcamForMeshing()));
        }

        public virtual bool AllowProduct(RoverProductCamera cam, RoverProductType prodType)
        {
            if (!AllowCamera(cam))
            {
                return false;
            }
            if (RoverProduct.IsMask(prodType) && !AllowRoverMasks())
            {
                return false;
            }
            if (RoverProduct.IsErrorMap(prodType) && !AllowErrorMaps())
            {
                return false;
            }
            //careful here - consider e.g. that a mask may be both a raster and geometry product
            return ((RoverProduct.IsRaster(prodType) && AllowRasterProducts(cam)) ||
                    (RoverProduct.IsGeometry(prodType) && AllowGeometryProducts(cam)));
        }

        /// <summary>
        /// Check if we should even bother downloading or ingesting based on filename.
        /// uses the Allow*() APIs so missions can specialize by just overriding those
        /// </summary>
        public virtual bool CheckFilename(string filename, out string reason)
        {
            return CheckProductId(RoverProductId.Parse(filename), out reason);
        }

        public bool CheckFilename(string filename)
        {
            return CheckFilename(filename, out string reason);
        }

        public virtual bool CheckProductId(RoverProductId id, out string reason)
        {
            reason = "";

            if (id == null)
            {
                reason = "failed to parse product id";
                return false;
            }

            if (id.Camera == RoverProductCamera.Unknown)
            {
                reason = "unknown camera";
                return false;
            }

            if (id.ProductType == RoverProductType.Unknown)
            {
                reason = "unknown product type";
                return false;
            }

            if (id.Producer == RoverProductProducer.Unknown)
            {
                reason = "unknown producer";
                return false;
            }

            if (id.Geometry == RoverProductGeometry.Unknown)
            {
                reason = "unknown image geometry";
                return false;
            }

            if (!AllowCamera(id.Camera))
            {
                reason = string.Format("camera {0} not allowed", id.Camera);
                return false;
            }

            if (!AllowProduct(id.Camera, id.ProductType))
            {
                reason = string.Format("{0} {1} products not allowed", id.Camera, id.ProductType);
                return false;
            }

            if (!AllowOPGS() && id.Producer == RoverProductProducer.OPGS)
            {
                reason = string.Format("producer {0} not allowed", id.Producer.ToString());
                return false;
            }

            if (!AllowMSSS() && id.Producer == RoverProductProducer.MSSS)
            {
                reason = string.Format("producer {0} not allowed", id.Producer.ToString());
                return false;
            }

            if (!AllowThumbnails() && id.Producer == RoverProductProducer.OPGS &&
                ((OPGSProductId)id).Size != RoverProductSize.Regular)
            {
                reason = "thumbnails not allowed";
                return false;
            }

            if (!AllowLinear() && id.Geometry == RoverProductGeometry.Linearized)
            {
                reason = "linearized images not allowed";
                return false;
            }

            if (!AllowNonlinear() && id.Geometry != RoverProductGeometry.Linearized)
            {
                reason = "nonlinear images not allowed";
                return false;
            }

            return true;
        }

        public virtual bool CheckProductId(RoverProductId id)
        {
            return CheckProductId(id, out string reason);
        }

        /// <summary>
        /// Mostly just confirms what CheckFilename() did using metadata instead of the filename
        /// but some things are only checked by one or the other
        /// uses the Allow*() APIs so missions can specialize by just overriding those
        /// </summary>
        public virtual bool CheckMetadata(PDSParser parser, out string reason)
        {
            reason = "";

            var cam = GetCamera(parser.InstrumentId);
            if (cam == RoverProductCamera.Unknown)
            {
                reason = "unknown camera " + parser.InstrumentId;
                return false;
            }

            var pt = parser.DerivedImageType;
            if (pt == RoverProductType.Unknown)
            {
                reason = "unknown product type";
                return false;
            }

            if (!AllowCamera(cam))
            {
                reason = string.Format("camera {0} not allowed", cam);
                return false;
            }

            if (!AllowProduct(cam, pt))
            {
                reason = string.Format("{0} {1} products not allowed", cam, pt);
                return false;
            }

            if (!AllowPartialDownloads() && parser.IsPartial)
            {
                reason = "partial downloads not allowed";
                return false;
            }

            if (parser.metadata.Bands != 3 && parser.metadata.Bands != 1)
            {
                reason = "only 1 or 3 band images allowed";
                return false;
            }

            if (!AllowOPGS() && parser.ProducingInstitution == RoverProductProducer.OPGS)
            {
                reason = "OPGS images not allowed";
                return false;
            }

            if (!AllowMSSS() && parser.ProducingInstitution == RoverProductProducer.MSSS)
            {
                reason = "MSSS images not allowed";
                return false;
            }

            if (!AllowThumbnails() && parser.ImageSizeType != RoverProductSize.Regular)
            {
                reason = "thumbnail images not allowed";
                return false;
            }

            if (!AllowLinear() && IsGeometricallyLinearlyCorrected(parser))
            {
                reason = "linearized images not allowed";
                return false;
            }

            if (!AllowNonlinear() && !IsGeometricallyLinearlyCorrected(parser))
            {
                reason = "nonlinear images not allowed";
                return false;
            }

            if (!AllowSunFinding() && parser.IsSunFinding)
            {
                reason = "sun finding images not allowed";
                return false;
            }

            return true;
        }

        public virtual bool CheckMetadata(PDSParser parser)
        {
            return CheckMetadata(parser, out string reason);
        }
    }

    public class MissionMSL : MissionSpecific
    {
        public const int MIN_HAZ_EXPOSURE = 80;
        public const int MIN_MASTCAM_FOCUS_CUTOFF = 3;
        public const int MAX_MASTCAM_WIDTH = 1344; //TODO this is unused

        public override bool IsGeometricallyLinearlyCorrected(PDSParser parser)
        {
            //some msss msl images are labelled incorrectly: reporting raw in the metadata, 
            //when they are linearized and labelled correctly in the filename
            //example 0609MR0025690030401020E01_DRCL
            return (parser.GeometricProjection == RoverProductGeometry.Linearized) ||
                ((parser.ProducingInstitution == RoverProductProducer.MSSS) &&
                 (parser.ProductId.Geometry == RoverProductGeometry.Linearized));
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

        public override bool AllowLocationsDB()
        {
            return true;
        }

        public override bool AllowLegacyManifestDB()
        {
            return true;
        }

        public override bool CheckProductId(RoverProductId id, out string reason)
        {
            if (!base.CheckProductId(id, out reason))
            {
                return false;
            }

            if (id.Producer == RoverProductProducer.OPGS)
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

            if (id.Producer == RoverProductProducer.MSSS)
            {
                // Check that this is a DCX file
                MSLMSSSProductId msssId = (MSLMSSSProductId)id;
                if (!msssId.RadiometricallyCalibrated || !msssId.ColorCorrected || !msssId.Decompressed)
                {
                    reason = "MSSS non-DCX files not allowed";
                    return false;
                }
                // Filter for color or black and white jpegs that are not thumbnails
                if (msssId.MSSSProductType == MSSSProductType.Unknown)
                {
                    reason = "MSSS product type unknown";
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

            var cam = GetCamera(parser.InstrumentId);

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
    }

    public class MissionM2020 : MissionSpecific
    {
        //some images have invalid PLANET_DAY_NUMBER
        //we have seen this in multiple M2020 datasets so far including ROASTT19 and TT4
        public override int DayNumber(PDSParser parser)
        {
            try
            {
                return parser.PlanetDayNumber;
            }
            catch (MetadataException)
            {
                return ((M2020OPGSProductId)parser.ProductId).GetDayNumber();
            }
        }

        public override RoverProductCamera TranslateCamera(RoverProductCamera cam)
        {
            switch (cam)
            {
                //ML and MR in RDR product names for M2020 really mean MastcamZ not Mastcam
                //and in any case M2020 has only MastcamZ not Mastcam
                case RoverProductCamera.MastcamLeft: return RoverProductCamera.MastcamZLeft;
                case RoverProductCamera.MastcamRight: return RoverProductCamera.MastcamZRight;
                default: return cam;
            }
        }

        public override double GetSensorPixelSizeMM(RoverProductCamera camera) {
            throw new NotImplementedException("sensor pixels size not implemented for 2020 instruments yet");
        }

        public override double GetFocalLengthMM(RoverProductCamera rovProdCam)
        {
            throw new NotImplementedException("focal lengths not implemented for 2020 instruments yet");
        }

        public override double GetMinimumFocusDistance(PDSMetadata metadata)
        {
            throw new NotImplementedException("min focus distance not implemented for 2020 instruments yet");
        }

        public override double? GetMaximumFocusDistance(PDSMetadata metadata)
        {
            throw new NotImplementedException("max focus distance not implemented for 2020 instruments yet");
        }

        public override RoverMasker GetMasker()
        {
            return new M2020RoverMasker(this); //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/554
        }

        public override bool IsHazcam(RoverProductCamera camera)
        {
            return base.IsHazcam(camera) ||
                camera == RoverProductCamera.FrontHazcamLeftB || camera == RoverProductCamera.FrontHazcamRightB;
        }

        public override bool IsMastcam(RoverProductCamera camera)
        {
            return base.IsMastcam(camera) ||
                camera == RoverProductCamera.MastcamZLeft || camera == RoverProductCamera.MastcamZRight;
        }

        public override bool IsArmcam(RoverProductCamera camera)
        {
            return camera == RoverProductCamera.PIXELMCC;
        }

        public override bool AllowPlacesDB()
        {
            return false; //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/535
        }

        public override bool CheckProductId(RoverProductId id, out string reason)
        {
            if (!base.CheckProductId(id, out reason))
            {
                return false;
            }

            if (id.Producer == RoverProductProducer.OPGS)
            {
                M2020OPGSProductId opgsId = (M2020OPGSProductId)id;
                string spec = opgsId.Spec.ToUpper();
                if (spec != "_")
                {
                    reason = "special processing " + spec;
                    return false;
                }

                //TODO check other Spec values, ColorFilter, Camspec, Downsample, Compression
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/754
            }

            if (id.Producer == RoverProductProducer.MSSS)
            {
                //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/754
            }

            return true;
        }
    }

    public class MissionROASTT19 : MissionM2020 
    {
        // ROASTT19: bug prevents RMC from being used for frame names. This workaround
        // will break multiple images with different filters resolving to same frame.
        public override string RoverMotionCounter(PDSParser parser)
        {          
            return ((M2020OPGSProductId)parser.ProductId).GetConcatenatedTimeString();
        }

        // ROASTT19: for some images the INSTRUMENT_ID says LEFT when it should say RIGHT, so use PRODUCT_ID instead
        public override RoverProductCamera GetCamera(PDSParser parser)
        {
            return TranslateCamera(parser.ProductId.Camera);
        }
    }
}
