using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public enum Mission { None, MSL, M2020, ROASTT19, TT4 }

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
                case Mission.TT4: return new MissionTT4();
                default: throw new NotImplementedException("unknown mission");
            }
        }

        public static MissionSpecific GetInstance(string mission)
        {
            return GetInstance((Mission)Enum.Parse(typeof(Mission), mission, ignoreCase: false));
        }

        public abstract Mission GetMission();

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

        public virtual RoverProductType GetProductType(string productId)
        {
            return RoverProductId.Parse(productId, this).ProductType;
        } 

        public virtual RoverProductType GetProductType(PDSParser parser)
        {
            return parser.DerivedImageType;
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
        /// uses PreferMSSSToOPGS(), PreferLinearToNonlinear(), PreferColorToGrayscale()
        /// so if a mission only differs from the default in one of those respects, just override that
        /// </summary>
        public virtual RoverObservationComparator GetRoverObservationComparator()
        {
            return new RoverObservationComparator(PreferMSSSToOPGS(),
                                                  PreferLinearToNonlinear(),
                                                  PreferColorToGrayscale(),
                                                  PreferEyeForGeometry());
        }

        /// <summary>
        /// see RoverObservationComparator.FilterProductIdGroups()  
        /// </summary>
        public virtual IEnumerable<RoverProductId> FilterProductIdGroups(IEnumerable<RoverProductId> products)
        {
            return products;
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

        public virtual RoverStereoEye PreferEyeForGeometry()
        {
            return RoverStereoEye.Left;
        }

        public abstract RoverMasker GetMasker();

        public virtual bool IsNavcam(RoverProductCamera camera)
        {
           return camera == RoverProductCamera.Navcam ||
               camera == RoverProductCamera.NavcamLeft || camera == RoverProductCamera.NavcamRight;
        }

        public virtual bool IsHazcam(RoverProductCamera camera)
        {
                return camera == RoverProductCamera.Hazcam ||
                    camera == RoverProductCamera.FrontHazcamLeft ||
                    camera == RoverProductCamera.FrontHazcamRight ||
                    camera == RoverProductCamera.RearHazcamLeft ||
                    camera == RoverProductCamera.RearHazcamRight;
        }

        public virtual bool IsMastcam(RoverProductCamera camera)
        {
           return camera == RoverProductCamera.Mastcam ||
               camera == RoverProductCamera.MastcamLeft || camera == RoverProductCamera.MastcamRight;
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
        /// whether to allow multi-frame products such as unified meshes
        /// </summary>
        public virtual bool AllowMultiFrameProducts()
        {
            return true;
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

        /// <summary>
        /// whether to prefer color images to bw when both are available
        /// </summary>
        public virtual bool PreferColorToGrayscale()
        {
            return true;
        }

        public virtual bool UseRoverMasks()
        {
            return true; //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/755
        }

        public virtual bool UseErrorMaps()
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
            var cam = GetCamera(parser);
            return (IsHazcam(cam) && UseHazcamForAlignment()) ||
                (IsNavcam(cam) && UseNavcamForAlignment()) ||
                (IsMastcam(cam) && UseMastcamForAlignment()) ||
                (IsArmcam(cam) && UseArmcamForAlignment());
        }

        public virtual bool UseForMeshing(PDSParser parser)
        {
            var cam = GetCamera(parser);
            return (IsHazcam(cam) && UseHazcamForMeshing()) ||
                (IsNavcam(cam) && UseNavcamForMeshing()) ||
                (IsMastcam(cam) && UseMastcamForMeshing()) ||
                (IsArmcam(cam) && UseArmcamForMeshing());
        }

        public virtual bool UseForTexturing(PDSParser parser)
        {
            var cam = GetCamera(parser);
            return (IsHazcam(cam) && UseHazcamForTexturing()) ||
                (IsNavcam(cam) && UseNavcamForTexturing()) ||
                (IsMastcam(cam) && UseMastcamForTexturing()) ||
                (IsArmcam(cam) && UseArmcamForTexturing());
        }

        public virtual bool UseCamera(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && (UseHazcamForAlignment() || UseHazcamForMeshing() || UseHazcamForTexturing())) ||
                (IsNavcam(cam) && (UseNavcamForAlignment() || UseNavcamForMeshing() || UseNavcamForTexturing())) ||
                (IsMastcam(cam) && (UseMastcamForAlignment() || UseMastcamForMeshing() || UseMastcamForTexturing())) ||
                (IsArmcam(cam) && (UseArmcamForAlignment() || UseArmcamForMeshing() || UseArmcamForTexturing()));
        }

        public virtual bool UseRasterProducts(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && (UseHazcamForAlignment() || UseHazcamForTexturing())) ||
                (IsNavcam(cam) && (UseNavcamForAlignment() || UseNavcamForTexturing())) ||
                (IsMastcam(cam) && (UseMastcamForAlignment() || UseMastcamForTexturing())) ||
                (IsArmcam(cam) && (UseArmcamForAlignment() || UseArmcamForTexturing()));
        }

        public virtual bool UseGeometryProducts(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && (UseHazcamForAlignment() || UseHazcamForMeshing())) ||
                (IsNavcam(cam) && (UseNavcamForAlignment() || UseNavcamForMeshing())) ||
                (IsMastcam(cam) && (UseMastcamForAlignment() || UseMastcamForMeshing())) ||
                (IsArmcam(cam) && (UseArmcamForAlignment() || UseArmcamForMeshing()));
        }

        public virtual bool UseProduct(RoverProductCamera cam, RoverProductType prodType)
        {
            if (!UseCamera(cam))
            {
                return false;
            }
            if (RoverProduct.IsMask(prodType) && !UseRoverMasks())
            {
                return false;
            }
            if (RoverProduct.IsErrorMap(prodType) && !UseErrorMaps())
            {
                return false;
            }
            //careful here - consider e.g. that a mask may be both a raster and geometry product
            return ((RoverProduct.IsRaster(prodType) && UseRasterProducts(cam)) ||
                    (RoverProduct.IsGeometry(prodType) && UseGeometryProducts(cam)));
        }

        /// <summary>
        /// uses the Allow*() APIs so missions can specialize by just overriding those
        /// </summary>
        public virtual bool CheckProductId(RoverProductId id, out string reason)
        {
            reason = "";

            if (id == null)
            {
                reason = "failed to parse product id";
                return false;
            }

            if (id.ProductType == RoverProductType.Unknown)
            {
                reason = "unknown product type";
                return false;
            }

            if (!id.IsSingleFrame() && !AllowMultiFrameProducts())
            {
                reason = "multi frame products (e.g. unified meshes) not allowed";
                return false;
            }

            if (!id.IsSingleSiteDrive())
            {
                reason = "multi site-drive products (e.g. unified meshes) not allowed";
                return false;
            }

            if (!id.IsSingleCamera())
            {
                reason = "multi camera products (e.g. unified meshes) not allowed";
                return false;
            }

            if (id.Camera == RoverProductCamera.Unknown)
            {
                reason = "unknown camera";
                return false;
            }

            if (!UseCamera(id.Camera))
            {
                reason = string.Format("camera {0} not allowed", id.Camera);
                return false;
            }

            if (!UseProduct(id.Camera, id.ProductType))
            {
                reason = string.Format("{0} {1} products not allowed", id.Camera, id.ProductType);
                return false;
            }

            if (id.Producer == RoverProductProducer.Unknown)
            {
                reason = "unknown producer";
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

            if (id.Geometry == RoverProductGeometry.Unknown)
            {
                reason = "unknown image geometry";
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

        public virtual IEnumerable<int[]> GetProductIdVariantSpans(RoverProductId id)
        {
            if (id.GetVersionSpan(out int start, out int length))
            {
                yield return new int[] { start, length };
            }
            yield break;
        }

        /// <summary>
        /// Mostly just confirms what CheckFilename() did using metadata instead of the filename
        /// but some things are only checked by one or the other
        /// uses the Allow*() APIs so missions can specialize by just overriding those
        /// </summary>
        public virtual bool CheckMetadata(PDSParser parser, out string reason)
        {
            reason = "";

            var cam = GetCamera(parser);
            if (cam == RoverProductCamera.Unknown)
            {
                reason = "unknown camera " + parser.InstrumentId;
                return false;
            }

            var pt = GetProductType(parser);
            if (pt == RoverProductType.Unknown)
            {
                reason = "unknown product type";
                return false;
            }

            if (!UseCamera(cam))
            {
                reason = string.Format("camera {0} not allowed", cam);
                return false;
            }

            if (!UseProduct(cam, pt))
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

        public virtual string GetDefaultAWSRegion()
        {
            return "us-gov-west-1";
        }

        public virtual string GetDefaultAWSProfile()
        {
            return "credss-default";
        }

        /// <summary>
        /// Get mission specific tactical mesh SQS queue name.  
        /// Does not get called if --queuename is specified.
        /// </summary>
        public virtual string GetTacticalMeshQueueName()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Get mission specific tactical mesh SQS fail queue name.  
        /// Does not get called if --failqueuename is specified.
        /// Return null or empty to disable tactical mesh fail queue.
        /// </summary>
        public virtual string GetTacticalMeshFailQueueName()
        {
            return null;
        }

        /// <summary>
        /// Pull a tactical mesh tiling message off the queue.
        /// The message type can be a mission specific subclass of QueueMessage.
        /// Does not get called if --usegenericmessagetype is specified. 
        /// </summary>
        public virtual QueueMessage DequeueTacticalMeshMessage(MessageQueue queue)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns null unless msg is a valid and recognized tactical mesh queue message.
        /// Each tactical mesh queue message must contain at most one valid URL.
        /// If a mission produces tactical meshes in more than one format (e.g. IV and OBJ)
        /// then when filter = true return non-null only for one of those, ideally the one written last.
        /// </summary>
        public virtual string GetUrlFromTacticalMeshQueueMessage(QueueMessage msg, bool filter = true,
                                                                 ILogger logger = null)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// This is only used for injecting a message into the queue for testing.
        /// Does not get called if --usegenericmessagetype is specified. 
        /// </summary>
        public virtual QueueMessage ParseTacticalMeshQueueMessage(string json)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Give up processing a tactical mesh this long after first attempt to process it.
        /// </summary>
        public virtual int GetTacticalMeshQueueMessageMaxAgeSec()
        {
            return 60 * 60; //1 hour
        }

        /// <summary>
        /// Get comma separated list of tactical mesh file extensions.
        /// Not cases sensitive, leading dots will be added automatically.
        /// In priority order so if a mesh is available in multiple formats the first one found will be used.
        /// </summary>
        public virtual string GetTacticalMeshExts()
        {
            //TODO for now prefer IV until we implement per-LOD OBJs
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/749
            return "iv,obj";
        }

        /// <summary>
        /// Get comma separated list of tactical image file extensions.
        /// Not cases sensitive, leading dots will be added automatically.
        /// In priority order so if an image is available in multiple formats the first one found will be used.
        /// </summary>
        public virtual string GetTacticalImageExts()
        {
            return "img,png";
        }
    }

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
                 (RoverProductId.Parse(parser.ProductIdString, this).Geometry == RoverProductGeometry.Linearized));
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
    }

    public class MissionM2020 : MissionSpecific
    {
        public const int EECAM_DOWNSAMPLE_FIELD = 46;
        public const int EECAM_RECONSTRUCTION_FIELD = 47;
        public const int DOWNSAMPLE_FIELD = 48;
        public const int COMPRESSION_FIELD = 49;
        public const int COMPRESSION_FIELD_LENGTH = 2;
        public const int VERSION_FIELD = 52;
        public const int VERSION_FIELD_LENGTH = 2;

        public override Mission GetMission()
        {
            return Mission.M2020;
        }

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
                return RoverProductId.Parse(parser.ProductIdString, this).GetSol();
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

        public override RoverObservationComparator GetRoverObservationComparator()
        {
            // 0 if a and b are equivalently good
            // negative if a is "better" than b
            // positive if a is "worse than" b
            int ext(RoverObservation a, RoverObservation b)
            {
                if (IsHazcam(a.Camera) || IsNavcam(a.Camera))
                {
                    //EECAM reconstruction counter 0-9A-Z
                    //prefer higher, precedence over version, downsample, compression
                    char rcA = a.Name[EECAM_RECONSTRUCTION_FIELD];
                    char rcB = b.Name[EECAM_RECONSTRUCTION_FIELD];
                    if (rcA != rcB)
                    {
                        return rcB - rcA;
                    }

                    //EECAM downsampling A,L,M,N, prefer higher, precedence over compression and version
                    char edsA = a.Name[EECAM_DOWNSAMPLE_FIELD];
                    char edsB = b.Name[EECAM_DOWNSAMPLE_FIELD];
                    if (edsA != edsB)
                    {
                        return edsB - edsA;
                    }
                }

                //downsample 0-3, prefer lower, precedence over compression and version
                char dsA = a.Name[DOWNSAMPLE_FIELD];
                char dsB = b.Name[DOWNSAMPLE_FIELD];
                if (dsA != dsB)
                {
                    return dsA - dsB;
                }

                //compresion, prefer higher, precedence over version
                int compA = CompressionPreference(a.Name.Substring(COMPRESSION_FIELD, COMPRESSION_FIELD_LENGTH));
                int compB = CompressionPreference(b.Name.Substring(COMPRESSION_FIELD, COMPRESSION_FIELD_LENGTH));
                if (compA != compB)
                {
                    return compB - compA;
                }

                return 0;
            }

            return new RoverObservationComparator(PreferMSSSToOPGS(),
                                                  PreferLinearToNonlinear(),
                                                  PreferColorToGrayscale(),
                                                  PreferEyeForGeometry(),
                                                  ext);
        }

        public override IEnumerable<RoverProductId> FilterProductIdGroups(IEnumerable<RoverProductId> products)
        {
            IEnumerable<RoverProductId> filterEECAM(IEnumerable<RoverProductId> ids, int idx)
            {
                char max = ids
                    .Where(id => IsHazcam(id.Camera) || IsNavcam(id.Camera))
                    .Select(id => id.FullId[idx])
                    .Max();
                return ids.Where(id => !(IsHazcam(id.Camera) || IsNavcam(id.Camera)) || id.FullId[idx] == max);
            }

            //EECAM reconstruction counter 0-9A-Z (prefer higher, precedence over version, downsample, and compression)
            products = products
                .GroupBy(id => id.GetPartialId(this, includeVariants: false))
                .SelectMany(ids => filterEECAM(ids, EECAM_RECONSTRUCTION_FIELD))
                .ToList();

            //EECAM downsampling A,L,M,N (prefer higher, precedence over version, downsample, and compression)
            products = products
                .GroupBy(id => id.GetPartialId(this, includeVariants: false))
                .SelectMany(ids => filterEECAM(ids, EECAM_DOWNSAMPLE_FIELD))
                .ToList();

            //downsample 0-3 (prefer lower, precedence over version and compression)
            products = products
                .GroupBy(id => id.GetPartialId(this, includeVariants: false))
                .Select(ids => ids.OrderBy(id => id.FullId[DOWNSAMPLE_FIELD]).First())
                .ToList();

            //compresion (prefer higher, precedence over version)
            int cf = COMPRESSION_FIELD, cfl = COMPRESSION_FIELD_LENGTH;
            products = products
                .GroupBy(id => id.GetPartialId(this, includeVariants: false))
                .Select(ids => ids.OrderByDescending(id => CompressionPreference(id.GetPartialId(cf, cfl))).First())
                .ToList();

            return products;
        }

        public int CompressionPreference(string compression)
        {
            compression = compression.ToUpper();
            if (compression.StartsWith("L")) //lossless
            {
                return 300;
            }
            else if (compression.StartsWith("I")) //ICER
            {
                return 200;
            }
            else if (compression == "A0") //JPEG quality 100
            {
                return 100;
            }
            else if (int.TryParse(compression, out int jpegQuality))
            {
                return jpegQuality;
            }
            else
            {
                return -1;
            }
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

            if (id is M2020OPGSProductId)
            {
                M2020OPGSProductId opgsId = (M2020OPGSProductId)id;
                string spec = opgsId.Spec.ToUpper();
                if (spec != "_")
                {
                    reason = "special processing " + spec;
                    return false;
                }

                var camspec = opgsId.Camspec.ToUpper();
                if (IsHazcam(id.Camera) || IsNavcam(id.Camera) || IsMastcam(id.Camera))
                {
                    var stereoPartner = camspec.Substring(0, 1);
                    if (stereoPartner != "_")
                    {
                        reason = "stereo partner " + stereoPartner;
                    }
                }

                //downsample and compression handled in RoverObservationComparator

                if (opgsId.Color == RoverProductColor.Unknown)
                {
                    reason = "color filter " + opgsId.ColorFilter;
                    return false;
                }
            }

            if (id.Producer == RoverProductProducer.MSSS)
            {
                //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/754
                return false;
            }

            return true;
        }

        public override IEnumerable<int[]> GetProductIdVariantSpans(RoverProductId id)
        {
            foreach (var span in base.GetProductIdVariantSpans(id))
            {
                yield return span;
            }
            if (id is M2020OPGSProductId)
            {
                yield return new int[] { EECAM_DOWNSAMPLE_FIELD, 1 };
                yield return new int[] { EECAM_RECONSTRUCTION_FIELD, 1 };
                yield return new int[] { DOWNSAMPLE_FIELD, 1 };
                yield return new int[] { COMPRESSION_FIELD, COMPRESSION_FIELD_LENGTH };
            }
            yield break;
        }

        public override string GetTacticalMeshQueueName()
        {
            throw new NotImplementedException(); //TODO testing with m20-ids-g-sqs-landform-lftest1
        }

        public override string GetTacticalMeshFailQueueName()
        {
            return "m20-ids-g-sqs-landform-tactical-fail";
        }

        public override QueueMessage DequeueTacticalMeshMessage(MessageQueue queue)
        {
            return queue.DequeueOne<SNSMessageWrapper>();
        }

        public override string GetUrlFromTacticalMeshQueueMessage(QueueMessage msg, bool filter = true,
                                                                  ILogger logger = null)
        {
            if (!(msg is SNSMessageWrapper))
            {
                throw new Exception("M2020 tactical mesh queue message does not have SNS wrapper");
            }

            var s3Msg = JsonHelper.FromJson<S3EventMessage>(((SNSMessageWrapper)msg).Message);
            if (s3Msg.Records.Count != 1)
            {
                throw new Exception(string.Format("M2020 tactical mesh queue message has {0} records, expected 1",
                                                  s3Msg.Records.Count));
            }

            var s3EventRecord = s3Msg.Records[0];
            string expected ="ObjectCreated:Put";
            if (s3EventRecord.eventName != expected)
            {
                throw new Exception(string.Format("M2020 tactical mesh queue message event name \"{0}\", " +
                                                  "expected \"{1}\"", s3EventRecord.eventName, expected));
            }

            var s3EventData = s3EventRecord.s3;
            if (s3EventData == null)
            {
                throw new Exception("M2020 tactical mesh queue message has no S3EventData");
            }

            if (s3EventData.bucket == null)
            {
                throw new Exception("M2020 tactical mesh queue message has no S3 bucket");
            }

            if (s3EventData.obj == null)
            {
                throw new Exception("M2020 tactical mesh queue message has no S3 object");
            }

            string url = string.Format("s3://{0}/{1}", s3EventData.bucket.name,
                                       s3EventData.obj.key); //already url encoded

            if (filter && !url.EndsWith(".obj", ignoreCase: true, culture: null))
            {
                if (logger != null)
                {
                    logger.LogInfo("ignoring M2020 tactical mesh queue message (not OBJ) {0}", url);
                }
                return null; //OBJ is written last, only keep OBJ messages
            }

            return url;
        }

        public override QueueMessage ParseTacticalMeshQueueMessage(string json)
        {
            return JsonHelper.FromJson<SNSMessageWrapper>(json, autoTypes: false);
        }
    }

    public class MissionROASTT19 : MissionM2020 
    {
        public override Mission GetMission()
        {
            return Mission.ROASTT19;
        }

        // ROASTT19: bug prevents RMC from being used for frame names. This workaround
        // will break multiple images with different filters resolving to same frame.
        public override string RoverMotionCounter(PDSParser parser)
        {          
            return ((M2020OPGSProductId)RoverProductId.Parse(parser.ProductIdString, this)).GetConcatenatedTimeString();
        }

        // ROASTT19: for some images the INSTRUMENT_ID says LEFT when it should say RIGHT, so use PRODUCT_ID instead
        public override RoverProductCamera GetCamera(PDSParser parser)
        {
            return TranslateCamera(RoverProductId.Parse(parser.ProductIdString, this).Camera);
        }
    }

    public class MissionTT4 : MissionM2020
    {
        public const int SEQUENCE_FIELD = 35; 
        public const int SEQUENCE_FIELD_LENGTH = 9; 

        public override Mission GetMission()
        {
            return Mission.TT4;
        }

        //TT4: sequence number is bumped for different variants
        public override IEnumerable<int[]> GetProductIdVariantSpans(RoverProductId id)
        {
            foreach (var span in base.GetProductIdVariantSpans(id))
            {
                yield return span;
            }
            yield return new int[] { SEQUENCE_FIELD, SEQUENCE_FIELD_LENGTH };
            yield break;
        }
    }
}
