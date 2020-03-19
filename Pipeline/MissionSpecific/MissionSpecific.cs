using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using Microsoft.Xna.Framework;
using System.IO;

namespace OPS.Pipeline
{
    public enum Mission { None, MSL, M2020, ROASTT19, TT4, ScarecrowEECAM, ROASTT20 }

    public abstract class MissionSpecific : ConfigDefaultsProvider
    {
        protected MissionSpecific()
        {
            Config.DefaultsProvider = this;
        }

        public string GetConfigDefaults(string configFilename)
        {
            switch (StringHelper.StripUrlExtension(configFilename))
            {
                case OrbitalConfig.CONFIG_FILENAME: return GetOrbitalConfigDefaults();
                case PlacesConfig.CONFIG_FILENAME: return GetPlacesConfigDefaults();
                default: return null;
            }
        }

        public static MissionSpecific GetInstance(Mission mission)
        {
            switch (mission)
            {
                case Mission.None: return null;
                case Mission.MSL: return new MissionMSL();
                case Mission.M2020: return new MissionM2020();
                case Mission.ROASTT19: return new MissionROASTT19();
                case Mission.TT4: return new MissionTT4();
                case Mission.ScarecrowEECAM: return new MissionScarecrowEECAM();
                case Mission.ROASTT20: return new MissionROASTT20();
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

        public virtual RoverProductCamera GetCamera(PDSParser parser)
        {
            var cam = RoverCamera.FromPDSInstrumentID(parser.InstrumentId);
            if (cam == RoverProductCamera.Unknown)
            {
                cam = ParseProductId(parser.ProductIdString).Camera;
            }
            return TranslateCamera(cam);
        }

        public virtual RoverProductType GetProductType(string productId)
        {
            return ParseProductId(productId).ProductType;
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
                                                  PreferEyeForGeometry(),
                                                  this);
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
        /// whether to allow PDS .LBL files
        /// for some missions these exist and can be useful
        /// for other missions these exist but are something else entirely
        /// https://github.jpl.nasa.gov/OnSight/Landform/issues/829
        /// </summary>
        public virtual bool AllowPDSLabelFiles()
        {
            return false;
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
            return true;
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

        public abstract RoverProductId ParseProductId(string id);

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

            if (!AllowThumbnails() && GetRoverProductSize(parser) != RoverProductSize.Regular)
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

        public virtual RoverProductSize GetRoverProductSize(PDSParser parser)
        {
            return parser.ImageSizeType;
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
        /// Kill tactical mesh tileset processes after this amount of time.
        /// </summary>
        public virtual int GetTacticalMeshQueueMaxHandlerSec()
        {
            return 10 * 60; //10 minutes
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
        /// Not case sensitive, leading dots will be added automatically.
        /// In priority order so if a mesh is available in multiple formats the first one found will be used.
        /// </summary>
        public virtual string GetTacticalMeshExts()
        {
            //prefer IV until we implement per-LOD OBJs
            //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/749
            return "iv,obj";
        }

        /// <summary>
        /// Get frame of tactical meshes as loaded from file.
        /// Should be one of the frame meta-names accepted by FrameCache.GetObservationTransform().
        /// </summary>
        public virtual string GetTacticalMeshFrame()
        {
            return "site";
        }

        /// <summary>
        /// Get comma separated list of tactical image file extensions.
        /// Not case sensitive, no leading dots.
        /// In priority order so if a file is available in multiple formats the first one found will be used.
        /// </summary>
        public virtual string GetTacticalImageExts()
        {
            return "img,png";
        }

        /// <summary>
        /// Get comma separated list of PDS file extensions.
        /// Not case sensitive, no leading dots.
        /// In priority order so if a file is available in multiple formats the first one found will be used.
        /// </summary>
        public virtual string GetPDSExts()
        {
            string exts = "img";
            if (AllowPDSLabelFiles())
            {
                exts += ",lbl";
            }
            exts += ",vic";
            return exts;
        }

        /// <summary>
        /// Get comma separated list of image RDR file extensions to use in scene manfests.
        /// Not case sensitive, no leading dots.
        /// In priority order so if a file is available in multiple formats the first one found will be used.
        /// </summary>
        public virtual string GetSceneManifestImageRDRExts()
        {
            return "img,png,jpg";
        }

        /// <summary>
        /// Get mission specific contextual mesh SQS queue name.  
        /// Does not get called if --queuename is specified.
        /// </summary>
        public virtual string GetContextualMeshQueueName()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Get mission specific contextual mesh SQS fail queue name.  
        /// Does not get called if --failqueuename is specified.
        /// Return null or empty to disable contextual mesh fail queue.
        /// </summary>
        public virtual string GetContextualMeshFailQueueName()
        {
            return null;
        }

        /// <summary>
        /// Pull a contextual mesh tiling message off the queue.
        /// The message type can be a mission specific subclass of QueueMessage.
        /// Does not get called if --usegenericmessagetype is specified. 
        /// </summary>
        public virtual QueueMessage DequeueContextualMeshMessage(MessageQueue queue)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Returns null unless msg is a valid and recognized contextual mesh queue message.
        /// </summary>
        public virtual ContextualMeshParameters GetParametersFromContextualMeshQueueMessage(QueueMessage msg)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// This is only used for injecting a message into the queue for testing.
        /// Does not get called if --usegenericmessagetype is specified. 
        /// </summary>
        public virtual QueueMessage ParseContextualMeshQueueMessage(string json)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Kill contextual mesh tileset processes after this amount of time.
        /// </summary>
        public virtual int GetContextualMeshQueueMaxHandlerSec()
        {
            return 2 * 60 * 60; //2 hours
        }

        /// <summary>
        /// Give up processing a contextual mesh this long after first attempt to process it.
        /// </summary>
        public virtual int GetContextualMeshQueueMessageMaxAgeSec()
        {
            return 6 * 60 * 60; //6 hours
        }

        /// <summary>
        /// Get S3 proxy for use in StorageHelper.ConvertS3URLToHttps()  
        /// </summary>
        public virtual string GetS3Proxy()
        {
            return null;
        }

        public virtual string GetOrbitalConfigDefaults()
        {
            return null;
        }

        public virtual string GetPlacesConfigDefaults()
        {
            return null;
        }
    }
}
