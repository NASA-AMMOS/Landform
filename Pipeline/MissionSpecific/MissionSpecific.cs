using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.Imaging;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Pipeline
{
    public enum Mission { None, MSL, M2020, ROASTT19, TT4, ScarecrowEECAM, ROASTT20, ORT11, TT16, M20SOPS }

    public class MissionConfig : SingletonConfig<MissionConfig>
    {
        public const string CONFIG_FILENAME = "mission"; //config file will be ~/.landform/mission.json
        public override string ConfigFileName()
        {
            return CONFIG_FILENAME;
        }

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_PDS_LABEL_FILES")]
        public bool AllowPDSLabelFiles { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_VIC_FILES")]
        public bool AllowVICFiles { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_IMG_FILES")]
        public bool AllowIMGFiles { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_PREFER_IMG_TO_VIC")]
        public bool PreferIMGToVIC { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_LOCATIONS_DB")]
        public bool AllowLocationsDB { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_PLACES_DB")]
        public bool AllowPlacesDB { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_LEGACY_MANIFEST_DB")]
        public bool AllowLegacyManifestDB { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_THUMBNAILS")]
        public bool AllowThumbnails { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_PARTIAL_PRODUCTS")]
        public bool AllowPartialProducts { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_VIDEO_PRODUCTS")]
        public bool AllowVideoProducts { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_SUN_FINDING")]
        public bool AllowSunFinding { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_IMAGE_PRODUCT_TYPE")]
        public string ImageProductType { get; set; } = "RAS";

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_LINEAR")]
        public bool AllowLinear { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_NONLINEAR")]
        public bool AllowNonLinear { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_MULTI_FRAME")]
        public bool AllowMultiFrame { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_ALLOW_GRAYSCALE_FOR_TEXTURING")]
        public bool AllowGrayscaleForTexturing { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_PREFER_LINEAR_GEOMETRY_PRODUCTS")]
        public bool PreferLinearGeometryProducts { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_PREFER_LINEAR_RASTER_PRODUCTS")]
        public bool PreferLinearRasterProducts { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_PREFER_COLOR_TO_GRAYSCALE")]
        public bool PreferColorToGrayscale { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_ROVER_MASKS")]
        public bool UseRoverMasks { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_ERROR_MAPS")]
        public bool UseErrorMaps { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_USE_HAZCAM_FOR_CONTEXTUAL_TRIGGERING")]
        public bool UseHazcamForContextualTriggering { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_HAZCAM_FOR_ORBITAL_TRIGGERING")]
        public bool UseHazcamForOrbitalTriggering { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_HAZCAM_FOR_ALIGNMENT")]
        public bool UseHazcamForAlignment { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_HAZCAM_FOR_MESHING")]
        public bool UseHazcamForMeshing { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_HAZCAM_FOR_TEXTURING")]
        public bool UseHazcamForTexturing { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_REAR_HAZCAM_FOR_CONTEXTUAL_TRIGGERING")]
        public bool UseRearHazcamForContextualTriggering { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_REAR_HAZCAM_FOR_ORBITAL_TRIGGERING")]
        public bool UseRearHazcamForOrbitalTriggering { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_REAR_HAZCAM_FOR_ALIGNMENT")]
        public bool UseRearHazcamForAlignment { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_REAR_HAZCAM_FOR_MESHING")]
        public bool UseRearHazcamForMeshing { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_REAR_HAZCAM_FOR_TEXTURING")]
        public bool UseRearHazcamForTexturing { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_NAVCAM_FOR_CONTEXTUAL_TRIGGERING")]
        public bool UseNavcamForContextualTriggering { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_NAVCAM_FOR_ORBITAL_TRIGGERING")]
        public bool UseNavcamForOrbitalTriggering { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_NAVCAM_FOR_ALIGNMENT")]
        public bool UseNavcamForAlignment { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_NAVCAM_FOR_MESHING")]
        public bool UseNavcamForMeshing { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_NAVCAM_FOR_TEXTURING")]
        public bool UseNavcamForTexturing { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_MASTCAM_FOR_CONTEXTUAL_TRIGGERING")]
        public bool UseMastcamForContextualTriggering { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_MASTCAM_FOR_ORBITAL_TRIGGERING")]
        public bool UseMastcamForOrbitalTriggering { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_MASTCAM_FOR_ALIGNMENT")]
        public bool UseMastcamForAlignment { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_USE_MASTCAM_FOR_MESHING")]
        public bool UseMastcamForMeshing { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_USE_MASTCAM_FOR_TEXTURING")]
        public bool UseMastcamForTexturing { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_ARMCAM_FOR_CONTEXTUAL_TRIGGERING")]
        public bool UseArmcamForContextualTriggering { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_ARMCAM_FOR_ORBITAL_TRIGGERING")]
        public bool UseArmcamForOrbitalTriggering { get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_USE_ARMCAM_FOR_ALIGNMENT")]
        public bool UseArmcamForAlignment { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_USE_ARMCAM_FOR_MESHING")]
        public bool UseArmcamForMeshing { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_USE_ARMCAM_FOR_TEXTURING")]
        public bool UseArmcamForTexturing { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_USE_UNIFIED_MESHES")]
        public bool UseUnifiedMeshes { get; set; } = false;

        [ConfigEnvironmentVariable("LANDFORM_UNIFIED_MESH_PRODUCT_TYPE")]
        public string UnifiedMeshProductType { get; set; } = "auto";

        //comma separated list of processing types to allow
        //sorted in order of preference (best last)
        [ConfigEnvironmentVariable("LANDFORM_ALLOWED_PROCESSING_TYPES")]
        public string AllowedProcessingTypes { get; set; } = "_"; 

        //comma separated list of producers to allow
        //must match RoverProductProducer enum values
        //sorted in order of preference (best last)
        [ConfigEnvironmentVariable("LANDFORM_ALLOWED_PRODUCERS")]
        public string AllowedProducers { get; set; } = "OPGS"; 

        [ConfigEnvironmentVariable("LANDFORM_CONTEXTUAL_MESH_PREFER_OLDER_PRODUCTS")]
        public bool ContextualMeshPreferOlderProducts{ get; set; } = true;

        [ConfigEnvironmentVariable("LANDFORM_CONTEXTUAL_MESH_MAX_WEDGES")]
        public int ContextualMeshMaxWedges { get; set; } = 2000; 

        [ConfigEnvironmentVariable("LANDFORM_CONTEXTUAL_MESH_MAX_TEXTURES")]
        public int ContextualMeshMaxTextures { get; set; } = 4000; 

        [ConfigEnvironmentVariable("LANDFORM_CONTEXTUAL_MESH_MAX_NAVCAM_WEDGES_PER_SITEDRIVE")]
        public int ContextualMeshMaxNavcamWedgesPerSiteDrive { get; set; } = 200; 

        [ConfigEnvironmentVariable("LANDFORM_CONTEXTUAL_MESH_MAX_NAVCAM_TEXTURES_PER_SITEDRIVE")]
        public int ContextualMeshMaxNavcamTexturesPerSiteDrive { get; set; } = 400; 

        [ConfigEnvironmentVariable("LANDFORM_CONTEXTUAL_MESH_MAX_MASTCAM_WEDGES_PER_SITEDRIVE")]
        public int ContextualMeshMaxMastcamWedgesPerSiteDrive { get; set; } = 200; 

        [ConfigEnvironmentVariable("LANDFORM_CONTEXTUAL_MESH_MAX_MASTCAM_TEXTURES_PER_SITEDRIVE")]
        public int ContextualMeshMaxMastcamTexturesPerSiteDrive { get; set; } = 400; 

        [ConfigEnvironmentVariable("LANDFORM_SERVICE_SSM_KEY_BASE")]
        public string ServiceSSMKeyBase { get; set; } = "REMOVED"; 

        [ConfigEnvironmentVariable("LANDFORM_SERVICE_SSM_ENCRYPTED")]
        public bool ServiceSSMEncrypted { get; set; } = true;
    }

    public abstract class MissionSpecific : ConfigDefaultsProvider
    {
        private readonly string[] DEFAULT_HAZCAM_RDR_SUBDIRS = new string[] { "fcam", "rcam" };
        private readonly string[] DEFAULT_NAVCAM_RDR_SUBDIRS = new string[] { "ncam" };
        private readonly string[] DEFAULT_MASTCAM_RDR_SUBDIRS = new string[] { "mcam" };
        private readonly string[] DEFAULT_ARMCAM_RDR_SUBDIRS = null;
        private readonly string[] DEFAULT_UNIFIED_MESH_RDR_SUBDIRS = new string[] { "mesh" };

        protected readonly string venue;

        protected MissionSpecific(string venue = null)
        {
            this.venue = venue ?? "dev";
            Config.DefaultsProvider = this;
            RoverProduct.SetImageRDRType(GetImageProductType());
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

        public static MissionSpecific GetInstance(Mission mission, string venue = null)
        {
            switch (mission)
            {
                case Mission.None: return null;
                case Mission.MSL: return new MissionMSL(venue);
                case Mission.M2020: return new MissionM2020(venue);
                default: throw new NotImplementedException("unknown mission");
            }
        }

        /// <summary>
        /// If mission string contains a colon then parse it as mission:venue
        /// </summary>
        public static MissionSpecific GetInstance(string mission)
        {
            string venue = null;
            int colon = mission.IndexOf(':');
            if (colon >= 0)
            {
                if (colon < mission.Length - 1) //leave venue=null if colon is last char (rather than set venue="")
                {
                    venue = mission.Substring(colon + 1);
                }
                mission = mission.Substring(0, colon);
            }
            return GetInstance(mission, venue);
        }

        public static MissionSpecific GetInstance(string mission, string venue)
        {
            return GetInstance((Mission)Enum.Parse(typeof(Mission), mission, ignoreCase: true), venue);
        }

        public abstract Mission GetMission();

        public string GetMissionVenue()
        {
            return venue;
        }

        public string GetMissionWithVenue()
        {
            return $"{GetMission().ToString()}:{venue}";
        }

        public virtual string RootFrameName()
        {
            return "root";
        }

        public virtual SiteDrive GetMinSiteDrive()
        {
            return new SiteDrive(1, 0);
        }

        public virtual SiteDrive GetLandingSiteDrive()
        {
            return GetMinSiteDrive();
        }

        public virtual Vector2? GetExpectedLandingLonLat()
        {
            return null;
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
            var pt = parser.DerivedImageType;
            if (pt == RoverProductType.Unknown)
            {
                //MSL MSSS products may be missing DERIVED_IMAGE_PARAMS.DERIVED_IMAGE_TYPE
                //we have also seen M20 OPGS products (in a special processing case) missing this field
                pt = GetProductType(parser.ProductIdString);
            }
            return pt;
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

        public virtual string GetProductIDString(string product)
        {
            return StringHelper.GetLastUrlPathSegment(product, stripExtension: true);
        }

        public virtual RoverObservationComparator.CompareResult
            CompareRoverObservations(RoverObservation a, RoverObservation b, params string[] exceptCrit)
        {
            return new RoverObservationComparator.CompareResult(0, "none");
        }

        /// <summary>
        /// see RoverObservationComparator.FilterProductIDGroups()  
        /// </summary>
        public virtual IEnumerable<RoverProductId>
            FilterProductIDGroups(IEnumerable<RoverProductId> products,
                                  Action<string, List<RoverProductId>, List<RoverProductId>> spew = null)
        {
            return products;
        }

        public virtual RoverStereoEye PreferEyeForGeometry()
        {
            return RoverStereoEye.Left;
        }

        public bool CanMakeSyntheticRoverMasks()
        {
            var masker = GetMasker();
            return masker != null && masker.CanMakeSyntheticRoverMasks(); //generally masker should not be null
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

        public virtual bool IsRearHazcam(RoverProductCamera camera)
        {
                return camera == RoverProductCamera.RearHazcamLeft ||
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
        /// </summary>
        public virtual bool AllowPDSLabelFiles()
        {
            return MissionConfig.Instance.AllowPDSLabelFiles;
        }

        /// <summary>
        /// whether to allow IMG files
        /// typically IMG are transcoded from VIC
        /// waiting for the the transcoding may add latency
        /// but on some missions only the IMG may be persistently stored to S3
        /// </summary>
        public virtual bool AllowIMGFiles()
        {
            return MissionConfig.Instance.AllowIMGFiles;
        }

        /// <summary>
        /// whether to allow VIC files
        /// on some missions only the IMG may be persistently stored to S3
        /// </summary>
        public virtual bool AllowVICFiles()
        {
            return MissionConfig.Instance.AllowVICFiles;
        }

        /// <summary>
        /// whether to prefer IMG to VIC if both are available
        /// </summary>
        public virtual bool PreferIMGToVIC ()
        {
            return MissionConfig.Instance.PreferIMGToVIC;
        }

        /// <summary>
        /// whether to allow priors from MSLLocations
        /// </summary>
        public virtual bool AllowLocationsDB()
        {
            return MissionConfig.Instance.AllowLocationsDB;
        }

        /// <summary>
        /// whether to allow priors from the Places database
        /// </summary>
        public virtual bool AllowPlacesDB()
        {
            return MissionConfig.Instance.AllowPlacesDB;
        }
             
        /// <summary>
        /// whether to allow priors from the OnSight legacy manifest
        /// </summary>
        public virtual bool AllowLegacyManifestDB()
        {
            return MissionConfig.Instance.AllowLegacyManifestDB;
        }

        /// <summary>
        /// whether to ingest thumbnail images
        /// </summary>
        public virtual bool AllowThumbnails()
        {
            return MissionConfig.Instance.AllowThumbnails;
        }

        /// <summary>
        /// whether to ingest partially downloaded images
        /// </summary>
        public virtual bool AllowPartialProducts()
        {
            return MissionConfig.Instance.AllowPartialProducts;
        }

        /// <summary>
        /// whether to ingest video frame images
        /// </summary>
        public virtual bool AllowVideoProducts()
        {
            return MissionConfig.Instance.AllowVideoProducts;
        }

        /// <summary>
        /// whether to ingest sun finding images
        /// </summary>
        public virtual bool AllowSunFinding()
        {
            return MissionConfig.Instance.AllowSunFinding;
        }

        /// <summary>
        /// get image RDR product type, e.g. for contextual mesh texturing
        /// </summary>
        public virtual string GetImageProductType()
        {
            return MissionConfig.Instance.ImageProductType;
        }

        /// <summary>
        /// whether to ingest linearized images
        /// </summary>
        public virtual bool AllowLinear()
        {
            return MissionConfig.Instance.AllowLinear;
        }

        /// <summary>
        /// whether to ingest non-linearized images
        /// ISSUE #353: need to validate that alignment works across cameras with non-linearized images
        /// </summary>
        public virtual bool AllowNonlinear()
        {
            return MissionConfig.Instance.AllowNonLinear;
        }

        /// <summary>
        /// whether to allow grayscale images for texturing
        /// </summary>
        public virtual bool AllowGrayscaleForTexturing()
        {
            return MissionConfig.Instance.AllowGrayscaleForTexturing;
        }

        /// <summary>
        /// whether to allow multi-frame products such as unified meshes
        /// </summary>
        public virtual bool AllowMultiFrame()
        {
            return MissionConfig.Instance.AllowMultiFrame;
        }

        /// <summary>
        /// whether to prefer non-linearized geometry products when both are available
        /// </summary>
        public virtual bool PreferLinearGeometryProducts()
        {
            return MissionConfig.Instance.PreferLinearGeometryProducts;
        }

        /// <summary>
        /// whether to prefer non-linearized raster products when both are available
        /// </summary>
        public virtual bool PreferLinearRasterProducts()
        {
            return MissionConfig.Instance.PreferLinearRasterProducts;
        }

        /// <summary>
        /// whether to prefer color images to bw when both are available
        /// </summary>
        public virtual bool PreferColorToGrayscale()
        {
            return MissionConfig.Instance.PreferColorToGrayscale;
        }

        public virtual bool UseRoverMasks()
        {
            return MissionConfig.Instance.UseRoverMasks;
        }

        public virtual bool UseErrorMaps()
        {
            return MissionConfig.Instance.UseErrorMaps;
        }

        public virtual bool UseHazcamForContextualTriggering()
        {
            return MissionConfig.Instance.UseHazcamForContextualTriggering;
        }

        public virtual bool UseHazcamForOrbitalTriggering()
        {
            return MissionConfig.Instance.UseHazcamForOrbitalTriggering;
        }

        public virtual bool UseHazcamForAlignment()
        {
            return MissionConfig.Instance.UseHazcamForAlignment;
        }

        public virtual bool UseHazcamForMeshing()
        {
            return MissionConfig.Instance.UseHazcamForMeshing;
        }

        public virtual bool UseHazcamForTexturing()
        {
            return MissionConfig.Instance.UseHazcamForTexturing;
        }

        public virtual bool UseRearHazcamForContextualTriggering()
        {
            return MissionConfig.Instance.UseRearHazcamForContextualTriggering;
        }

        public virtual bool UseRearHazcamForOrbitalTriggering()
        {
            return MissionConfig.Instance.UseRearHazcamForOrbitalTriggering;
        }

        public virtual bool UseRearHazcamForAlignment()
        {
            return MissionConfig.Instance.UseRearHazcamForAlignment;
        }

        public virtual bool UseRearHazcamForMeshing()
        {
            return MissionConfig.Instance.UseRearHazcamForMeshing;
        }

        public virtual bool UseRearHazcamForTexturing()
        {
            return MissionConfig.Instance.UseRearHazcamForTexturing;
        }

        public virtual bool UseNavcamForContextualTriggering()
        {
            return MissionConfig.Instance.UseNavcamForContextualTriggering;
        }

        public virtual bool UseNavcamForOrbitalTriggering()
        {
            return MissionConfig.Instance.UseNavcamForOrbitalTriggering;
        }

        public virtual bool UseNavcamForAlignment()
        {
            return MissionConfig.Instance.UseNavcamForAlignment;
        }

        public virtual bool UseNavcamForMeshing()
        {
            return MissionConfig.Instance.UseNavcamForMeshing;
        }

        public virtual bool UseNavcamForTexturing()
        {
            return MissionConfig.Instance.UseNavcamForTexturing;
        }

        public virtual bool UseMastcamForContextualTriggering()
        {
            return MissionConfig.Instance.UseMastcamForContextualTriggering;
        }

        public virtual bool UseMastcamForOrbitalTriggering()
        {
            return MissionConfig.Instance.UseMastcamForOrbitalTriggering;
        }

        public virtual bool UseMastcamForAlignment()
        {
            return MissionConfig.Instance.UseMastcamForAlignment;
        }

        public virtual bool UseMastcamForMeshing()
        {
            return MissionConfig.Instance.UseMastcamForMeshing;
        }

        public virtual bool UseMastcamForTexturing()
        {
            return MissionConfig.Instance.UseMastcamForTexturing;
        }

        public virtual bool UseArmcamForContextualTriggering()
        {
            return MissionConfig.Instance.UseArmcamForContextualTriggering;
        }

        public virtual bool UseArmcamForOrbitalTriggering()
        {
            return MissionConfig.Instance.UseArmcamForOrbitalTriggering;
        }

        public virtual bool UseArmcamForAlignment()
        {
            return MissionConfig.Instance.UseArmcamForAlignment;
        }

        public virtual bool UseArmcamForMeshing()
        {
            return MissionConfig.Instance.UseArmcamForMeshing;
        }

        public virtual bool UseArmcamForTexturing()
        {
            return MissionConfig.Instance.UseArmcamForTexturing;
        }

        public virtual string[] GetHazcamRDRSubdirs()
        {
            return DEFAULT_HAZCAM_RDR_SUBDIRS;
        }

        public virtual string[] GetNavcamRDRSubdirs()
        {
            return DEFAULT_NAVCAM_RDR_SUBDIRS;
        }

        public virtual string[] GetMastcamRDRSubdirs()
        {
            return DEFAULT_MASTCAM_RDR_SUBDIRS;
        }

        public virtual string[] GetArmcamRDRSubdirs()
        {
            return DEFAULT_ARMCAM_RDR_SUBDIRS;
        }

        public virtual string[] GetUnifiedMeshRDRSubdirs()
        {
            return DEFAULT_UNIFIED_MESH_RDR_SUBDIRS;
        }

        public virtual bool UseUnifiedMeshes()
        {
            return MissionConfig.Instance.UseUnifiedMeshes;
        }

        public virtual string GetUnifiedMeshProductType()
        {
            return MissionConfig.Instance.UnifiedMeshProductType.Replace("auto", GetImageProductType());
        }

        public bool UseForContextualTriggering(PDSParser parser)
        {
            return UseForContextualTriggering(GetCamera(parser));
        }

        public bool UseForContextualTriggering(RoverProductId id)
        {
            return UseForContextualTriggering(id.Camera);
        }

        public virtual bool UseForContextualTriggering(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && UseHazcamForContextualTriggering() &&
                    (!IsRearHazcam(cam) || UseRearHazcamForContextualTriggering())) ||
                (IsNavcam(cam) && UseNavcamForContextualTriggering()) ||
                (IsMastcam(cam) && UseMastcamForContextualTriggering()) ||
                (IsArmcam(cam) && UseArmcamForContextualTriggering());
        }

        public bool UseForOrbitalTriggering(PDSParser parser)
        {
            return UseForOrbitalTriggering(GetCamera(parser));
        }

        public bool UseForOrbitalTriggering(RoverProductId id)
        {
            return UseForOrbitalTriggering(id.Camera);
        }

        public virtual bool UseForOrbitalTriggering(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && UseHazcamForOrbitalTriggering() &&
                    (!IsRearHazcam(cam) || UseRearHazcamForOrbitalTriggering())) ||
                (IsNavcam(cam) && UseNavcamForOrbitalTriggering()) ||
                (IsMastcam(cam) && UseMastcamForOrbitalTriggering()) ||
                (IsArmcam(cam) && UseArmcamForOrbitalTriggering());
        }

        public bool UseForAlignment(PDSParser parser)
        {
            return UseForAlignment(GetCamera(parser));
        }

        public bool UseForAlignment(RoverProductId id)
        {
            return UseForAlignment(id.Camera);
        }

        public virtual bool UseForAlignment(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && UseHazcamForAlignment() && (!IsRearHazcam(cam) || UseRearHazcamForAlignment())) ||
                (IsNavcam(cam) && UseNavcamForAlignment()) ||
                (IsMastcam(cam) && UseMastcamForAlignment()) ||
                (IsArmcam(cam) && UseArmcamForAlignment());
        }

        public bool UseForMeshing(PDSParser parser)
        {
            return UseForMeshing(GetCamera(parser)) && RoverProduct.IsGeometry(GetProductType(parser));
        }

        public bool UseForMeshing(RoverProductId id)
        {
            return UseForMeshing(id.Camera) && RoverProduct.IsGeometry(id.ProductType);
        }

        public virtual bool UseForMeshing(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && UseHazcamForMeshing() && (!IsRearHazcam(cam) || UseRearHazcamForMeshing())) ||
                (IsNavcam(cam) && UseNavcamForMeshing()) ||
                (IsMastcam(cam) && UseMastcamForMeshing()) ||
                (IsArmcam(cam) && UseArmcamForMeshing());
        }

        protected bool UseForTexturing(RoverProductCamera cam, RoverProductType pt, bool color)
        {
            return UseForTexturing(cam) && RoverProduct.IsRaster(pt) &&
                (RoverProduct.IsMask(pt) || color || AllowGrayscaleForTexturing());
        }

        public bool UseForTexturing(PDSParser parser)
        {
            return UseForTexturing(GetCamera(parser), GetProductType(parser), parser.metadata.Bands > 1);
        }

        public bool UseForTexturing(RoverProductId id)
        {
            return UseForTexturing(id.Camera, id.ProductType, id.Color == RoverProductColor.FullColor);
        }

        public virtual bool UseForTexturing(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && UseHazcamForTexturing() && (!IsRearHazcam(cam) || UseRearHazcamForTexturing())) ||
                (IsNavcam(cam) && UseNavcamForTexturing()) ||
                (IsMastcam(cam) && UseMastcamForTexturing()) ||
                (IsArmcam(cam) && UseArmcamForTexturing());
        }

        public virtual bool UseCamera(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && (UseHazcamForAlignment() || UseHazcamForMeshing() || UseHazcamForTexturing()) &&
                    (!IsRearHazcam(cam) ||
                     UseRearHazcamForAlignment() || UseRearHazcamForMeshing() || UseRearHazcamForTexturing())) ||
                (IsNavcam(cam) && (UseNavcamForAlignment() || UseNavcamForMeshing() || UseNavcamForTexturing())) ||
                (IsMastcam(cam) && (UseMastcamForAlignment() || UseMastcamForMeshing() || UseMastcamForTexturing())) ||
                (IsArmcam(cam) && (UseArmcamForAlignment() || UseArmcamForMeshing() || UseArmcamForTexturing()));
        }

        public virtual bool UseRasterProducts(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && (UseHazcamForAlignment() || UseHazcamForTexturing()) &&
                    (!IsRearHazcam(cam) || UseRearHazcamForAlignment() || UseRearHazcamForTexturing())) ||
                (IsNavcam(cam) && (UseNavcamForAlignment() || UseNavcamForTexturing())) ||
                (IsMastcam(cam) && (UseMastcamForAlignment() || UseMastcamForTexturing())) ||
                (IsArmcam(cam) && (UseArmcamForAlignment() || UseArmcamForTexturing()));
        }

        public virtual bool UseGeometryProducts(RoverProductCamera cam)
        {
            return (IsHazcam(cam) && (UseHazcamForAlignment() || UseHazcamForMeshing()) &&
                    (!IsRearHazcam(cam) || UseRearHazcamForAlignment() || UseRearHazcamForMeshing())) ||
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

            if (!id.IsSingleFrame() && !AllowMultiFrame())
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

            if (!GetAllowedProducers().Contains(id.Producer))
            {
                reason = string.Format("producer {0} not allowed", id.Producer);
                return false;
            }

            if (id is OPGSProductId)
            {
                OPGSProductId opgsId = (OPGSProductId)id;

                if (GetAllowedProcessingTypes().FindIndex(t => t == opgsId.Spec) < 0)
                {
                    reason = "special processing " + opgsId.Spec;
                    return false;
                }

                if (!AllowThumbnails() && opgsId.Size != RoverProductSize.Regular)
                {
                    reason = "thumbnails not allowed";
                    return false;
                }
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

        //null if not supported by mission
        //otherwise first capturing group corresponds to second argument of videoEDRExists callback for IsVideoProduct()
        public virtual Regex GetVideoURLRegex()
        {
            return null;
        }

        //false if not supported by mission
        //otherwise check if product ID and/or URL is a video product
        //videoEDRExists is an optional callback (s3Folder, fileBasename) => bool that is needed for some missions
        //e.g. for M2020 it's not possible to know if a ZCAM image is a video frame just from its ID or URL
        //but we can instead munge the URL into a corresponding ECV EDR url and check for that
        public virtual bool IsVideoProduct(RoverProductId id, string url, Func<string, string, bool> videoEDRExists)
        {
            return false;
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

            if (!AllowVideoProducts() && parser.IsVideoFrame)
            {
                reason = "video frames not allowed";
                return false;
            }

            if (!AllowPartialProducts() && parser.IsPartial)
            {
                reason = "partial products not allowed";
                return false;
            }

            if (parser.metadata.Bands != 3 && parser.metadata.Bands != 1)
            {
                reason = "only 1 or 3 band images allowed";
                return false;
            }

            if (parser.ProducingInstitution == RoverProductProducer.Unknown)
            {
                reason = "unknown producer";
                return false;
            }

            if (!GetAllowedProducers().Contains(parser.ProducingInstitution))
            {
                reason = string.Format("producer {0} not allowed", parser.ProducingInstitution);
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
        /// Refresh AWS and any other credentials that may be needed for this mission.
        /// Uses the default profile and region for the mission by default.
        /// Returns new profile name or null if failed or unchanged.
        /// </summary>
        public virtual string RefreshCredentials(string awsProfile = null, string awsRegion = null, bool quiet = true,
                                                 bool dryRun = false, bool throwOnFail = false, ILogger logger = null)
        {
            return null;
        }

        /// <summary>
        /// Get the desired maximum time between credential refresh.
        /// If non-positive then credential refresh is not required.
        /// </summary>
        public virtual int GetCredentialDurationSec()
        {
            return 0;
        }

        /// <summary>
        /// Get comma separated list of tactical (i.e. wedge) mesh file extensions.
        /// Not case sensitive, no leading dots.
        /// In priority order so if a file is available in multiple formats the first one found will be used.
        /// </summary>
        public virtual string GetTacticalMeshExts()
        {
            return "iv,obj";
        }

        /// <summary>
        /// Get a regex for testing whether a URL should trigger tactical mesh tileset generation.
        /// The first capturing group, which always exists, is the product id.
        /// The second capturing group, if any, is the number of the last LOD.
        /// </summary>
        public virtual string GetTacticalMeshTriggerRegex()
        {
            return "auto_iv"; //see ProcessTactical.ParseMeshRegex()
        }

        /// <summary>
        /// Get the geometry type to process for tactical meshes.
        /// </summary>
        public virtual RoverProductGeometry GetTacticalMeshGeometry()
        {
            return RoverProductGeometry.Any;
        }

        /// <summary>
        /// Get frame of tactical meshes as loaded from file.
        /// Should be one of the frame meta-names accepted by FrameCache.GetObservationTransform().
        /// </summary>
        public virtual string GetTacticalMeshFrame(RoverProductId id = null)
        {
            return "site";
        }

        public string GetTacticalMeshFrame(string idStr)
        {
            return GetTacticalMeshFrame(ParseProductId(idStr));
        }

        /// <summary>
        /// Get the subdirs of the RDR directory to search for contextual mesh RDRs.
        /// Order matters: fetch will process the subdirs in order.
        /// Unified meshes should be fetched before RDRs they pertain to.
        /// Other instruments should be in priority order as fetch may trim downloads to fit max download limits.
        /// </summary>
        public virtual string[] GetContextualMeshRDRSubdirs()
        {
            var dirs = new List<string>();
            if (UseUnifiedMeshes() && GetUnifiedMeshRDRSubdirs() != null)
            {
                dirs.AddRange(GetUnifiedMeshRDRSubdirs());
            }
            if ((UseNavcamForAlignment() || UseNavcamForMeshing() || UseNavcamForTexturing()) &&
                GetNavcamRDRSubdirs() != null)
            {
                dirs.AddRange(GetNavcamRDRSubdirs());
            }
            if ((UseHazcamForAlignment() || UseHazcamForMeshing() || UseHazcamForTexturing()) &&
                GetHazcamRDRSubdirs() != null)
            {
                dirs.AddRange(GetHazcamRDRSubdirs());
            }
            if ((UseMastcamForAlignment() || UseMastcamForMeshing() || UseMastcamForTexturing()) &&
                GetMastcamRDRSubdirs() != null)
            {
                dirs.AddRange(GetMastcamRDRSubdirs());
            }
            if ((UseArmcamForAlignment() || UseArmcamForMeshing() || UseArmcamForTexturing()) &&
                GetArmcamRDRSubdirs() != null)
            {
                dirs.AddRange(GetArmcamRDRSubdirs());
            }
            return dirs.ToArray();
        }

        public virtual bool GetContextualMeshPreferOlderProducts()
        {
            return MissionConfig.Instance.ContextualMeshPreferOlderProducts;
        }

        public virtual int GetContextualMeshMaxWedges()
        {
            return MissionConfig.Instance.ContextualMeshMaxWedges;
        }

        public virtual int GetContextualMeshMaxTextures()
        {
            return MissionConfig.Instance.ContextualMeshMaxTextures;
        }

        public virtual int GetContextualMeshMaxNavcamWedgesPerSiteDrive()
        {
            return MissionConfig.Instance.ContextualMeshMaxNavcamWedgesPerSiteDrive;
        }

        public virtual int GetContextualMeshMaxNavcamTexturesPerSiteDrive()
        {
            return MissionConfig.Instance.ContextualMeshMaxNavcamTexturesPerSiteDrive;
        }

        public virtual int GetContextualMeshMaxMastcamWedgesPerSiteDrive()
        {
            return MissionConfig.Instance.ContextualMeshMaxMastcamWedgesPerSiteDrive;
        }

        public virtual int GetContextualMeshMaxMastcamTexturesPerSiteDrive()
        {
            return MissionConfig.Instance.ContextualMeshMaxMastcamTexturesPerSiteDrive;
        }

        public virtual string FilterContextualMeshWedge(RoverProductId id, string url)
        {
            string reason = FilterContextualMeshProduct(id, url);
            if (reason != null)
            {
                return reason;
            }
            if (!UseForMeshing(id) && !UseForAlignment(id))
            {
                return "product type not used for meshing or alignment";
            }
            var preferredEye = PreferEyeForGeometry();
            if (!RoverStereoPair.IsStereoEye(id.Camera, preferredEye))
            {
                return string.Format("stereo eye {0} != {1}", id.Camera, preferredEye);
            }
            var preferredGeometry = PreferLinearGeometryProducts() ?
                RoverProductGeometry.Linearized : RoverProductGeometry.Raw;
            if (id.Geometry != preferredGeometry)
            {
                return string.Format("linearity {0} != {1}", id.Geometry, preferredGeometry);
            }
            return null;
        }

        public virtual string FilterContextualMeshTexture(RoverProductId id, string url)
        {
            string reason = FilterContextualMeshProduct(id, url);
            if (reason != null)
            {
                return reason;
            }
            if (!UseForTexturing(id))
            {
                return "product type not used for texturing";
            }
            var preferredGeometry = PreferLinearRasterProducts() ?
                RoverProductGeometry.Linearized : RoverProductGeometry.Raw;
            if (id.Geometry != preferredGeometry)
            {
                return string.Format("linearity {0} != {1}", id.Geometry, preferredGeometry);
            }
            return null;
        }

        protected virtual string FilterContextualMeshProduct(RoverProductId id, string url)
        {
            if (id is OPGSProductId)
            {
                if ((id as OPGSProductId).Size == RoverProductSize.Thumbnail)
                {
                    return "thumbnail product";
                }
                if ((id as OPGSProductId).SiteDrive.Drive % 2 == 1)
                {
                    return "odd drive number (in-motion)";
                }
            }
            if (!CheckProductId(id, out string reason))
            {
                return reason;
            }
            return null;
        }

        /// <summary>
        /// Get comma separated list of PDS file extensions.
        /// Not case sensitive, no leading dots.
        /// In priority order so if a file is available in multiple formats the first one found will be used.
        /// </summary>
        public virtual string GetPDSExts(bool disablePDSLabelFiles = false, bool prioritizePDSLabelFiles = false)
        {
            string imgExts = AllowIMGFiles() ? "img" : "";
            if (!disablePDSLabelFiles && AllowPDSLabelFiles())
            {
                if (prioritizePDSLabelFiles)
                {
                    imgExts = "lbl" + (!string.IsNullOrEmpty(imgExts) ? "," : "") + imgExts;
                }
                else
                {
                    imgExts += (!string.IsNullOrEmpty(imgExts) ? "," : "") + "lbl";
                }
            }

            string vicExts = AllowVICFiles() ? "vic" : "";

            string exts = PreferIMGToVIC() ? imgExts : vicExts;
            exts += (!string.IsNullOrEmpty(exts) ? "," : "") + (PreferIMGToVIC() ? vicExts : imgExts);

            return exts;
        }

        /// <summary>
        /// Get comma separated list of image RDR file extensions to use in scene manfests.
        /// Not case sensitive, no leading dots.
        /// In priority order so if a file is available in multiple formats the first one found will be used.
        /// </summary>
        public virtual string GetSceneManifestImageRDRExts()
        {
            string exts = GetPDSExts(disablePDSLabelFiles: true);
            return exts + (!string.IsNullOrEmpty(exts) ? "," : "") + "png,jpg";
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

        /// <summary>
        /// Mission surface frames (e.g. SITE, LOCAL_LEVEL) are typically +X north, +Y east, +Z nadir.
        /// </summary>
        public virtual void GetLocalLevelBasis(out Vector3 north, out Vector3 east, out Vector3 nadir)
        {
            north = new Vector3(1, 0, 0);
            east = new Vector3(0, 1, 0);
            nadir = new Vector3(0, 0, 1);
        }

        /// <summary>
        /// Mission surface frames (e.g. SITE, LOCAL_LEVEL) are typically +X north, +Y east, +Z nadir.
        ///
        /// GIS images in Equirectangular projection have
        /// * latitude decreasing with row
        /// * longitude increasing with col
        /// * elevation positive towards the zenith.
        ///
        /// Returns orthonormal basis for a GIS image frame expressed as directions in local level frame which aligns
        /// * image latitude with LOCAL_LEVEL north (+X)
        /// * image longitude with LOCAL_LEVEL east (+Y)
        /// * elevation with LOCAL_LEVEL zenith (-Z).
        /// </summary>
        public virtual void GetOrthonormalGISBasisInLocalLevelFrame(out Vector3 gisElevationInLocalLevel,
                                                                    out Vector3 gisImageRightInLocalLevel,
                                                                    out Vector3 gisImageDownInLocalLevel)
        {
            GetLocalLevelBasis(out Vector3 north, out Vector3 east, out Vector3 nadir);
            gisElevationInLocalLevel = -nadir;
            gisImageRightInLocalLevel = east;
            gisImageDownInLocalLevel = -north;
        }

        public virtual double GetMastHeightMeters()
        {
            return 2; //MSL and M2020 rover mast height is about 2m above bottom of wheels on flat surface
        }

        public virtual string SolToString(int sol)
        {
            return string.Format("{0:D4}", sol);
        }

        public virtual string SiteToString(int site)
        {
            return OPGSProductId.SiteToString(site);
        }

        public virtual string DriveToString(int drive)
        {
            return OPGSProductId.DriveToString(drive);
        }

        private List<string> allowedProcessingTypes = new List<string>();
        public List<string> GetAllowedProcessingTypes(String types)
        {
            lock (allowedProcessingTypes)
            {
                if (allowedProcessingTypes.Count == 0)
                {
                    allowedProcessingTypes.AddRange(StringHelper.ParseList(types));
                }
                return allowedProcessingTypes;
            }
        }

        public virtual List<string> GetAllowedProcessingTypes()
        {
            return GetAllowedProcessingTypes(MissionConfig.Instance.AllowedProcessingTypes);
        }

        private List<RoverProductProducer> allowedProducers = new List<RoverProductProducer>();
        public List<RoverProductProducer> GetAllowedProducers(String producers)
        {
            lock (allowedProducers)
            {
                if (allowedProducers.Count == 0)
                {
                    allowedProducers
                        .AddRange(StringHelper.ParseList(producers)
                                  .Select(p => (RoverProductProducer)Enum.Parse(typeof(RoverProductProducer), p,
                                                                                ignoreCase: true)));
                }
                return allowedProducers;
            }
        }

        public virtual List<RoverProductProducer> GetAllowedProducers()
        {
            return GetAllowedProducers(MissionConfig.Instance.AllowedProducers);
        }

        public virtual string GetSSMWatchdogProcess()
        {
            return null;
        }

        public virtual string GetSSMWatchdogCommand()
        {
            return null;
        }

        public virtual string GetCloudWatchWatchdogProcess()
        {
            return null;
        }

        public virtual string GetCloudWatchWatchdogCommand()
        {
            return null;
        }

        public virtual string GetServiceSSMKeyBase()
        {
            return MissionConfig.Instance.ServiceSSMKeyBase.Replace("{venue}", venue);
        }

        public virtual bool GetServiceSSMEncrypted()
        {
            return MissionConfig.Instance.ServiceSSMEncrypted;
        }

        public virtual List<string> GetFDRSearchDirs()
        {
            return null;
        }
    }
}
