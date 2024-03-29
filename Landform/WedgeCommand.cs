//#define DBG_STRETCHED
//#define DBG_BLURRED
//#define DBG_FRUSTA
using System;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using CommandLine;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.Imaging;
using JPLOPS.Geometry;
using JPLOPS.Pipeline;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Landform
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

        [Option(HelpText = "Mesh decimation method (EdgeCollapse, ResampleFSSR, ResamplePoisson)", Default = MeshDecimationMethod.ResampleFSSR)]
        public virtual MeshDecimationMethod MeshDecimator { get; set; }

        [Option(HelpText = "Only use specific surface observations, comma separated (e.g. MLF_452276219RASLS0311330MCAM02600M1)", Default = null)]
        public virtual string OnlyForObservations { get; set; }

        [Option(HelpText = "Only use specific surface frames, comma separated (e.g. MastcamLeft_00031013300028400454000060009001618010680001200000)", Default = null)]
        public virtual string OnlyForFrames { get; set; }

        [Option(HelpText = "Only use specific surface cameras, comma separated (e.g. Hazcam, Mastcam, Navcam, FrontHazcam, FrontHazcamLeft, etc)", Default = null)]
        public virtual string OnlyForCameras { get; set; }

        [Option(HelpText = "Only use observations from specific site drives SSSSSDDDDD, comma separated, wildcard xxxxx", Default = null)]
        public virtual string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Allow rover observations for which no suitable rover mask is available or could be generated", Default = false)]
        public virtual bool AllowUnmaskedRoverObservations { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted, Manual, Landform, LandformBEV, LandformBEVRoot, LandformBEVCalf, Agisoft)", Default = null)]
        public virtual string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior, LegacyManifest, PlacesDB, LocationsDB, PlacesDBSitePDSLocal, PDSChained, PDS)", Default = null)]
        public virtual string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public virtual bool UsePriors { get; set; }

        [Option(HelpText = "Use adjusted transforms only", Default = false)]
        public virtual bool OnlyAligned { get; set; }

        [Option(HelpText = "Preferred observation image texture variant (Original, Stretched, Blurred, Blended), Blended falls back to Stretched, Stretched falls back to Original", Default = TextureVariant.Stretched)]
        public virtual TextureVariant TextureVariant { get; set; }

        [Option(HelpText = "Length of the convex hull to use when finding observations to texture width (meters)", Default = TexturingDefaults.TEXTURE_FAR_CLIP)]
        public virtual double TextureFarClip { get; set; }

        [Option(HelpText = "Override median hue [0-360], negative disables (e.g. 33)", Default = -1)]
        public double OverrideMedianHue { get; set; }

        [Option(HelpText = "Image contrast stretch mode: None, StandardDeviation, or HistogramPercent ", Default = StretchMode.None)]
        public StretchMode StretchMode { get; set; }

        [Option(HelpText = "Optimize color contrast number of standard deviations", Default = 2)]
        public double StretchNumStdDev { get; set; }

        [Option(HelpText = "Redo observation image masks", Default = false)]
        public bool RedoObservationMasks { get; set; }

        [Option(HelpText = "Redo observation image stats", Default = false)]
        public bool RedoObservationStats { get; set; }

        [Option(HelpText = "Redo observation image hulls", Default = false)]
        public bool RedoObservationHulls { get; set; }

        [Option(HelpText = "Redo stretched observation textures", Default = false)]
        public bool RedoStretchedObservationTextures { get; set; }

        [Option(HelpText = "Just show list of image observations", Default = false)]
        public bool ListImageObservations { get; set; }
    }

    public class WedgeCommand : LandformCommand
    {
        public const int SPEW_INTERVAL_SEC = 10;

        protected WedgeCommandOptions wcopts;

        protected SiteDrive[] siteDrives;
        protected TransformSource[] priorSources;
        protected TransformSource[] adjustedSources;

        protected FrameCache frameCache;
        protected ObservationCache observationCache;

        protected string meshFrame;

        protected SiteDrive? rootSiteDrive;

        protected Matrix orbitalDEMToRoot; //unprojected point in orbitalDEM camera model -> project root frame
        protected Matrix orbitalTextureToRoot; //unprojected point in orbitalTexture camera model -> project root frame

        protected double orbitalDEMMetersPerPixel, orbitalTextureMetersPerPixel;

        protected DEM orbitalDEM;
        protected Image orbitalTexture;

        protected IDictionary<string, ConvexHull> obsToHull;
        protected List<Observation> imageObservations;
        protected List<Observation> orbitalImages;
        protected List<RoverObservation> roverImages;
        protected bool reverseNextRoverImagesIteration;
        protected Dictionary<int, Observation> indexedImages;
        protected double medianHue = -1;

        protected WedgeCommand(WedgeCommandOptions wcopts) : base(wcopts)
        {
            this.wcopts = wcopts;

            if (wcopts.Redo)
            {
                wcopts.RedoObservationMasks = true;
                wcopts.RedoObservationHulls = true;
                wcopts.RedoObservationStats = true;
                wcopts.RedoStretchedObservationTextures = true;
            }
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

            if (!base.ParseArguments(outDir))
            {
                return false; //help
            }

            if (wcopts.TextureFarClip > 0)
            {
                PDSImage.farLimit = wcopts.TextureFarClip;
            }

            if (project != null)
            {
                LoadFrameCache();
                LoadObservationCache();
            }

            HandleSpecialMeshFrames();

            if (wcopts.ListImageObservations)
            {
                ListImageObservations();
                return false;
            }

            if (wcopts.OverrideMedianHue >= 0 && wcopts.OverrideMedianHue <= 360)
            {
                medianHue = wcopts.OverrideMedianHue;
            }

            return true;
        }

        protected override bool ParseArguments(string outDir)
        {
            throw new NotImplementedException();
        }

        protected virtual string GetMeshFrame()
        {
            return project != null ? project.MeshFrame : "auto";
        }

        protected virtual string GetAutoMeshFrame()
        {
            return "newest";
        }

        protected virtual bool PassthroughMeshFrameAllowed()
        {
            return false;
        }

        protected virtual bool NonPassthroughMeshFrameAllowed()
        {
            return true;
        }

        protected virtual void HandleSpecialMeshFrames()
        {
            meshFrame = GetMeshFrame();

            if (string.IsNullOrEmpty(meshFrame))
            {
                return;
            }

            meshFrame = meshFrame.ToLower().Trim();

            if (meshFrame == "auto")
            {
                meshFrame = GetAutoMeshFrame();
            }
                
            string missionRoot = mission != null ? mission.RootFrameName() : null;

            var specials =
                new string[] { "passthrough", "newest", "oldest", "mission_root", "project_root", missionRoot };

            bool isSiteDrive = SiteDrive.IsSiteDriveString(meshFrame);
            bool isSpecial = !isSiteDrive && specials.Contains(meshFrame);

            if (!isSiteDrive && !isSpecial)
            {
                throw new Exception("unsupported mesh frame: " + meshFrame);
            }

            var origMeshFrame = meshFrame;
            if (meshFrame == "passthrough")
            {
                if (!PassthroughMeshFrameAllowed())
                {
                    throw new Exception("passthrough mesh frame not allowed");
                }
            }
            else if (!NonPassthroughMeshFrameAllowed())
            {
                throw new Exception("only passthrough mesh frame allowed");
            }

            if (meshFrame == "mission_root" || meshFrame == missionRoot)
            {
                meshFrame = "root"; //recognized as a meta-name by FrameCache.GetObservationTransform()
            }
            else if (meshFrame == "project_root")
            {
                if (rootSiteDrive == null)
                {
                    //this can happen if there were no frames to load or the frame cache was not loaded
                    throw new Exception("project root output requested but no root site drive");
                }
                if (rootSiteDrive == mission.GetLandingSiteDrive())
                {
                    meshFrame = "root";
                }
                else
                {
                    meshFrame = rootSiteDrive.ToString();
                }
            }
            else if (meshFrame == "newest" || meshFrame == "oldest")
            {
                if (observationCache == null)
                {
                    throw new Exception("observation cache not loaded, cannot resolve special frame: " + meshFrame);
                }
                                              
                var sds = observationCache
                    .GetAllObservations()
                    .Where(obs => obs is RoverObservation)
                    .Select(obs => ((RoverObservation)obs).SiteDrive)
                    .Distinct()
                    .ToArray();

                if (sds.Length == 0)
                {
                    throw new Exception("no sitedrives");
                }

                if (meshFrame == "newest")
                {
                    meshFrame = sds.OrderByDescending(sd => sd).First().ToString();
                }
                else
                {
                    meshFrame = sds.OrderBy(sd => sd).First().ToString();
                }

                isSiteDrive = true;
            }

            //some workflows do not load frame cache, for example updating scene manifest for tactical meshes
            if (isSiteDrive && frameCache != null && !frameCache.ContainsFrame(meshFrame))
            {
                throw new Exception("sitedrive frame not found: " + meshFrame);
            }

            pipeline.LogInfo("scene mesh frame: {0}{1}", meshFrame,
                             origMeshFrame != meshFrame ? " (" + origMeshFrame + ")" : "");
        }

        protected virtual void LoadFrameCache()
        {
            frameCache = new FrameCache(pipeline, project.Name);
            int num = frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, wcopts.UsePriors,
                                                           wcopts.OnlyAligned);
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

        protected virtual bool AllowNoObservations()
        {
            return false;
        }

        protected List<RoverObservation> GetRoverObservations(Func<RoverObservation, bool> filter = null)
        {
            return observationCache.GetAllObservations()
                .Where(obs => (obs is RoverObservation))
                .Cast<RoverObservation>()
                .Where(obs => (filter == null || filter(obs)))
                .OrderBy(obs => obs.Name)
                .ToList();
        }

        protected RoverObservation GetBestMaskObservation(RoverObservation obs)
        {
            var maskObs = observationCache.GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName))
                .Where(o => o is RoverObservation)
                .Cast<RoverObservation>()
                .Where(o => o.ObservationType == RoverProductType.RoverMask)
                .Where(o => o.IsLinear == obs.IsLinear)
                .Where(o => o.Width == obs.Width && o.Height == obs.Height)
                .ToList();

            if (maskObs.Count == 0)
            {
                return null;
            }
            
            var comparator = new RoverObservationComparator(mission);
            
            return comparator
                .KeepBestRoverObservations(maskObs, RoverObservationComparator.LinearVariants.Both,
                                           RoverProductType.RoverMask)
                .FirstOrDefault();
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
                        (!wcopts.NoSurface && (obs is RoverObservation) &&
                         (frameCache == null || frameCache.ContainsFrame(obs.FrameName)) &&
                         ObservationFilter((RoverObservation)obs) &&
                         (siteDrives.Length == 0 || siteDrives.Any(sd => sd == ((RoverObservation)obs).SiteDrive)) &&
                         (observations.Length == 0 || observations.Any(name => name == obs.Name)) &&
                         (frames.Length == 0 || frames.Any(name => name == obs.FrameName)) &&
                         (cams.Length == 0 || cams.Any(c => RoverCamera.IsCamera(c, ((RoverObservation)obs).Camera)))));

            if (!wcopts.AllowUnmaskedRoverObservations && (mission == null || !mission.CanMakeSyntheticRoverMasks()))
            {
                var roverObs = GetRoverObservations(obs => obs.ObservationType != RoverProductType.RoverMask);
                int nr = 0;
                foreach (var obs in roverObs)
                {
                    if (GetBestMaskObservation(obs) == null)
                    {
                        observationCache.Remove(obs);
                        pipeline.LogVerbose("removing observation {0}, no matching rover mask product", obs.Name);
                        nr++;
                    }
                }
                var maskObs = GetRoverObservations(obs => obs.ObservationType == RoverProductType.RoverMask);
                {
                    foreach (var obs in maskObs)
                    {
                        var off = observationCache.GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName))
                            .Where(o => o is RoverObservation)
                            .Cast<RoverObservation>()
                            .ToList();
                        if (off.Count == 1 && off[0] == obs)
                        {
                            observationCache.Remove(obs);
                            pipeline.LogVerbose("removing orphan mask observation {0}", obs.Name);
                            nr++;
                        }
                    }
                }
                num = observationCache.NumObservations();
                if (nr > 0)
                {
                    pipeline.LogInfo("removed {0} rover observations with no matching mask observation", nr);
                }
            }

            if (num == 0 && !AllowNoObservations())
            {
                throw new Exception("no" + DescribeObservationFilter() + " observations available");
            }

            int numOrbital = wcopts.NoOrbital ? 0 : observationCache.GetAllObservations().Count(obs => obs.IsOrbital);
            int numSurface = num - numOrbital;

            wcopts.NoOrbital |= numOrbital == 0;
            wcopts.NoSurface |= numSurface == 0;

            if (!wcopts.NoOrbital)
            {
                var cfg = OrbitalConfig.Instance;

                orbitalDEMMetersPerPixel = cfg.DEMMetersPerPixel;
                if (observationCache.ContainsObservation(Observation.ORBITAL_DEM_INDEX))
                {
                    var obs = observationCache.GetObservation(Observation.ORBITAL_DEM_INDEX);
                    orbitalDEMToRoot = frameCache.GetBestPrior(obs.FrameName).Transform.Mean;
                    //orbitalDEMMetersPerPixel = (obs.CameraModel as ConformalCameraModel).AvgMetersPerPixel;
                }

                orbitalTextureMetersPerPixel = cfg.ImageMetersPerPixel;
                if (observationCache.ContainsObservation(Observation.ORBITAL_IMAGE_INDEX))
                {
                    var obs = observationCache.GetObservation(Observation.ORBITAL_IMAGE_INDEX);
                    orbitalTextureToRoot = frameCache.GetBestPrior(obs.FrameName).Transform.Mean;
                    //orbitalTextureMetersPerPixel = (obs.CameraModel as ConformalCameraModel).AvgMetersPerPixel;
                }
            }

            orbitalImages = observationCache.GetAllObservations().Where(obs => obs.IsOrbitalImage).ToList();
            
            roverImages = GetRoverObservations(obs => obs.ObservationType == RoverProductType.Image);
            
            FilterRoverImages();
            
            imageObservations = roverImages.Cast<Observation>().ToList();
            imageObservations.AddRange(orbitalImages);
            
            indexedImages = new Dictionary<int, Observation>();
            foreach (var obs in imageObservations)
            {
                indexedImages[obs.Index] = obs;
            }

            pipeline.LogInfo("loaded {0}{1} surface observations{2} in project {3}{4}{5}",
                             numSurface, DescribeObservationFilter(),
                             numOrbital > 0 ? $" and {numOrbital} orbital observations" : "",
                             project.Name,
                             siteDrives.Length > 0 ? (" for sitedrives " + string.Join(", ", siteDrives)): "",
                             cams.Length > 0 ? (" for cameras " + string.Join(", ", cams)) : "");

            pipeline.LogInfo("{0} image observations ({1} surface, {2} orbital)", imageObservations.Count,
                             imageObservations.Count - orbitalImages.Count, orbitalImages.Count);
        }

        protected bool LoadOrbitalDEM(bool required = false)
        {
            try
            {
                var heightmap = LoadOrbitalAsset(Observation.ORBITAL_DEM_INDEX);
                if (heightmap == null)
                {
                    throw new Exception("failed to load orbital DEM");
                }
                var cfg = OrbitalConfig.Instance;
                orbitalDEM = new DEM(heightmap, cfg.DEMMetersPerPixel, cfg.DEMMinFilter, cfg.DEMMaxFilter);
                return true;
            }
            catch (Exception ex)
            {
                if (!required)
                {
                    pipeline.LogWarn(ex.Message);
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }

        protected bool LoadOrbitalTexture(bool required = false)
        {
            try
            {
                orbitalTexture = LoadOrbitalAsset(Observation.ORBITAL_IMAGE_INDEX);
                if (orbitalTexture == null)
                {
                    throw new Exception("failed to load orbital image");
                }
                return true;
            }
            catch (Exception ex)
            {
                if (!required)
                {
                    pipeline.LogWarn(ex.Message);
                    return false;
                }
                else
                {
                    throw;
                }
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
                if (cfg.ImageIsGeoTIFF)
                {
                    asset = new SparseGISImage(filePath, null, cfg.ByteImageIsSRGB);
                }
                else
                {
                    var conv = cfg.ByteImageIsSRGB ? ImageConverters.ValueRangeSRGBToNormalizedImageLinearRGB :
                        ImageConverters.ValueRangeToNormalizedImage;
                    asset = Image.Load(filePath, conv);
                }
            }
            asset.CameraModel = obs.CameraModel;

            pipeline.LogInfo("loaded {0}x{1} {2} as {3} using {4}: {5}", asset.Width, asset.Height, obs.Name,
                             asset.GetType().Name, asset.CameraModel.GetType().Name, filePath);

            return asset;
        }

        protected virtual void FilterRoverImages()
        {
            var comparator = new RoverObservationComparator(mission, pipeline);
            roverImages = comparator
                .KeepBestRoverObservations(roverImages, RoverObservationComparator.LinearVariants.Best,
                                           RoverProductType.Image)
                .OrderBy(obs => obs.Name)
                .ToList();
        }

        private void ListImageObservations()
        {
            if (imageObservations != null)
            {
                var allRoverObservations = observationCache.GetAllObservations()
                    .Where(obs => (obs is RoverObservation) &&
                           ((RoverObservation)obs).ObservationType == RoverProductType.Image)
                    .ToList();

                pipeline.LogInfo("{0} surface image observations, {1} linear variants selected for texturing:",
                                 allRoverObservations, roverImages.Count);
                foreach (var obs in allRoverObservations.OrderBy(obs => obs.Name))
                {
                    pipeline.LogInfo("{0} {1}selected for texturing", obs.Name,
                                     indexedImages.ContainsKey(obs.Index) ? "" : "not ");
                }

                pipeline.LogInfo("{0} orbital image observations:", orbitalImages.Count);
                foreach (var obs in orbitalImages.OrderBy(obs => obs.Name))
                {
                    pipeline.LogInfo(obs.Name);
                }
            }
            else
            {
                pipeline.LogInfo("no image observations");
            }
        }
            
        protected bool ReverseNextRoverImagesIteration()
        {
            bool ret = reverseNextRoverImagesIteration;
            reverseNextRoverImagesIteration = !ret;
            return ret;
        }

        protected IEnumerable<RoverObservation> GetRoverImagesInNextIterationOrder()
        {
            return ReverseNextRoverImagesIteration() ?
                roverImages.OrderByDescending(obs => obs.Name).ToList() : roverImages;
        }

        protected void BuildObservationImageMasks()
        {
            int no = roverImages.Count;
            int np = 0, nc = 0, nl = 0, nf = 0;
            double lastSpew = UTCTime.Now();
            CoreLimitedParallel.ForEachNoPartition(GetRoverImagesInNextIterationOrder(), obs =>
            {
                if (!wcopts.RedoObservationMasks && obs.MaskGuid != Guid.Empty)
                {
                    Interlocked.Increment(ref nc);
                    Interlocked.Increment(ref nl);
                    return;
                }
                
                Interlocked.Increment(ref np);

                double now = UTCTime.Now();
                if (!wcopts.NoProgress && (pipeline.Verbose || ((now - lastSpew) > SPEW_INTERVAL_SEC)))
                {
                    pipeline.LogInfo("creating mask for observation {0}, processing {1} in parallel, " +
                                     "completed {2}/{3}, {4} cached, {5} failed", obs.Name, np, nc, no, nl, nf);
                    lastSpew = now;
                }
                    
                try
                {
                    Image img = pipeline.LoadImage(obs.Url);

                    var maskObs = GetBestMaskObservation(obs);
                    
                    Image maskImage = ImageMasker.MakeMask(pipeline, masker, maskObs != null ? maskObs.Url : null, img);
                    
                    if (!wcopts.NoSave)
                    {
                        var maskProd = new PngDataProduct(maskImage);
                        pipeline.SaveDataProduct(project, maskProd); //leave cache enabled
                        obs.MaskGuid = maskProd.Guid;
                        obs.Save(pipeline);
                    }

                    Interlocked.Increment(ref nc);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref nf);
                    pipeline.LogException(ex, $"error creating mask for observation {obs.Name}");
                }

                Interlocked.Decrement(ref np);
            });

            pipeline.LogInfo("built masks for {0} observations, {1} cached, {2} failed", nc - nl, nl, nf);
        }

        protected void BuildObservationImageHulls()
        {
            BuildObservationImageHulls(false, false);
        }

        protected void BuildObservationImageHulls(bool forceRedo = false, bool noSave = false)
        {
            obsToHull = Backproject.BuildFrustumHulls(pipeline, frameCache, meshFrame, wcopts.UsePriors,
                                                      wcopts.OnlyAligned, GetRoverImagesInNextIterationOrder(), project,
                                                      wcopts.RedoObservationHulls || forceRedo, wcopts.NoSave || noSave,
                                                      farClip: wcopts.TextureFarClip);
#if DBG_FRUSTA
            if (wcopts.WriteDebug)
            {
                foreach (var entry in obsToHull)
                {
                    SaveMesh(entry.Value.Mesh, "Frusta/" + entry.Key);
                }
            }
#endif
        }

        protected void BuildObservationImageStats()
        {
            int no = roverImages.Count;
            int np = 0, nc = 0, nl = 0, nf = 0;
            double lastSpew = UTCTime.Now();
            CoreLimitedParallel.ForEachNoPartition(GetRoverImagesInNextIterationOrder(), obs =>
            {
                if (!wcopts.RedoObservationStats && obs.StatsGuid != Guid.Empty)
                {
                    Interlocked.Increment(ref nc);
                    Interlocked.Increment(ref nl);
                    return;
                }
                
                Interlocked.Increment(ref np);
                
                double now = UTCTime.Now();
                if (!wcopts.NoProgress && (pipeline.Verbose || ((now - lastSpew) > SPEW_INTERVAL_SEC)))
                {
                    pipeline.LogInfo("computing stats for observation {0}, processing {1} in parallel, " +
                                     "completed {2}/{3}, {4} cached, {5} failed", obs.Name, np, nc, no, nl, nf);
                    lastSpew = now;
                }

                try
                {
                    Image img = null;
                    if (obs.StretchedGuid != Guid.Empty)
                    {
                        img = pipeline.GetDataProduct<PngDataProduct>(project, obs.StretchedGuid, noCache: true).Image;
                    }
                    else
                    {
                        img = pipeline.LoadImage(obs.Url);
                        if (obs.MaskGuid != Guid.Empty)
                        {
                            //let the masks stay in the LRU cache here
                            //they might be used in BuildBlurredObservationImages()
                            //and most commands clear the image cache entirely after all Build*() phases are done
                            var mask = pipeline.GetDataProduct<PngDataProduct>(project, obs.MaskGuid).Image;
                            img = new Image(img); //don't mutate cached image
                            img.UnionMask(mask, new float[] { 0 }); //0 means bad, 1 means good
                        }
                    }

                    if (!wcopts.NoSave)
                    {
                        var statsProd = new ImageStats(img);
                        pipeline.SaveDataProduct(project, statsProd, noCache: true);
                        obs.StatsGuid = statsProd.Guid;
                        obs.Save(pipeline);
                    }
                    Interlocked.Increment(ref nc);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref nf);
                    pipeline.LogException(ex, $"error computing stats for observation {obs.Name}");
                }

                Interlocked.Decrement(ref np);
            });

            pipeline.LogInfo("built image stats for {0} observations, {1} cached, {2} failed", nc - nl, nl, nf);

            int numColor = Backproject.GetImageStats(pipeline, project,
                                                     roverImages, //doesn't load images, don't toggle order
                                                     out double lumaMed, out double lumaMAD, out double hueMed);

            if (numColor > 0 && wcopts.OverrideMedianHue < 0)
            {
                medianHue = hueMed;
            }

            pipeline.LogInfo("global luminance median {0:f3}, MAD {1:f3}, hue median {2:f3}, {3}/{4} images color",
                             lumaMed, lumaMAD, hueMed, numColor, roverImages.Count);
        }

        protected void BuildStretchedObservationImages()
        {
            int no = roverImages.Count;
            int np = 0, nc = 0, nl = 0, nf = 0;
            double lastSpew = UTCTime.Now();
            CoreLimitedParallel.ForEachNoPartition(GetRoverImagesInNextIterationOrder(), obs =>
            {
                if (!wcopts.RedoStretchedObservationTextures && obs.StretchedGuid != Guid.Empty)
                {
#if DBG_STRETCHED
                    if (wcopts.WriteDebug)
                    {
                        SaveDebugWedgeImage
                            (pipeline.GetDataProduct<PngDataProduct>(project, obs.StretchedGuid, noCache: true).Image,
                             obs, "_stretched");
                    }

#endif
                    Interlocked.Increment(ref nc);
                    Interlocked.Increment(ref nl);
                    return;
                }
                
                Interlocked.Increment(ref np);

                double now = UTCTime.Now();
                if (!wcopts.NoProgress && (pipeline.Verbose || ((now - lastSpew) > SPEW_INTERVAL_SEC)))
                {
                    pipeline.LogInfo("computing stretched image for observation {0}, processing {1} in parallel, " +
                                     "completed {2}/{3}, {4} cached, {5} failed", obs.Name, np, nc, no, nl, nf);
                    lastSpew = now;
                }

                try
                {
                    Image img = null;
                    if (wcopts.StretchMode != StretchMode.None)
                    {
                        img = pipeline.LoadImage(obs.Url);
                        if (obs.MaskGuid != Guid.Empty)
                        {
                            var mask =
                                pipeline.GetDataProduct<PngDataProduct>(project, obs.MaskGuid, noCache: true).Image;
                            img = new Image(img); //don't mutate cached image
                            img.UnionMask(mask, new float[] { 0 }); //0 means bad, 1 means good
                        }
                    }

                    Image stretchedImage = null; //don't mutate cached image
                    if (wcopts.StretchMode == StretchMode.StandardDeviation)
                    {
                        stretchedImage = new Image(img);
                        stretchedImage.ApplyStdDevStretch(wcopts.StretchNumStdDev);
                    }
                    else if (wcopts.StretchMode == StretchMode.HistogramPercent)
                    {
                        stretchedImage = new Image(img);
                        stretchedImage.HistogramPercentStretch();
                    }
                    
#if DBG_STRETCHED
                    if (wcopts.WriteDebug && stretchedImage != null)
                    {
                        SaveDebugWedgeImage(stretchedImage, obs, "_stretched");
                    }
#endif
                    
                    if (!wcopts.NoSave)
                    {
                        if (stretchedImage != null)
                        {
                            var imgProd = new PngDataProduct(stretchedImage);
                            pipeline.SaveDataProduct(project, imgProd, noCache: true);
                            obs.StretchedGuid = imgProd.Guid;
                        }
                        else
                        {
                            obs.StretchedGuid = Guid.Empty;
                        }
                        obs.Save(pipeline);
                    }
                    
                    Interlocked.Increment(ref nc);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref nf);
                    pipeline.LogException(ex, $"error creating stretched image for observation {obs.Name}");
                }

                Interlocked.Decrement(ref np);
            });
            pipeline.LogInfo("built stretched images for {0} observations, {1} cached, {2} failed", nc - nl, nl, nf);
        }
    }
}
