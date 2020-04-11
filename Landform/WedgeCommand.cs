using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    public class WedgeCommandOptions : LandformCommandOptions
    {
        [Option(HelpText = "Wedge mesh decimation blocksize, 0 to disable, -1 for auto", Default = -1)]
        public virtual int DecimateWedgeMeshes { get; set; }

        [Option(HelpText = "Wedge image decimation blocksize, 0 to disable, -1 for auto", Default = -1)]
        public virtual int DecimateWedgeImages { get; set; }

        [Option(HelpText = "Wedge mesh auto decimation target resolution", Default = 1024)]
        public virtual int TargetWedgeMeshResolution { get; set; }

        [Option(HelpText = "Wedge image auto decimation target resolution", Default = 1024)]
        public virtual int TargetWedgeImageResolution { get; set; }

        [Option(HelpText = "Mesh decimation method (EdgeCollapse, ResampleFSSR, ResamplePoisson, MeshLab, MeshLabResample)", Default = MeshDecimationMethod.EdgeCollapse)]
        public virtual MeshDecimationMethod MeshDecimator { get; set; }

        [Option(HelpText = "Only use specific surface observations, comma separated (e.g. MLF_452276219RASLS0311330MCAM02600M1)", Default = null)]
        public virtual string OnlyForObservations { get; set; }

        [Option(HelpText = "Only use specific surface frames, comma separated (e.g. MastcamLeft_00031013300028400454000060009001618010680001200000)", Default = null)]
        public virtual string OnlyForFrames { get; set; }

        [Option(HelpText = "Only use specific surface cameras, comma separated (e.g. Hazcam, Mastcam, Navcam, FrontHazcam, FrontHazcamLeft, etc)", Default = null)]
        public virtual string OnlyForCameras { get; set; }

        [Option(HelpText = "Only use observations from specific site drives SSSSSDDDDD, comma separated, wildcard xxxxx", Default = null)]
        public virtual string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted, Manual, Landform, LandformBEV, LandformBEVRoot, LandformBEVCalf, Agisoft)", Default = null)]
        public virtual string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior, LegacyManifest, PlacesDB, LocationsDB, PlacesDBSitePDSLocal, PDSChained, PDS)", Default = null)]
        public virtual string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public virtual bool UsePriors { get; set; }

        [Option(HelpText = "Use adjusted transforms only", Default = false)]
        public virtual bool OnlyAligned { get; set; }

        [Option(HelpText = "Disable orbital", Default = false, Required = false)]
        public virtual bool NoOrbital { get; set; }

        [Option(HelpText = "Disable suface observations, only orbital", Default = false)]
        public virtual bool NoSurface { get; set; }

        [Option(Required = false, Default = null, HelpText = "Override default orbital DEM file path")]
        public string OrbitalDEM { get; set; }

        [Option(Required = false, Default = null, HelpText = "Override default orbital image file path")]
        public string OrbitalImage { get; set; }

        [Option(Required = false, Default = DEM.DEF_MIN_FILTER, HelpText = "DEM values less than this will be ignored")]
        public double DEMMinFilter { get; set; }

        [Option(Required = false, Default = DEM.DEF_MAX_FILTER, HelpText = "DEM larger than this will be ignored")]
        public double DEMMaxFilter { get; set; }
    }

    public class WedgeCommand : LandformCommand
    {
        protected WedgeCommandOptions wcopts;

        protected SiteDrive[] siteDrives;
        protected TransformSource[] priorSources;
        protected TransformSource[] adjustedSources;

        protected FrameCache frameCache;
        protected ObservationCache observationCache;

        protected SiteDrive? rootSiteDrive;

        protected DEM orbitalDEM;
        protected double orbitalAvgMetersPerPixel;
        protected Matrix orbitalToRoot;

        protected bool IsRootSiteDrive(SiteDrive sd)
        {
            return rootSiteDrive.HasValue && sd == rootSiteDrive.Value;
        }

        protected WedgeCommand(WedgeCommandOptions wcopts) : base(wcopts)
        {
            this.wcopts = wcopts;
        }

        protected virtual bool ParseArgumentsAndLoadCaches(string outDir)
        {
            if (wcopts.NoOrbital && wcopts.NoSurface)
            {
                throw new Exception("cannot combine --noorbital with --nosurface");
            }

            if (wcopts.UsePriors && wcopts.OnlyAligned)
            {
                throw new Exception("cannot specify both --usepriors and --onlyaligned");
            }

            siteDrives = SiteDrive.ParseList(wcopts.OnlyForSiteDrives);
            priorSources = FrameTransform.ParseSources(wcopts.PriorTransformSources);
            adjustedSources = FrameTransform.ParseSources(wcopts.AdjustedTransformSources);

            if (outDir != null)
            {
                outDir = DecorateOutDir(outDir);
            }

            if (!base.ParseArguments(outDir))
            {
                return false; //help
            }

            if (project != null)
            {
                LoadFrameCache();
                LoadObservationCache();
            }

            return true;
        }

        protected virtual string DecorateOutDir(string outDir)
        {
            return FrameTransform.AppendSourcesPath(outDir, adjustedSources, priorSources, wcopts.UsePriors);
        }

        protected override bool ParseArguments(string outDir)
        {
            throw new NotImplementedException();
        }

        protected virtual void LoadFrameCache()
        {
            frameCache = new FrameCache(pipeline, project.Name);
            int num = frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, wcopts.UsePriors);
            pipeline.LogInfo("loaded {0} frames in project {1}", num, project.Name);
            rootSiteDrive = frameCache.CheckPriors(mission.GetLandingSiteDrive());
            if (!rootSiteDrive.HasValue)
            {
                pipeline.LogError("incomplete priors: not all sitedrives are connected");
            }
            else
            {
                pipeline.LogInfo("effective root frame for project: {0}", rootSiteDrive.Value);
            }
        }

        protected virtual bool ObservationFilter(RoverObservation obs)
        {
            return true;
        }

        protected virtual string DescribeObservationFilter()
        {
            return "";
        }

        protected virtual void LoadObservationCache()
        {
            var observations = StringHelper.ParseList(wcopts.OnlyForObservations);
            var frames = StringHelper.ParseList(wcopts.OnlyForFrames);
            var cams = RoverCamera.ParseList(wcopts.OnlyForCameras);

            observationCache = new ObservationCache(pipeline, project.Name);

            int num = observationCache.
                Preload(obs =>
                        (!wcopts.NoOrbital && obs.IsOrbital) ||
                        (!wcopts.NoSurface && (obs is RoverObservation) && ObservationFilter((RoverObservation)obs) &&
                         (siteDrives.Length == 0 || siteDrives.Any(sd => sd == ((RoverObservation)obs).SiteDrive)) &&
                         (observations.Length == 0 || observations.Any(name => name == obs.Name)) &&
                         (frames.Length == 0 || frames.Any(name => name == obs.FrameName)) &&
                         (cams.Length == 0 || cams.Any(c => RoverCamera.IsCamera(c, ((RoverObservation)obs).Camera)))));

            //TODO
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/1037 
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/976
            //if (num == 0)
            //{
            //    throw new Exception("no surface or orbital data available");
            //}

            int numOrbital = wcopts.NoOrbital ? 0 : observationCache.GetAllObservations().Count(obs => obs.IsOrbital);
            int numSurface = num - numOrbital;

            //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/1037 
            //wcopts.NoOrbital |= numOrbital == 0;
            wcopts.NoSurface |= numSurface == 0;

            pipeline.LogInfo("loaded {0}{1} surface observations{2} in project {3}{4}{5}",
                             numSurface, DescribeObservationFilter(),
                             numOrbital > 0 ? $" and {numOrbital} orbital observations" : "",
                             project.Name,
                             siteDrives.Length > 0 ? (" for sitedrives " + string.Join(", ", siteDrives)): "",
                             cams.Length > 0 ? (" for cameras " + string.Join(", ", cams)) : "");
        }

        protected void LoadOrbitalDEM(SiteDrive originSiteDrive)
        {
            try
            {
                string demFile = wcopts.OrbitalDEM;
                orbitalDEM = LoadOrbitalDEM(mission, originSiteDrive, ref demFile,
                                            minFilter: wcopts.DEMMinFilter, maxFilter: wcopts.DEMMaxFilter,
                                            logger: pipeline);

                orbitalAvgMetersPerPixel = orbitalDEM.AvgMetersPerPixel;

                orbitalToRoot = frameCache.GetBestPrior(originSiteDrive.ToString()).Transform.Mean;

                var originPixel = orbitalDEM.OriginPixel;
                var cmod = orbitalDEM.CameraModel;
                pipeline.LogInfo("loaded {0}x{1} orbital DEM {2} at sitedrive {3} (pixel {4:F3}, {5:F3}) using {6}" +
                                 ", ({7}, {8}) meters per pixel",
                                 orbitalDEM.Width, orbitalDEM.Height, demFile,
                                 originSiteDrive, originPixel.X, originPixel.Y, cmod.GetType().Name,
                                 orbitalDEM.MetersPerPixel.X, orbitalDEM.MetersPerPixel.Y);
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("failed to load orbital DEM or PlacesDB, running without orbital: {0}", ex.Message);
                wcopts.NoOrbital = true;
            }
        }

        /// <summary>
        /// Load an orbital DEM image.
        ///
        /// demFile defaults to OrbitalConfig.OrbitalDEMStoragePath under LocalPipelineConfig.StorageDir.
        ///
        /// metersPerPixel defaults to OrbitalConfig.OrbitalDEMMetersPerPixel.
        ///
        /// Has OrthographicCameraModel that projects points in siteDrive frame to pixels on the DEM.
        ///
        /// Mission surface frames (e.g. SITE, LOCAL_LEVEL) are +X north, +Y east, +Z down.
        ///
        /// Orbital DEM images typically have latitude increasing with row and longitude increasing with col.
        ///
        /// Requires PlacesDB to map siteDrive to a Lat/Lon in DEM.
        ///
        /// Uses the planetary body given by OrbitalConfig.OrbitalBodyName.
        ///
        /// Throws exception if
        /// * failed to get lat/lon for siteDrive 
        /// * lat/lon for siteDrive outside bounds of DEM
        /// * no vaid elevation at lat/lon for siteDrive in DEM
        ///
        /// TODO #1034 optionally respect cfg.OrbitalImageMetersPerPixel
        /// TODO #1042 use either OrthographicCameraModel or GISCameraModel
        /// TODO #1015 validate PlacesDB orbital metadata
        /// TODO #1037 move this whole thing to ingest
        /// </summary>
        public static DEM LoadOrbitalDEM(MissionSpecific mission, SiteDrive siteDrive, ref string demFile,
                                         double? metersPerPixel = null, double? elevationScale = null,
                                         double minFilter = DEM.DEF_MIN_FILTER, double maxFilter = DEM.DEF_MAX_FILTER,
                                         ILogger logger = null)
        {
            var cfg = OrbitalConfig.Instance;
            
            if (string.IsNullOrEmpty(demFile) && !string.IsNullOrEmpty(cfg.OrbitalDEMStoragePath))
            {
                demFile = Path.Combine(LocalPipelineConfig.Instance.StorageDir, cfg.OrbitalDEMStoragePath);
            }
            if (string.IsNullOrEmpty(demFile) || !File.Exists(demFile))
            {
                throw new Exception("orbital DEM not found: " + demFile);
            }

            if (!metersPerPixel.HasValue)
            {
                metersPerPixel = cfg.OrbitalDEMMetersPerPixel;
            }

            if (!elevationScale.HasValue)
            {
                elevationScale = cfg.OrbitalDEMElevationScale;
            }

            var placesDB = new PlacesDB(logger, requireOrbital: true);
            var gisCam = new GISCameraModel(demFile, cfg.OrbitalBodyName);
            var originPixel = gisCam.LonLatToImage(placesDB.GetLonLat(siteDrive));
            
            mission.GetOrthonormalGISBasisInLocalLevelFrame(out Vector3 elevationDir,
                                                            out Vector3 rightDir, out Vector3 downDir);

            var mpp = gisCam.CheckLocalGISImageBasisAndGetResolution(originPixel, logger, throwOnError: true);

            double? originElevation = null; //DEM constructor will look this up given originPixel

            return DEM.OrthoDEM(new SparseGISElevationMap(demFile), elevationDir, rightDir, downDir,
                                metersPerPixel.Value, mpp.X / mpp.Y, elevationScale.Value,
                                originPixel, originElevation, minFilter, maxFilter);
        }
    }
}
