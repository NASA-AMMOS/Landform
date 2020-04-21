using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.MathExtensions;
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
        protected Matrix orbitalDEMToRoot; //unprojected point in orbitalDEM camera model -> project root frame

        protected WedgeCommand(WedgeCommandOptions wcopts) : base(wcopts)
        {
            this.wcopts = wcopts;
        }

        protected virtual bool ParseArgumentsAndLoadCaches(string outDir)
        {
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

            if (num == 0)
            {
                throw new Exception("no surface or orbital data available");
            }

            int numOrbital = wcopts.NoOrbital ? 0 : observationCache.GetAllObservations().Count(obs => obs.IsOrbital);
            int numSurface = num - numOrbital;

            wcopts.NoOrbital |= numOrbital == 0;
            wcopts.NoSurface |= numSurface == 0;

            pipeline.LogInfo("loaded {0}{1} surface observations{2} in project {3}{4}{5}",
                             numSurface, DescribeObservationFilter(),
                             numOrbital > 0 ? $" and {numOrbital} orbital observations" : "",
                             project.Name,
                             siteDrives.Length > 0 ? (" for sitedrives " + string.Join(", ", siteDrives)): "",
                             cams.Length > 0 ? (" for cameras " + string.Join(", ", cams)) : "");
        }

        protected void LoadOrbitalDEM()
        {
            try
            {
                int idx = Observation.ORBITAL_DEM_INDEX;
                var heightmap = LoadOrbitalAsset(idx);
                if (heightmap != null)
                {
                    var cfg = OrbitalConfig.Instance;
                    orbitalDEM = new DEM(heightmap, cfg.DEMMetersPerPixel, cfg.DEMMinFilter, cfg.DEMMaxFilter);
                    var obs = observationCache.GetObservation(idx);
                    orbitalDEMToRoot = frameCache.GetBestPrior(obs.FrameName).Transform.Mean;
                }
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("failed to load orbital DEM, running without it: {0}", ex.Message);
                wcopts.NoOrbital = true;
            }
        }

        /// <summary>
        /// Common implementation of LoadOrbitalDEM() and TextureCommand.LoadOrbitalImage().
        /// </summary>
        protected Image LoadOrbitalAsset(int obsIndex)
        {
            if (observationCache == null || !observationCache.ContainsObservation(obsIndex))
            {
                pipeline.LogInfo("orbital {0} not available (index {1}), continuing without it",
                                 obsIndex == Observation.ORBITAL_DEM_INDEX ? "DEM" : "image", obsIndex);
                wcopts.NoOrbital = true;
                return null;
            }
                
            var obs = observationCache.GetObservation(obsIndex);

            string filePath = obs.Url;
            if (!filePath.StartsWith("file://"))
            {
                throw new Exception($"URL for {obs.Name} is not local: {obs.Url}");
            }
            filePath = filePath.Substring(7);

            var cfg = OrbitalConfig.Instance;

            Image asset = null;
            if (obsIndex == Observation.ORBITAL_DEM_INDEX)

            {
                asset = cfg.DEMIsGeoTIFF ? new SparseGISElevationMap(filePath)
                    : Image.Load(filePath, ImageConverters.PassThrough);
            }
            else
            {
                asset = cfg.ImageIsGeoTIFF ? new SparseGISImage(filePath) : Image.Load(filePath);
            }
            asset.CameraModel = obs.CameraModel;

            pipeline.LogInfo("loaded {0}x{1} {2} as {3} using {4}: {5}", asset.Width, asset.Height, obs.Name,
                             asset.GetType().Name, asset.CameraModel.GetType().Name, filePath);

            return asset;
        }
    }
}
