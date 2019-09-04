using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Xna.Framework;
using CommandLine;
using log4net;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;
using OPS.Util;
using OPS.Imaging;
using OPS.Imaging.Emgu;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    public enum SiteDrivePriority { NewestFirst, OldestFirst, BiggestFirst, SmallestFirst };
    
    public enum AlignmentMode { PairwiseMinimal, PairwiseMaximal, Simultaneous, None };

    public enum CalfMode { None, Centroid, Temporal };

    [Verb("local-bev-align", HelpText = "birds eye view alignment")]
    public class LocalBEVAlignerOptions : LandformCommandOptions
    {
        [Option(HelpText = "Only align specific site drives SSSSSDDDDD, comma separated, wildcard xxxxx", Default = null)]
        public string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Only load specific frames, comma separated", Default = null)]
        public string OnlyForFrames { get; set; }

        [Option(HelpText = "Don't adjust specified site drives (or \"newest\", \"oldest\", \"largest\", \"smallest\"), comma separated", Default = null)]
        public string FixSiteDrives { get; set; }

        [Option(HelpText = "Alignment algorithm: PairwiseMinimal, PairwiseMaximal, Simultaneous, None (match only)", Default = AlignmentMode.PairwiseMinimal)]
        public AlignmentMode AlignmentMode { get; set; }

        [Option(HelpText = "Algorithm to bring un-aligned \"calf\" site drives along for the ride: None, Centroid (match to aligned site drive with closest horizontal centroid), Temporal (match to closest aligned site drive by acquisition time)", Default = CalfMode.Centroid)]
        public CalfMode CalfMode { get; set; }

        [Option(HelpText = "In pairwise alignment modes lower priority site drives will be aligned to higher priority ones (NewestFirst, OldestFirst, BiggestFirst, SmallestFirst)", Default = SiteDrivePriority.OldestFirst)]
        public SiteDrivePriority SiteDrivePriority { get; set; }

        [Option(HelpText = "Only use products from specific cameras, comma separated (FrontHazcamLeft, FrontHazcamRight, RearHazcamLeft, RearHazcamRight, NavcamLeft, NavcamRight, MastcamLeft, MastcamRight, MAHLI)", Default = "NavcamLeft")]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Stop after rendering BEVs (and DEMs)", Default = false)]
        public bool OnlyRenderBEVs { get; set; }

        [Option(HelpText = "Stop after detecting features", Default = false)]
        public bool OnlyDetectFeatures { get; set; }

        [Option(HelpText = "Wedge mesh decimation blocksize, or -1 for auto", Default = -1)]
        public int DecimateMeshes { get; set; }

        [Option(HelpText = "Wedge image decimation blocksize, or -1 for auto", Default = -1)]
        public int DecimateImages { get; set; }

        [Option(HelpText = "Auto wedge image decimation target resolution", Default = 512)]
        public int TargetImageResolution { get; set; }

        [Option(HelpText = "Auto wedge mesh decimation target resolution", Default = 256)]
        public int TargetMeshResolution { get; set; }

        [Option(HelpText = "Max triangle aspect ratio for organized mesh reconstruction", Default = 10)]
        public double MaxTriangleAspect { get; set; }

        [Option(HelpText = "Disable generating organized mesh normals when normal image missing", Default = false)]
        public bool NoGenerateNormals { get; set; }

        [Option(HelpText = "Birds eye view meters per pixel", Default = 0.005)]
        public double BEVMetersPerPixel { get; set; }

        [Option(HelpText = "Birds eye view max radius in meters from site drive origin, 0 or negative for unlimited", Default = 20)]
        public double MaxBEVRadius { get; set; }

        [Option(HelpText = "Max dense BEV image dimension, 0 or negative to use max heap allocation size", Default = 0)]
        public int SparseImageThreshold { get; set; }

        [Option(HelpText = "Birds eye view blend mode (Over, Average, Max, Min)", Default = BlendMode.Max)]
        public BlendMode BEVBlending { get; set; }

        [Option(HelpText = "Birds eye view coloring (Texture, Tilt, Elevation}", Default = BirdsEyeViewing.ColorMode.Tilt)]
        public BirdsEyeViewing.ColorMode BEVColoring { get; set; }

        [Option(HelpText = "Birds eye view sparse invalidation blocksize, relative to largest image dimension if < 1, disabled if 0", Default = 0.005)]
        public double BEVSparseBlocksize { get; set; }

        [Option(HelpText = "Birds eye view sparse invalidation block threshold", Default = 0.8)]
        public double BEVMinValidBlockRatio { get; set; }

        [Option(HelpText = "Birds eye view smoothing box size (should be odd)", Default = 1)]
        public int BEVSmoothing { get; set; }

        [Option(HelpText = "Birds eye view decimation", Default = 2)]
        public int BEVDecimation { get; set; }

        [Option(HelpText = "Inpaint birds eye view images by this many pixels, 0 to disable, negative for unlimited", Default = 20)]
        public int BEVInpaint { get; set; }

        [Option(HelpText = "Threshold BEV images at this level", Default = 0)]
        public double BEVThreshold { get; set; }

        [Option(HelpText = "Detector type", Default = FeatureDetector.DetectorType.FAST)]
        public FeatureDetector.DetectorType DetectorType { get; set; }

        [Option(HelpText = "Maximum number of features per image", Default = 50000)]
        public int MaxFeaturesPerImage { get; set; }

        [Option(HelpText = "Extra radius to cull features near invalid regions", Default = 4)]
        public int FeatureExtraInvalidRadius { get; set; }

        [Option(HelpText = "FAST detector threshold", Default = 5)]
        public int FASTThreshold { get; set; }

        [Option(HelpText = "Minimum feature response", Default = 10)]
        public double MinFeatureResponse { get; set; }

        [Option(HelpText = "Write products for debugging", Default = false)]
        public bool WriteDebug { get; set; }

        [Option(HelpText = "Debug output directory, omit to save to local project storage", Default = null)]
        public string DebugOutputFolder { get; set; }

        [Option(HelpText = "Debug mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public string MeshFormat { get; set; }

        [Option(HelpText = "Debug image format, e.g. png, jpg, help for list", Default = "png")]
        public string ImageFormat { get; set; }

        [Option(HelpText = "Recompute existing BEVs", Default = false)]
        public bool RedoBEVs { get; set; }

        [Option(HelpText = "Recompute existing features", Default = false)]
        public bool RedoFeatures { get; set; }

        [Option(HelpText = "Recompute existing feature matches", Default = false)]
        public bool RedoMatches { get; set; }

        [Option(HelpText = "Redo everything", Default = false)]
        public bool Redo { get; set; }

        [Option(HelpText = "Search radius for feature matching in meters", Default = 1)]
        public double MatchRadius { get; set; }

        [Option(HelpText = "Max descriptor distance ratio", Default = 1)]
        public double MaxDescriptorDistanceRatio { get; set; }

        [Option(HelpText = "Max descriptor distance", Default = 500)]
        public double MaxDescriptorDistance { get; set; }

        [Option(HelpText = "Disable bidirectional feature matching", Default = false)]
        public bool NoBidirectionalMatching { get; set; }

        [Option(HelpText = "Max RANSAC tests", Default = 5000000)]
        public int MaxRansacTests { get; set; }

        [Option(HelpText = "Max RANSAC residual in meters", Default = 0.02)]
        public double MaxRansacResidual { get; set; }

        [Option(HelpText = "Max RANSAC feature match radius meters", Default = 0.05)]
        public double RansacMatchRadius { get; set; }

        [Option(HelpText = "Min RANSAC feature separation meters", Default = 0.05)]
        public double MinRansacSeparation { get; set; }

        [Option(HelpText = "Min RANSAC good matches", Default = 25)]
        public int MinRansacMatches { get; set; }

        [Option(HelpText = "Max RANSAC good matches", Default = 500)]
        public int MaxRansacMatches { get; set; }

        [Option(HelpText = "Optimize contrast", Default = true)]
        public bool StretchContrast { get; set; }

        [Option(HelpText = "Optimize color contrast number of standard deviations", Default = 2)]
        public double StretchStdDevs { get; set; }

        [Option(HelpText = "Spatial outlier number of mean absolute deviations", Default = 5)]
        public double SpatialOutlierMADs { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }
    }

    public class LocalBEVAligner : LandformCommand
    {
        private LocalBEVAlignerOptions options;

        private Project project;
        private MissionSpecific mission;
        private RoverMasker masker;

        private string outputPath;
        private string imageExt;
        private string meshExt;

        private FrameCache frameCache;
        private ObservationCache observationCache;

        private List<MeshObservations> observations;

        private string[] siteDrives;

        //sitedrive name => (observation, mesh, image), (observation, mesh, image), ...
        private ConcurrentDictionary<string, ConcurrentBag<Tuple<string, Mesh, Image>>> mergeInputs =
            new ConcurrentDictionary<string, ConcurrentBag<Tuple<string, Mesh, Image>>>();

        //sitedrive name => BEV image
        private ConcurrentDictionary<string, Image> bevs = new ConcurrentDictionary<string, Image>();

        //sitedrive name => DEM image
        private ConcurrentDictionary<string, Image> dems = new ConcurrentDictionary<string, Image>();

        //sitedrive name => pixel in BEV image corresponding to world frame origin, based on priors
        private ConcurrentDictionary<string, Vector2> bevOrigins = new ConcurrentDictionary<string, Vector2>();

        //sitedrive name => features sorted by increasing distance to origin of sitedrive
        private ConcurrentDictionary<string, ImageFeature[]> features =
            new ConcurrentDictionary<string, ImageFeature[]>();

        //modelSiteDrive-dataSiteDrive => feature matches
        private ConcurrentDictionary<string, FeatureMatch[]> matches =
            new ConcurrentDictionary<string, FeatureMatch[]>();

        //modelSiteDrive-dataSiteDrive => feature matches
        private ConcurrentDictionary<string, FeatureMatch[]> ransacMatches =
            new ConcurrentDictionary<string, FeatureMatch[]>();

        //modelSiteDrive-dataSiteDrive => (modelPoint, dataPoint), (modelPoint, dataPoint), ...
        private ConcurrentDictionary<string, SpatialMatch[]> spatialMatches =
            new ConcurrentDictionary<string, SpatialMatch[]>();

        //(modelSiteDrive, dataSiteDrive), (modelSiteDrive, dataSiteDrive), ...
        List<Tuple<string, string>> siteDrivePairs = new List<Tuple<string, string>>();
        
        private double MetersPerPixel { get { return options.BEVMetersPerPixel * options.BEVDecimation; } }
        private double PixelsPerMeter { get { return 1 / MetersPerPixel; } }

        private Matrix SiteDriveTransform(string siteDrive)
        {
            return frameCache.GetBestPrior(siteDrive).Transform.Mean;
        }

        /// <summary>
        /// map a 3D point in meters from a given site drive to a 2D pint in pixels in a given site drive
        /// </summary>
        private Vector2 PointToPixel(Vector3 srcPoint, string srcSiteDrive, string dstSiteDrive)
        {
            var srcToRoot = SiteDriveTransform(srcSiteDrive);
            var ptInRoot = Vector3.Transform(srcPoint, srcToRoot);
            var pixelInRoot = ptInRoot * PixelsPerMeter;
            return bevOrigins[dstSiteDrive] + new Vector2(pixelInRoot.X, pixelInRoot.Y);
        }

        private static System.Drawing.PointF ToPointF(Vector2 v)
        {
            return new System.Drawing.PointF((float)v.X, (float)v.Y);
        }

        private static LineSegment2DF ToLineSegment2DF(Vector2 a, Vector2 b)
        {
            return new LineSegment2DF(ToPointF(a), ToPointF(b));
        }

        //return the index of the first entry in distances that is >= distance
        //yes there is a built-in Array.BinarySearch()
        //but here we can control behavior when distance is not actually present in distances
        private static int BinarySearch(double[] distances, double distance)
        {
            int l = 0, u = distances.Length - 1;
            while (u - l > 1)
            {
                var m = (u + l) / 2;
                if (distance <= distances[m])
                {
                    u = m;
                }
                else
                {
                    l = m;
                }
            }
            return u;
        }

        private int BEVArea(string siteDrive)
        {
            var bev = bevs[siteDrive];
            return bev.Width * bev.Height;
        }

        public LocalBEVAligner(LocalBEVAlignerOptions options) : base(options)
        {
            this.options = options;

            if (options.Redo)
            {
                options.RedoBEVs = true;
                options.RedoFeatures = true;
                options.RedoMatches = true;
            }
        }

        public int Run()
        {
            project = Project.Find(pipeline, options.ProjectName);
            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }
            mission = MissionSpecific.GetInstance(project.Mission);
            masker = mission.GetMasker();

            string dir = "alignment/AdjustProducts/";
            outputPath = pipeline.GetLocalDebugFolder(options.DebugOutputFolder, dir, project.Name);
            //don't ensure outputPath exists here, we may never need it

            if (options.WriteDebug)
            {
                meshExt = MeshSerializers.Instance.CheckFormat(options.MeshFormat, pipeline);
                if (meshExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} debug meshes to {1}", meshExt, outputPath);

                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} debug images to {1}", imageExt, outputPath);
            }

            double startSec = UTCTime.Now();

            frameCache = new FrameCache(pipeline, options.ProjectName);
            frameCache.Preload(loadTransforms: true, transformFilter: ft => ft.IsPrior());

            observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.Preload();

            var opts = new MeshObservations.CollectOptions(options.OnlyForSiteDrives, options.OnlyForFrames,
                                                           options.OnlyForCameras, mission)
            {
                AllowMastcam = false,
                RequirePoints = true,
                RequireNormals = options.BEVColoring == BirdsEyeViewing.ColorMode.Tilt && options.NoGenerateNormals,
                RequireTextures = options.BEVColoring == BirdsEyeViewing.ColorMode.Texture,
                RequirePriorTransform = true,
                TargetFrame = "root"
            };
            observations = MeshObservations.Collect(frameCache, observationCache, opts);

            //for now lexicographically sort siteDrives so that older ones come before newer
            //just to give a canonical order
            //later in ComputePairs we may change the order depending on options
            siteDrives = observations.Select(obs => obs.SiteDrive.ToString()).Distinct().OrderBy(sd => sd).ToArray();

            if (siteDrives.Length < 2 && !(options.OnlyRenderBEVs || options.OnlyDetectFeatures))
            {
                pipeline.LogError("birds eye view aligner requires at least 2 site drives");
                return 1;
            }

            pipeline.LogInfo("computing birds eye view alignment for {0} observations, {1} site drives",
                             observations.Count(), siteDrives.Length);

            LoadOrRenderBEVs(); //observations -> bevs, dems

            if (options.OnlyRenderBEVs)
            {
                pipeline.LogInfo("rendered birds eye views for {0} site drives ({1:F3}s)",
                                 bevs.Count, UTCTime.Now() - startSec);
                return 0;
            }

            LoadOrDetectFeatures(); //bevs -> features

            if (options.OnlyDetectFeatures)
            {
                pipeline.LogInfo("rendered birds eye views for {0} site drives and detected features ({1:F3}s)",
                                 bevs.Count, UTCTime.Now() - startSec);
                return 0;
            }

            ComputePairs(); //siteDrives -> siteDrivePairs

            int nm = LoadOrMatchPairs(); //siteDrivePairs, features -> spatialMatches

            int na = Align(); //spatialMatches -> LandformBEV aligned FrameTransforms

            bool matchOnly = options.AlignmentMode == AlignmentMode.None;
            pipeline.LogInfo("matched {0}{1} site drives from {2} birds eye views ({3:F3}s)",
                             matchOnly ? "" : "and aligned ", matchOnly ? nm : na,
                             bevs.Count, UTCTime.Now() - startSec);

            return 0;
        }

        /// <summary>
        /// populates mergeInputs with individual wedge meshes and textures from observations
        /// </summary>
        private void BuildWedgeMeshes()
        {
            double startSec = UTCTime.Now();
            int no = observations.Count();
            pipeline.LogInfo("creating wedge meshes for {0} observations...", no);

            var meshOpts = new MeshObservations.MeshOptions()
                {
                    Frame = "root",
                    UsePriors = true,
                    MaxTriangleAspect = options.MaxTriangleAspect,
                    GenerateNormals = !options.NoGenerateNormals
                };

            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(observations, obs => { 

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("computing products for {0} observations in parallel, completed {1}/{2}",
                                         np, nc, no);
                    }

                    int mbs = MeshObservations.AutoDecimate(obs.Points, options.DecimateMeshes, options.TargetMeshResolution);
                    if (mbs > 1 && mbs != options.DecimateMeshes)
                    {
                        pipeline.LogVerbose("auto decimating wedge mesh {0} with blocksize {1}", obs.Name, mbs);
                    }
                    var mo = meshOpts.Clone();
                    mo.Decimate = mbs;
                    Mesh mesh = obs.BuildOrganizedMesh(pipeline, frameCache, masker, mo);

                    Image img = null;
                    if (options.BEVColoring == BirdsEyeViewing.ColorMode.Texture && obs.Texture != null)
                    {
                        img = pipeline.LoadImage(obs.Texture.Url);
                        int ibs = MeshObservations.AutoDecimate(obs.Texture, options.DecimateImages, options.TargetImageResolution);
                        if (ibs > 1)
                        {
                            if (ibs != options.DecimateImages)
                            {
                                pipeline.LogVerbose("auto decimating wedge image {0} with blocksize {1}", obs.Name, ibs);
                            }
                            img = img.Decimated(ibs);
                        }
                    }

                    var input = new Tuple<string, Mesh, Image>(obs.Points.Name, mesh, img);
                    mergeInputs.AddOrUpdate(obs.SiteDrive.ToString(),
                                            _ => new ConcurrentBag<Tuple<string, Mesh, Image>>(new [] { input }),
                                            (_, bag) => { bag.Add(input); return bag; });

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            pipeline.LogInfo("created wedge meshes for {0} observations ({1:F3}s)", nc, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// populates bevs, dems, and bevOrigins from database or observations
        /// </summary>
        private void LoadOrRenderBEVs()
        {
            if (options.RedoBEVs || !LoadBEVs())
            {
                BuildWedgeMeshes();
                RenderBEVs();
                SaveBEVs();
            }

            PostProcessBEVs(out double min, out double max);

            if (options.WriteDebug)
            {
                PathHelper.EnsureExists(outputPath);
                double startSec = UTCTime.Now();
                int np = 0, nc = 0;
                CoreLimitedParallel.ForEach(bevs, pair => {

                        Interlocked.Increment(ref np);

                        if (!options.NoProgress)
                        {
                            pipeline.LogInfo("saving {0} birds eye view images in parallel, completed {1}/{2}",
                                             np, nc, bevs.Count);
                        }

                        var siteDrive = pair.Key;
                        var bev = pair.Value;
                        if (!options.StretchContrast && options.BEVColoring == BirdsEyeViewing.ColorMode.Elevation)
                        {
                            bev = new Image(bev);
                            bev.ScaleValues((float)min, (float)max, 0, 1);
                        }
                        bev.Save<byte>(outputPath + siteDrive + "_BirdsEyeView" + imageExt);

                        Interlocked.Decrement(ref np);
                        Interlocked.Increment(ref nc);
                    });
                pipeline.LogInfo("saved {0} birds eye view images ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
            }
        }

        /// <summary>
        /// render any BEV and DEM images that were not loaded from database
        /// </summary>
        private void RenderBEVs()
        {
            double startSec = UTCTime.Now();
            var bevsNeeded = siteDrives.Where(sd => !bevs.ContainsKey(sd) || !dems.ContainsKey(sd)).ToArray();
            pipeline.LogInfo("rendering {0} birds eye views...", bevsNeeded.Length);

            var bevOptions = new Rasterizer.BEVOptions
            {
                BlendMode = options.BEVBlending,
                MetersPerPixel = options.BEVMetersPerPixel,
                SparseBlockSize = options.BEVSparseBlocksize,
                MinSparseBlockValidRatio = options.BEVMinValidBlockRatio,
                Inpaint = options.BEVInpaint,
                Blur = options.BEVSmoothing,
                Decimate = options.BEVDecimation,
                MaxRadiusMeters = options.MaxBEVRadius,
                RadiusRelativeToOrigin = true,
                ImageFactory = (bands, width, height) =>
                {
                    string err = null;
                    if (options.SparseImageThreshold > 0 && width > options.SparseImageThreshold)
                    {
                        err = string.Format("width {0} > {1}", width, options.SparseImageThreshold);
                    }
                    if (options.SparseImageThreshold > 0 && err == null && height > options.SparseImageThreshold)
                    {
                        err = string.Format("height {0} > {1}", height, options.SparseImageThreshold);
                    }
                    if (err == null)
                    {
                        err = Image.CheckSize(bands, width, height);
                    }
                    if (string.IsNullOrEmpty(err))
                    {
                        return new Image(bands, width, height);
                    }
                    else
                    {
                        pipeline.LogVerbose("using sparse image to render {0}x{1} {2} band birds eye view: {3}",
                                            width, height, bands, err);
                        return new SparseImage(bands, width, height);
                    }
                }
            };

            var demOptions = bevOptions.Clone();
            demOptions.BlendMode = BlendMode.Average;

            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(bevsNeeded, siteDrive => {

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("rendering {0} birds eye views in parallel, completed {1}/{2}",
                                         np, nc, bevsNeeded.Length);
                    }

                    Mesh mesh = null;
                    Image img = null;

                    //ensure inputs are in a canonical order particularly for BEVBlending = Over
                    var inputs = mergeInputs[siteDrive]
                    .OrderBy(inp => inp.Item1) //order by observation name
                    .Distinct() //ConcurrentBag is not necessarily a set
                    .Select(inp => new Tuple<Mesh, Image>(inp.Item2, inp.Item3))
                    .ToArray();
                    
                    if (options.BEVColoring == BirdsEyeViewing.ColorMode.Texture)
                    {
                        var pair = Mesh.MergeMeshesAndTextures(inputs);
                        mesh = pair.Item1;
                        img = pair.Item2;
                    }
                    else
                    {
                        mesh = Mesh.Merge(inputs.Select(pr => pr.Item1).ToArray());
                    }
                    
                    switch (options.BEVColoring)
                    {
                        case BirdsEyeViewing.ColorMode.Texture: break;
                        case BirdsEyeViewing.ColorMode.Tilt:
                        {
                            mesh.ColorByNormals(TiltMode.InvAcos);
                            break;
                        }
                        case BirdsEyeViewing.ColorMode.Elevation:
                        {
                            mesh.ColorByElevation(absolute: true);
                            break;
                        }
                    }
                    
                    if (options.WriteDebug)
                    {
                        string imageFilename = null;
                        if (img != null)
                        {
                            imageFilename = siteDrive + imageExt;
                            PathHelper.EnsureExists(outputPath);
                            img.Save<byte>(outputPath + imageFilename);
                        }
                        PathHelper.EnsureExists(outputPath);
                        mesh.Save(outputPath + siteDrive + meshExt, imageFilename);
                    }

                    var sdToWorld = frameCache.GetBestTransform(siteDrive).Transform.Mean;
                    var sdOrigin = Vector3.Transform(Vector3.Zero, sdToWorld);
                    var sdOriginPixel = new Vector2(sdOrigin.X, sdOrigin.Y) / options.BEVMetersPerPixel;

                    if (!bevs.ContainsKey(siteDrive))
                    {
                        pipeline.LogVerbose("rendering birds eye view for site drive {0}...", siteDrive);
                        Vector2 origin = sdOriginPixel;
                        var bev = Rasterizer.RenderBirdsEyeView(mesh, img, ref origin, bevOptions);
                        
                        pipeline.LogVerbose("birds eye view for site drive {0}: {1}x{2}, origin ({3}, {4}), " +
                                            "{5} meters/pixel ({6} with decimation), sparse block size {7}, " +
                                            "valid block ratio {8}, inpaint {9}, smoothing {10}, decimation {11}, " +
                                            "max radius {12}m",
                                            siteDrive, bev.Width, bev.Height, (int)origin.X, (int)origin.Y,
                                            options.BEVMetersPerPixel, MetersPerPixel, options.BEVSparseBlocksize,
                                            options.BEVMinValidBlockRatio, options.BEVInpaint, options.BEVSmoothing,
                                            options.BEVDecimation, options.MaxBEVRadius);

                        try
                        {
                            if (bev is SparseImage)
                            {
                                bev = (bev as SparseImage).Densify();
                                pipeline.LogVerbose("densified {0}x{1} birds eye view for site drive {2}",
                                                    bev.Width, bev.Height, siteDrive);
                            }
                        }
                        catch (Exception ex)
                        {
                            throw new Exception(string.Format("cannot densify birds eye view for site drive {0}, " +
                                                              "try increasing BEV decimation (currently {1}): {2}",
                                                              siteDrive, options.BEVDecimation, ex.Message));
                        }

                        bevs[siteDrive] = bev;
                        bevOrigins[siteDrive] = origin;
                    }

                    if (!dems.ContainsKey(siteDrive))
                    {
                        var bev = bevs[siteDrive];
                        var origin = bevOrigins[siteDrive];

                        if (options.BEVColoring == BirdsEyeViewing.ColorMode.Elevation &&
                            options.BEVBlending == BlendMode.Average)
                        {
                            dems[siteDrive] = new Image(bev); //deep copy - BEV may later be post-processed
                        }
                        else
                        {
                            mesh.ColorByElevation(absolute: true);
                            Vector2 demOrigin = sdOriginPixel;
                            var dem = Rasterizer.RenderBirdsEyeView(mesh, null, ref demOrigin, demOptions);
                            if (dem.Width != bev.Width || dem.Height != bev.Height)
                            {
                                throw new Exception(string.Format("DEM dimensions {0}x{1} don't match BEV {2}x{3}",
                                                                  dem.Width, dem.Height, bev.Width, bev.Height));
                            }

                            if (demOrigin != origin)
                            {
                                throw new Exception(string.Format("DEM origin {0} doesn't match BEV {1}",
                                                                  demOrigin, origin));
                            }

                            try
                            {
                                if (dem is SparseImage)
                                {
                                    dem = (dem as SparseImage).Densify();
                                    pipeline.LogVerbose("densified {0}x{1} DEM for site drive {2}",
                                                        dem.Width, dem.Height, siteDrive);
                                }
                            }
                            catch (Exception ex)
                            {
                                throw new Exception(string.Format("cannot densify DEM for site drive {0}, " +
                                                                  "try increasing BEV decimation (currently {1}): {2}",
                                                                  siteDrive, options.BEVDecimation, ex.Message));
                            }

                            dems[siteDrive] = dem;
                        }
                    }
                        
                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            pipeline.LogInfo("generated {0} birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// populate bevs, dems, and bevOrigins from database
        /// returns true iff all were loaded successfully
        /// </summary>
        private bool LoadBEVs()
        {
            double startSec = UTCTime.Now();
            CoreLimitedParallel.ForEach(siteDrives, siteDrive => {
                    var rec = BirdsEyeView.Find(pipeline, project.Name, siteDrive);
                    if (rec != null &&
                        rec.Coloring == options.BEVColoring &&
                        rec.Blending == options.BEVBlending &&
                        rec.MetersPerPixel == options.BEVMetersPerPixel &&
                        rec.SparseBlockSize == options.BEVSparseBlocksize &&
                        rec.MinValidBlockRatio == options.BEVMinValidBlockRatio &&
                        rec.Inpaint == options.BEVInpaint &&
                        rec.Smoothing == options.BEVSmoothing &&
                        rec.Decimation == options.BEVDecimation)
                    {
                        var bev = pipeline.GetDataProduct<TiffDataProduct>(project, rec.BEVGuid).Image;
                        var dem = pipeline.GetDataProduct<TiffDataProduct>(project, rec.DEMGuid).Image;
                        var mask = pipeline.GetDataProduct<PngDataProduct>(project, rec.MaskGuid).Image;
                        bev.UnionMask(mask, new float[] { 1 });
                        dem.UnionMask(mask, new float[] { 1 });
                        bevs[siteDrive] = bev;
                        dems[siteDrive] = dem;
                        bevOrigins[siteDrive] = new Vector2(rec.OriginX, rec.OriginY);
                    }
                });
            pipeline.LogInfo("loaded {0} birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
            return bevs.Count == siteDrives.Length;
        }

        /// <summary>
        /// save bevs, dems, and associated metadata to database
        /// </summary>
        private void SaveBEVs()
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("saving {0} birds eye views...", bevs.Count);
            CoreLimitedParallel.ForEach(bevs, pair => {
                    var siteDrive = pair.Key;
                    var bev = pair.Value;
                    var dem = dems[siteDrive];
                    var mask = bev.MaskToImage();
                    var origin = bevOrigins[siteDrive];
                    BirdsEyeView.Create(pipeline, project, siteDrive, bev, dem, mask, origin, options.BEVColoring,
                                        options.BEVBlending, options.BEVMetersPerPixel, options.BEVSparseBlocksize,
                                        options.BEVMinValidBlockRatio, options.BEVInpaint, options.BEVSmoothing,
                                        options.BEVDecimation);
                });
            pipeline.LogInfo("saved {0} birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// apply optional image processing (e.g. contrast stretching, thresholding) to BEVs
        /// </summary>
        private void PostProcessBEVs(out double min, out double max)
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("post processing {0} birds eye views...", bevs.Count);

            int n = 0;
            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            double mean = 0;
            double stddev = 0;
            if (options.StretchContrast || options.BEVColoring == BirdsEyeViewing.ColorMode.Elevation)
            {
                CollectBEVStats(out n, out min, out max, out mean, out stddev);
            }

            if (options.StretchContrast)
            {
                double lower = Math.Max(mean - stddev * options.StretchStdDevs, min);
                double upper = Math.Min(mean + stddev * options.StretchStdDevs, max);
                pipeline.LogInfo("stretching [{0}, {1}] -> [0, 1] ({2} stddev)", lower, upper, options.StretchStdDevs);
                foreach (var bev in bevs.Values)
                {
                    bev.ScaleValues((float)lower, (float)upper, 0, 1);
                }
            }

            if (options.BEVThreshold > 0)
            {
                pipeline.LogInfo("thresholding to {0}", options.BEVThreshold);
                foreach (var bev in bevs.Values)
                {
                    bev.ApplyInPlace(v => v > options.BEVThreshold ? 1 : 0);
                }
            }

            pipeline.LogInfo("post processed {0} birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// collect combined stats across all BEVs  
        /// n - total number of valid pixels
        /// min, max, mean, stddev - stats for valid pixel values
        /// </summary>
        private void CollectBEVStats(out int n, out double min, out double max, out double mean, out double stddev)
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("collecting combined stats for {0} birds eye views...", bevs.Count);

            n = 0;
            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            mean = 0;
            foreach (var bev in bevs.Values)
            {
                foreach (ImageCoordinate ic in bev.Coordinates(includeInvalidValues: false))
                {
                    var v = bev[0, ic.Row, ic.Col];
                    min = Math.Min(min, v);
                    max = Math.Max(max, v);
                    mean += v;
                    n++;
                }
            }
            mean /= n;
            
            double variance = 0;
            foreach (var bev in bevs.Values)
            {
                foreach (ImageCoordinate ic in bev.Coordinates(includeInvalidValues: false))
                {
                    var d = bev[0, ic.Row, ic.Col] - mean;
                    variance += d * d;
                }
            }
            variance /= n;
            stddev = Math.Sqrt(variance);

            pipeline.LogInfo("{0} valid pixels, min {1:F3}, max {2:F3}, mean {3:F3}, stddev {4:F3}",
                             n, min, max, mean, stddev);
            pipeline.LogInfo("collected stats for {0} birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// populate features from database or bevs  
        /// </summary>
        private void LoadOrDetectFeatures()
        {
            if (options.RedoFeatures || !LoadFeatures())
            {
                DetectFeatures();
                SaveFeatures();
            }

            if (options.WriteDebug)
            {
                PathHelper.EnsureExists(outputPath);
                double startSec = UTCTime.Now();
                int np = 0, nc = 0;
                CoreLimitedParallel.ForEach(siteDrives, siteDrive => {
                        Interlocked.Increment(ref np);
                        if (!options.NoProgress)
                        {
                            pipeline.LogInfo("saving {0} birds eye view feature images in parallel, completed {1}/{2}",
                                             np, nc, siteDrives.Length);
                        }
                        var bev = bevs[siteDrive];
                        var mask = bev.MaskToImage(valid: 1, invalid: 0);
                        var feat = features[siteDrive];
                        var img = FeatureDetecting.DrawFeaturesEmgu(bev, mask, feat, siteDrive, stretch: false);
                        foreach (var otherSiteDrive in siteDrives)
                        {
                            var pixel = PointToPixel(Vector3.Zero, otherSiteDrive, siteDrive);
                            var color = new Vector3(otherSiteDrive != siteDrive ? 0 : 1,
                                                    otherSiteDrive != siteDrive ? 1 : 0,
                                                    0);
                            DrawOrigin(img, pixel, color);
                        }
                        img.ToOPSImage().Save<byte>(outputPath + siteDrive + "_BirdsEyeView_Features" + imageExt);
                        Interlocked.Decrement(ref np);
                        Interlocked.Increment(ref nc);
                    });
                pipeline.LogInfo("saved {0} birds eye view feature images ({1:F3}s)", siteDrives.Length,
                                 UTCTime.Now() - startSec);
            }
        }

        /// <summary>
        /// draw a cross and/or a circle at a given pixel
        /// </summary>
        private void DrawOrigin(Image<Bgr, byte> img, Vector2 pixel, Vector3 color,
                                double crossRadius = 0.05, double circleRadius = 0.5)
        {
            var bgr = new Bgr((float)color.X * 255, (float)color.Y * 255, (float)color.Z * 255); //actually RGB
            if (crossRadius > 0)
            {
                var cr = crossRadius * PixelsPerMeter;
                img.Draw(ToLineSegment2DF(pixel + new Vector2(-cr, 0), pixel + new Vector2(cr, 0)), bgr, 2);
                img.Draw(ToLineSegment2DF(pixel + new Vector2(0, -cr), pixel + new Vector2(0, cr)), bgr, 2);
            }
            if (circleRadius > 0)
            {
                var cr = circleRadius * PixelsPerMeter;
                img.Draw(new CircleF(ToPointF(pixel), (float)cr), bgr, 2);
            }
        }

        /// <summary>
        /// detect features that were not loaded from database
        /// </summary>
        private void DetectFeatures()
        {
            double startSec = UTCTime.Now();
            var featuresNeeded = siteDrives.Where(sd => !features.ContainsKey(sd));
            pipeline.LogInfo("detecting {0} features in {1} birds eye views...", options.DetectorType,
                             featuresNeeded.Count());

            var detectorOpts = new FeatureDetector.Options()
                {
                    DetectorType = options.DetectorType,
                    MinResponse = options.MinFeatureResponse,
                    MaxFeatures = options.MaxFeaturesPerImage,
                    ExtraInvalidRadius = options.FeatureExtraInvalidRadius,
                    FASTThreshold = options.FASTThreshold,
                    FeaturesPerImageBucketSize = 1000,
                    FeaturesPerSizeBucketSize = 5,
                    FeaturesPerResponseBucketSize = 10,
                };
            FeatureDetector detector = new FeatureDetector(pipeline, masker, detectorOpts);

            int nc = 0, np = 0;
            CoreLimitedParallel.ForEach(featuresNeeded, siteDrive => {

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("detecting features for {0} site drives in parallel, completed {1}/{2}",
                                         np, nc, siteDrives.Length);
                    }

                    var origin = PointToPixel(Vector3.Zero, siteDrive, siteDrive);

                    FeatureDetector.FeatureSortKey sortByDistance =
                    (SIFTFeature f) => Vector2.DistanceSquared(f.Location, origin);

                    var bev = bevs[siteDrive];
                    var mask = bev.MaskToImage(valid: 1, invalid: 0);

                    var feat = features[siteDrive] = detector.Detect(bev, mask, sortByDistance);

                    pipeline.LogVerbose("detected {0} {1} features in {2}x{3} birds eye view for {4}, " +
                                        "max features {5}, extra invalid radius {6}, FAST threshold {7}",
                                        feat.Length, options.DetectorType, bev.Width, bev.Height, siteDrive,
                                        options.MaxFeaturesPerImage, options.FeatureExtraInvalidRadius,
                                        options.FASTThreshold);

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            if (options.Verbose)
            {
                detector.DumpHistograms(pipeline);
            }

            pipeline.LogInfo("detected features for {0} birds eye views ({1:F3}s)",
                             features.Count, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// populate features from database
        /// returns true iff all were loaded successfully
        /// </summary>
        private bool LoadFeatures()
        {
            double startSec = UTCTime.Now();
            CoreLimitedParallel.ForEach(siteDrives, siteDrive => {
                    var rec = BirdsEyeViewFeatures.Find(pipeline, project.Name, siteDrive);
                    if (rec != null &&
                        rec.DetectorType == options.DetectorType &&
                        rec.MinFeatureResponse == options.MinFeatureResponse &&
                        rec.MaxFeatures == options.MaxFeaturesPerImage &&
                        rec.ExtraInvalidRadius == options.FeatureExtraInvalidRadius &&
                        rec.FASTThreshold == options.FASTThreshold)
                    {
                        features[siteDrive] =
                            pipeline.GetDataProduct<FeaturesDataProduct>(project, rec.FeaturesGuid).Features;
                    }
                });
            pipeline.LogInfo("loaded {0} birds eye view features ({1:F3}s)", features.Count, UTCTime.Now() - startSec);
            return features.Count == siteDrives.Length;
        }

        /// <summary>
        /// save features and associated metadata to database
        /// </summary>
        private void SaveFeatures()
        {
            double startSec = UTCTime.Now();
            CoreLimitedParallel.ForEach(features, pair => {
                    var siteDrive = pair.Key;
                    var features = pair.Value;
                    BirdsEyeViewFeatures.Create(pipeline, project, siteDrive, features, options.DetectorType,
                                                options.MinFeatureResponse, options.MaxFeaturesPerImage,
                                                options.FeatureExtraInvalidRadius, options.FASTThreshold);
                });
            pipeline.LogInfo("saved {0} birds eye view features ({1:F3}s)", features.Count, UTCTime.Now() - startSec);
        }
        
        /// <summary>
        /// populates matches[modelSiteDrive-dataSiteDrive] from features
        /// assumes features[siteDrive] are sorted by increasing distance to origin of siteDrive
        /// </summary>
        private int MatchFeatures(string modelSiteDrive, string dataSiteDrive)
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("matching features in birds eye views for site drives {0} (model) and  {1} (data)...",
                             modelSiteDrive, dataSiteDrive);

            IEnumerable<FeatureMatch> matchPair(string model, string data)
            {
                var modelFeatures = features[model];
                var dataFeatures = features[data];

                //pixel corresponding to origin of model sitedrive in model BEV
                var modelOrigin = PointToPixel(Vector3.Zero, model, model);

                //pixel corresponding to origin of data sitedrive in data BEV
                var dataOrigin = PointToPixel(Vector3.Zero, data, data);

                //pixel corresponding to origin of data sitedrive in model BEV
                var dataOriginInModel = PointToPixel(Vector3.Zero, data, model);
                
                //distance in pixels of model feature to origin of model sitedrive in model BEV
                var modelDistances = modelFeatures.Select(f => Vector2.Distance(f.Location, modelOrigin)).ToArray();

                //NOTE: features for a site drive are already sorted by distance to origin of that site drive
                
                double radius = options.MatchRadius * PixelsPerMeter;
                
                for (int i = 0; i < dataFeatures.Length; i++)
                {
                    var df = dataFeatures[i];
                    var dfInModel = dataOriginInModel + (df.Location - dataOrigin);
                    var r = Vector2.Distance(dfInModel, modelOrigin);
                    int minSearchIndex = BinarySearch(modelDistances, r - radius);
                    int maxSearchIndex = BinarySearch(modelDistances, r + radius) - 1;
                    if (maxSearchIndex >= minSearchIndex)
                    {
                        var match =
                            BruteForceMatcher.FindBestModelFeatureForDataFeature
                            (modelFeatures, dataFeatures, i,
                             options.MaxDescriptorDistanceRatio,
                             mf => Vector2.Distance(mf.Location, dfInModel) <= radius,
                             minSearchIndex, maxSearchIndex);
                        if (match != null && match.DescriptorDistance <= options.MaxDescriptorDistance)
                        {
                            yield return match;
                        }
                    }
                }
            }

            var best = new Dictionary<FeatureMatch, double>();
            int d2m = 0, m2d = 0;

            foreach (var match in matchPair(modelSiteDrive, dataSiteDrive))
            {
                d2m++;
                best[match] = match.DescriptorDistance;
            }

            if (!options.NoBidirectionalMatching)
            {
                foreach (var match in matchPair(dataSiteDrive, modelSiteDrive))
                {
                    var tmp = match.ModelIndex;
                    match.ModelIndex = match.DataIndex;
                    match.DataIndex = tmp;
                    if (!best.ContainsKey(match))
                    {
                        best[match] = match.DescriptorDistance;
                        m2d++;
                    }
                    else if (best[match] > match.DescriptorDistance)
                    {
                        best[match] = match.DescriptorDistance;
                        d2m--;
                        m2d++;
                    }
                }
            }
                
            var pair = modelSiteDrive + "-" + dataSiteDrive;

            var matchArray = matches[pair] = best.Keys.OrderBy(m => m.DescriptorDistance).ToArray();

            if (options.Verbose)
            {
                var histogram = new Histogram(50, pair + " matches", "distance");
                foreach (var match in matchArray)
                {
                    histogram.Add(match.DescriptorDistance);
                }
                histogram.Dump(pipeline);
            }

            int nm = matchArray.Length;
            pipeline.LogInfo("{0} feature matches for site drives {1} (model) and {2} (data) ({3} d2m, {4} m2d) " +
                             "({5:F3}s)", nm, modelSiteDrive, dataSiteDrive, d2m, m2d, UTCTime.Now() - startSec);
            return nm;
        }

        /// <summary>
        /// populates ransacMatches[modelSiteDrive-dataSiteDrive] from corresponding matches and features
        /// </summary>
        private int RansacMatches(string modelSiteDrive, string dataSiteDrive)
        {
            var pair = modelSiteDrive + "-" + dataSiteDrive;
            var matchArray = matches[pair];
            var nm = matchArray.Length;

            double startSec = UTCTime.Now();
            pipeline.LogInfo("RANSACing {0} feature matches for site drives {1} (model) and  {2} (data)...",
                             nm, modelSiteDrive, dataSiteDrive);

            var modelFeatures = features[modelSiteDrive];
            var dataFeatures = features[dataSiteDrive];

            //pixel corresponding to origin of model sitedrive in model BEV
            var modelOrigin = PointToPixel(Vector3.Zero, modelSiteDrive, modelSiteDrive);

            //pixel corresponding to origin of data sitedrive in data BEV
            var dataOrigin = PointToPixel(Vector3.Zero, dataSiteDrive, dataSiteDrive);

            //pixel corresponding to origin of data sitedrive in model BEV
            var dataOriginInModel = PointToPixel(Vector3.Zero, dataSiteDrive, modelSiteDrive);

            //pixel offsets corresponding to model features relative to data sitedrive origin in model BEV
            var modelPts = matchArray
                .Select(m => modelFeatures[m.ModelIndex].Location - dataOriginInModel)
                .ToArray();

            //pixel offsets corresponding to data features relative to data sitedrive origin in model BEV
            var dataPtsInModel = matchArray
                .Select(m => dataFeatures[m.DataIndex].Location - dataOrigin)
                .ToArray();

            var bestTransform = new RigidTransform2D();
            var bestMatches = new List<int>(nm);
            var tmpMatches = new List<int>(nm);
            double bestResidual = double.PositiveInfinity;

            double radius = options.RansacMatchRadius * PixelsPerMeter;
            double radiusSquared = radius * radius;

            double minSep = options.MinRansacSeparation * PixelsPerMeter;
            double minSepSquared = minSep * minSep;

            var maxResidual = options.MaxRansacResidual * PixelsPerMeter;

            var random = NumberHelper.MakeRandomGenerator();
            int[,] shuffle = null;
            HashSet<Tuple<int, int>> alreadyTried = null;
            int maxTests = 0;
            long totalCombinations = ((long)nm) * (((long)nm) - 1) / 2; //nm choose 2
            if (totalCombinations < 2 * (long)(options.MaxRansacTests))
            {
                pipeline.LogVerbose("generating random shuffle of {0} feature pairs for {1}", totalCombinations, pair);

                //the total number of combinations is tractable
                //so enumerate all combinations, randomly shuffle, take at most MaxRansacTests of them
                shuffle = new int[(int)totalCombinations, 2]; 
                int n = 0;
                for (int i = 0; i < nm; i++)
                {
                    for (int j = i + 1; j < nm; j++)
                    {
                        shuffle[n, 0] = i;
                        shuffle[n, 1] = j;
                        n++;
                    }
                }

                //Fisher-Yates shuffle
                void swap(int i, int j, int k)
                {
                    var t = shuffle[i, k];
                    shuffle[i, k] = shuffle[j, k];
                    shuffle[j, k] = t;
                }
                for (int i = 0; i < (int)totalCombinations - 1; i++)
                {
                    int j = random.Next(i, (int)totalCombinations);
                    swap(i, j, 0);
                    swap(i, j, 1);
                }

                maxTests = (int)Math.Min(totalCombinations, options.MaxRansacTests);
            }
            else
            {
                pipeline.LogVerbose("random shuffle of {0} feature pairs for {1} too big, using probabilistic sampling",
                                    totalCombinations, pair);
                //if the total number of combinations is more than twice MaxRansacTests then
                //avoid allocating shuffle which could be gigantic
                //in this case we instead throw dice to generate combinations
                //but keep track of the ones we've already tried and re-throw if we get a dupe
                //since we'll be trying at most half of the total possible combinations
                //we should't spend too much time re-throwing
                alreadyTried = new HashSet<Tuple<int, int>>();
                maxTests = options.MaxRansacTests;
            }

            pipeline.LogInfo("RANSACing {0} match pairs for {1}", maxTests, pair);
            int nt;
            int maxMatches = 0;
            for (nt = 0; nt < maxTests; nt++)
            {
                Tuple<int, int> seeds = null;
                if (shuffle != null)
                {
                    seeds = new Tuple<int, int>(shuffle[nt, 0], shuffle[nt, 1]);
                }
                else
                {
                    do
                    {
                        int j = random.Next(0, nm);
                        int k = random.Next(0, nm);
                        seeds = new Tuple<int, int>(Math.Min(j, k), Math.Max(j, k)); //canonical order Item1 < item2
                    }
                    while (seeds.Item1 == seeds.Item2 || alreadyTried.Contains(seeds));
                    alreadyTried.Add(seeds);
                }

                if (minSepSquared > 0 &&
                    (Vector2.DistanceSquared(dataPtsInModel[seeds.Item1], dataPtsInModel[seeds.Item2]) < minSepSquared
                     || Vector2.DistanceSquared(modelPts[seeds.Item1], modelPts[seeds.Item2]) < minSepSquared))
                {
                    continue;
                }

                var xform =
                    RigidTransform2D.Estimate(new [] { dataPtsInModel[seeds.Item1], dataPtsInModel[seeds.Item2] },
                                              new [] { modelPts[seeds.Item1], modelPts[seeds.Item2] },
                                              out double residual);

                if (residual > bestResidual)
                {
                    continue;
                }

                tmpMatches.Clear();
                for (int j = 0; j < nm; j++)
                {
                    var d = Vector2.DistanceSquared(xform.Transform(dataPtsInModel[j]), modelPts[j]);
                    if (d < radiusSquared)
                    {
                        bool ok = true;
                        if (minSepSquared > 0)
                        {
                            foreach (var k in tmpMatches)
                            {
                                if (Vector2.DistanceSquared(dataPtsInModel[j], dataPtsInModel[k]) < minSepSquared ||
                                    Vector2.DistanceSquared(modelPts[j], modelPts[k]) < minSepSquared)
                                {
                                    ok = false;
                                    break;
                                }
                            }
                        }
                        if (ok)
                        {
                            tmpMatches.Add(j);
                        }
                    }
                    if (tmpMatches.Count >= options.MaxRansacMatches)
                    {
                        break;
                    }
                }

                maxMatches = Math.Max(maxMatches, tmpMatches.Count);

                if (tmpMatches.Count < options.MinRansacMatches)
                {
                    continue;
                }

                xform = RigidTransform2D.Estimate(tmpMatches.Select(j => dataPtsInModel[j]).ToArray(),
                                                  tmpMatches.Select(j => modelPts[j]).ToArray(),
                                                  out residual);

                //if (residual < bestResidual)
                if (tmpMatches.Count() > bestMatches.Count())
                {
                    bestResidual = residual;
                    bestTransform = xform;
                    bestMatches.Clear();
                    bestMatches.AddRange(tmpMatches);
                }

                if (bestResidual < maxResidual)
                {
                    break;
                }
            }

            if (options.WriteDebug)
            {
                var mf = bestMatches
                    .Select(m => modelFeatures[matchArray[m].ModelIndex])
                    .Cast<SIFTFeature>()
                    .CastToMKeyPoint()
                    .ToArray();
                
                void writeImage(string suffix, Func<Vector2, Vector2> dataPointTransform)
                {
                    var df = bestMatches
                        .Select(m =>
                                {
                                    var f = new SIFTFeature((SIFTFeature)(dataFeatures[matchArray[m].DataIndex]));
                                    f.Location = dataPointTransform(dataPtsInModel[m]) + dataOriginInModel;
                                    return f;
                                })
                        .CastToMKeyPoint()
                        .ToArray();
                    
                    var img = bevs[modelSiteDrive].ToEmgu<Bgr>();
                    
                    Features2DToolbox.DrawKeypoints(img, new VectorOfKeyPoint(mf), img, new Bgr(255, 0, 0), //RGB
                                                    Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
                    
                    Features2DToolbox.DrawKeypoints(img, new VectorOfKeyPoint(df), img, new Bgr(0, 255, 0), //RGB
                                                    Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
                    
                    img.ToOPSImage().Save<byte>(outputPath + pair + "_BirdsEyeView_RANSAC" + suffix + imageExt);
                }
                
                PathHelper.EnsureExists(outputPath);

                writeImage("_0_priors", pt => pt);
                writeImage("_1_rotation", pt => bestTransform.Rotate(pt));
                writeImage("_2_solved", pt => bestTransform.Transform(pt));
            }
                        
            ransacMatches[pair] = bestMatches.Select(m => matchArray[m]).ToArray();

            nm = bestMatches.Count;
            var msg =
                nm > 0 ? string.Format(", best transform ({0:F3}m, {1:F3}m, {2:F3}deg), residual {3:F3}m, {4} matches",
                                       bestTransform.Translation.X * MetersPerPixel,
                                       bestTransform.Translation.Y * MetersPerPixel,
                                       MathHelper.ToDegrees(bestTransform.Rotation),
                                       bestResidual * MetersPerPixel, nm)
                : "";
            pipeline.LogInfo("performed {0}/{1} ransac tests for {2} ({3} combinations), max matches {4}{5} ({6:F3}s)",
                             nt, maxTests, pair, totalCombinations, maxMatches, msg, UTCTime.Now() - startSec);
            return nm;
        }

        /// <summary>
        /// compute spatialMatches from ransacMatches, features, and dems
        /// </summary>
        private int SpatializeMatches(string modelSiteDrive, string dataSiteDrive)
        {
            var modelFeatures = features[modelSiteDrive];
            var dataFeatures = features[dataSiteDrive];

            //pixel corresponding to world origin in model BEV
            var modelOrigin = bevOrigins[modelSiteDrive];

            //pixel corresponding to world origin in data BEV
            var dataOrigin = bevOrigins[dataSiteDrive];

            var modelDEM = dems[modelSiteDrive];
            var dataDEM = dems[dataSiteDrive];

            var pair = modelSiteDrive + "-" + dataSiteDrive;

            var pairs = new List<SpatialMatch>();
            var lengths = new List<double>();
            foreach (var match in ransacMatches[pair])
            {
                var mf = modelFeatures[match.ModelIndex];
                var df = dataFeatures[match.DataIndex];

                var mxy = (mf.Location - modelOrigin) * MetersPerPixel;
                var mz = modelDEM[0, (int)mf.Location.Y, (int)mf.Location.X];

                var dxy = (df.Location - dataOrigin) * MetersPerPixel;
                var dz = dataDEM[0, (int)df.Location.Y, (int)df.Location.X];

                var mp = new Vector3(mxy.X, mxy.Y, mz);
                var dp = new Vector3(dxy.X, dxy.Y, dz);
                lengths.Add(Vector3.Distance(mp, dp));
                pairs.Add(new SpatialMatch(mp, dp));
            }

            //the XY components of the matches should already be pretty robust due to the ransac
            //but now that they have Z components those can be dirty
            int n = lengths.Count();
            if (n > 1)
            {
                lengths.Sort();
                double median = lengths[n/2];
                for (int i = 0; i < n; i++)
                {
                    lengths[i] = Math.Abs(lengths[i] - median);
                }
                lengths.Sort();
                var mad = lengths[n/2]; //median absolute deviation
                
                double threshold = options.SpatialOutlierMADs * mad;
                pairs = pairs
                    .Where(pr => Math.Abs(Vector3.Distance(pr.ModelPoint, pr.DataPoint) - median) < threshold)
                    .ToList();
                int nn = pairs.Count();
                if (nn < n)
                {
                    pipeline.LogInfo("{0} outlier spatial matches for {1}, median {2:F3}, threshold {3:F3} ({4} MAD)",
                                     n - nn, pair, median, threshold, options.SpatialOutlierMADs);
                }
                n = nn;
            }
                
            spatialMatches[pair] = pairs.ToArray();

            return n;
        }

        /// <summary>
        /// compute siteDrivePairs = (modelSiteDrive, dataSiteDrive), (modelSiteDrive, dataSiteDrive), ...
        /// </summary>
        private void ComputePairs()
        {
            switch (options.SiteDrivePriority)
            {
                case SiteDrivePriority.NewestFirst:
                {
                    siteDrives = siteDrives.OrderByDescending(sd => sd).ToArray();
                    break;
                }
                case SiteDrivePriority.OldestFirst:
                {
                    siteDrives = siteDrives.OrderBy(sd => sd).ToArray();
                    break;
                }
                case SiteDrivePriority.BiggestFirst:
                {
                    siteDrives = siteDrives.OrderByDescending(sd => BEVArea(sd)).ToArray();
                    break;
                }
                case SiteDrivePriority.SmallestFirst:
                {
                    siteDrives = siteDrives.OrderBy(sd => BEVArea(sd)).ToArray();
                    break;
                }
            }

            pipeline.LogInfo("site drives ordered by {0}: {1}",
                             options.SiteDrivePriority, string.Join(", ", siteDrives));

            for (int i = 0; i < siteDrives.Length; i++)
            {
                for (int j = i + 1; j < siteDrives.Length; j++)
                {
                    siteDrivePairs.Add(new Tuple<string, string>(siteDrives[i], siteDrives[j]));
                }
            }

            pipeline.LogInfo("{0} site drive pairs", siteDrivePairs.Count);
        }

        /// <summary>
        /// populates matches, ransacMatches, and spatialMatches from database or siteDrivePairs and features
        /// </summary>
        private int LoadOrMatchPairs()
        {
            if (options.RedoMatches || !LoadMatches())
            {
                MatchPairs();
                SaveMatches();
            }

            int ng = 0;
            foreach (var entry in spatialMatches)
            {
                var name = entry.Key;
                var num = entry.Value.Length;
                if (num > 0)
                {
                    pipeline.LogInfo("{0}: {1} matches", name, num);
                }
                if (num >= options.MinRansacMatches)
                {
                    ng++;
                }
            }
            pipeline.LogInfo("{0} site drive pairs with at least {1} matches", ng, options.MinRansacMatches);

            if (options.WriteDebug)
            {
                PathHelper.EnsureExists(outputPath);
                double startSec = UTCTime.Now();
                int np = 0, nc = 0;
                CoreLimitedParallel.ForEach(siteDrivePairs, pair => {
                        
                        Interlocked.Increment(ref np);
                        if (!options.NoProgress)
                        {
                            pipeline.LogInfo("saving {0} birds eye match images/meshes in parallel, completed {1}/{2}",
                                             np, nc, siteDrivePairs.Count);
                        }

                        var model = pair.Item1;
                        var data = pair.Item2;
                        var pairName = model + "-" + data;

                        if (matches[pairName].Length > 0)
                        {
                            ImageMatching
                                .DrawMatches(bevs[model], bevs[data], features[model], features[data],
                                             matches[pairName]
                                             .Select(m => new KeyValuePair<int, int>(m.DataIndex, m.ModelIndex))
                                             .ToArray(),
                                             model, data, stretch: false)
                                .Save<byte>(outputPath + pairName + "_BirdsEyeView_Matches" + imageExt);
                        }

                        if (ransacMatches[pairName].Length > 0)
                        {
                            ImageMatching
                            .DrawMatches(bevs[model], bevs[data], features[model], features[data],
                                         ransacMatches[pairName]
                                         .Select(m => new KeyValuePair<int, int>(m.DataIndex, m.ModelIndex))
                                         .ToArray(),
                                         model, data, stretch: false)
                            .Save<byte>(outputPath + pairName + "_BirdsEyeView_RANSAC_Matches" + imageExt);
                        }

                        if (spatialMatches[pairName].Length > 0)
                        {
                            ImageMatching
                            .MakeMatchMesh(spatialMatches[pairName].Select(p => p.ModelPoint).ToArray(),
                                           spatialMatches[pairName].Select(p => p.DataPoint).ToArray())
                            .Save(outputPath + pairName + "_matches" + meshExt);
                        }

                        Interlocked.Decrement(ref np);
                        Interlocked.Increment(ref nc);
                    });
                pipeline.LogInfo("saved {0} birds eye view match image/meshes ({1:F3}s)", siteDrivePairs.Count,
                                 UTCTime.Now() - startSec);
            }

            var good = new HashSet<string>();
            foreach (var pair in siteDrivePairs)
            {
                var model = pair.Item1;
                var data = pair.Item2;
                var pairName = model + "-" + data;
                if (spatialMatches.ContainsKey(pairName) && spatialMatches[pairName].Length >= options.MinRansacMatches)
                {
                    good.Add(model);
                    good.Add(data);
                }
            }

            return good.Count;
        }

        /// <summary>
        /// compute matches, ransacMatches, and spatialMatches from siteDrivePairs and features  
        /// </summary>
        private void MatchPairs()
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("matching features in birds eye views for {0} site drive pairs...", siteDrivePairs.Count);

            var histogram = new Histogram(10, "pairs", "matches");
            int nc = 0, np = 0;
            CoreLimitedParallel.ForEach(siteDrivePairs, pair => {
                    
                    Interlocked.Increment(ref np);
                    
                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("matching {0} sitedrive pairs in parallel, completed {1}/{2}",
                                         np, nc, siteDrivePairs.Count);
                    }

                    var model = pair.Item1;
                    var data = pair.Item2;
                    var pairName = model + "-" + data;

                    //features -> matches
                    int nm = matches.ContainsKey(pairName) ? matches[pairName].Length : MatchFeatures(model, data);

                    if (nm > options.MinRansacMatches)
                    {
                        //matches -> ransacMatches
                        nm = ransacMatches.ContainsKey(pairName) ?
                            ransacMatches[pairName].Length : RansacMatches(model, data);

                        if (nm > 0)
                        {
                            //ransacMatches -> spatialMatches
                            nm = spatialMatches.ContainsKey(pairName) ?
                                spatialMatches[pairName].Length : SpatializeMatches(model, data);
                        }
                        else
                        {
                            spatialMatches[pairName] = new SpatialMatch[] {};
                        }
                    }
                    else
                    {
                        ransacMatches[pairName] = new FeatureMatch[] {};
                        spatialMatches[pairName] = new SpatialMatch[] {};
                    }

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            if (options.Verbose)
            {
                histogram.Dump(pipeline);
            }

            pipeline.LogInfo("matched features in birds eye views for {0} site drive pairs ({1:F3}s)",
                             siteDrivePairs.Count, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// populate matches, ransacMatches, and spatialMatches from database
        /// returns true iff all were loaded successfully
        /// </summary>
        private bool LoadMatches()
        {
            double startSec = UTCTime.Now();
            CoreLimitedParallel.ForEach(siteDrivePairs, pair => {
                    var model = pair.Item1;
                    var data = pair.Item2;
                    var pairName = model + "-" + data;
                    var fm = FeatureMatches.Find(pipeline, project.Name, pairName);
                    if (fm != null)
                    {
                        matches[pairName] =
                            pipeline.GetDataProduct<FeatureMatchesDataProduct>(project, fm.MatchesGuid).Matches;
                        var rm = FeatureMatches.Find(pipeline, project.Name, pairName + "_RANSAC");
                        if (rm != null)
                        {
                            ransacMatches[pairName] =
                                pipeline.GetDataProduct<FeatureMatchesDataProduct>(project, rm.MatchesGuid).Matches;
                            var sm = SpatialMatches.Find(pipeline, project.Name, pairName);
                            if (sm != null)
                            {
                                spatialMatches[pairName] =
                                    pipeline.GetDataProduct<SpatialMatchesDataProduct>(project, sm.MatchesGuid).Matches;
                            }
                        }
                    } 
                });
            pipeline.LogInfo("loaded {0} site drive feature matches ({1:F3}s)", spatialMatches.Count,
                             UTCTime.Now() - startSec);
            return spatialMatches.Count == siteDrivePairs.Count;
        }

        /// <summary>
        /// save matches, ransacMatches, and spatialMatches to database
        /// </summary>
        private void SaveMatches()
        {
            double startSec = UTCTime.Now();
            CoreLimitedParallel.ForEach(siteDrivePairs, pair => {
                    var model = pair.Item1;
                    var data = pair.Item2;
                    var pairName = model + "-" + data;
                    FeatureMatches.Create(pipeline, project, pairName, model, data, matches[pairName]);
                    FeatureMatches.Create(pipeline, project, pairName + "_RANSAC", model, data, ransacMatches[pairName]);
                    SpatialMatches.Create(pipeline, project, pairName, model, data, spatialMatches[pairName]);
                });
            pipeline.LogInfo("saved {0} site drive feature matches ({1:F3}s)", matches.Count, UTCTime.Now() - startSec);
        }

        private class Node
        {
            public string Name;
            public Node Parent;
            public List<Node> Children = new List<Node>();
            public int Depth; //length of path along ancestor chain to world
            public Matrix Transform; //to parent
            public Matrix? WorldTransform; //to world
            public Node(string name)
            {
                this.Name = name;
            }
        }
        private List<Node> nodes = new List<Node>();
        private Dictionary<string, Node> siteDriveToNode = new Dictionary<string, Node>();
        private HashSet<string> fixedNodes = new HashSet<string>();

        /// <summary>
        /// build graph of sitedrive nodes  
        /// for each pair of sitedrives for which we have a sufficient spatial match
        /// the "data" sitedrive is a child of the "model" sitedrive
        /// at this stage the graph is a DAG because a node can be a child of more than one parent
        /// the graph is also possibly disconnected (i.e. there can be more than one node with no parent)
        /// </summary>
        private void MakeGraph()
        {
            foreach (var sd in siteDrives)
            {
                var node = new Node(sd);
                nodes.Add(node);
                siteDriveToNode[sd] = node;
            }

            var fx = StringHelper.ParseList(options.FixSiteDrives);

            var specials = new Dictionary<string, string>();
            specials["newest"] = siteDrives.OrderByDescending(sd => sd).FirstOrDefault();
            specials["oldest"] = siteDrives.OrderBy(sd => sd).FirstOrDefault();
            specials["largest"] = siteDrives.OrderByDescending(sd => BEVArea(sd)).FirstOrDefault();
            specials["smallest"] = siteDrives.OrderBy(sd => BEVArea(sd)).FirstOrDefault();

            for (int i = 0; i < fx.Length; i++)
            {
                var sd = fx[i].ToLower();
                if (specials.ContainsKey(sd))
                {
                    fx[i] = specials[sd];
                }
            }

            fixedNodes.UnionWith(fx);

            foreach (var pair in siteDrivePairs)
            {
                var model =  pair.Item1;
                var data =  pair.Item2;
                var key = model + "-" + data;
                if (spatialMatches.ContainsKey(key) && spatialMatches[key].Length >= options.MinRansacMatches)
                {
                    var parent = siteDriveToNode[model];
                    var child = siteDriveToNode[data];
                    parent.Children.Add(child);
                    child.Parent = parent; //for now any parent will do
                }
            }
        }

        /// <summary>
        /// write out sitedrive -> root adjusted transforms
        /// </summary>
        private void SaveTransforms(IEnumerable<Node> aligned, TransformSource transformSource)
        {
            var unaligned = new HashSet<string>(siteDrives);
            foreach (var node in aligned)
            {
                unaligned.Remove(node.Name);
                var ut = new UncertainRigidTransform(node.WorldTransform.Value);
                var frame = frameCache.GetFrame(node.Name);
                var ft = FrameTransform.FindOrCreate(pipeline, frame, transformSource, ut);
                ft.Transform = ut;
                ft.Save(pipeline);
                bool added = false;
                lock (frame.Transforms)
                {
                    added = frame.Transforms.Add(ft.Source);
                }
                if (added)
                {
                    frame.Save(pipeline);
                }
                pipeline.LogInfo("saved {0} adjusted transform for site drive {1}", transformSource, node.Name);
            }
            foreach (var sd in unaligned)
            {
                var frame = frameCache.GetFrame(sd);
                bool removed = false;
                lock (frame.Transforms)
                {
                    removed = frame.Transforms.Remove(transformSource);
                }
                if (removed)
                {
                    frame.Save(pipeline);
                }
                //can't use frameCache here because it was loaded with only priors
                //but that's OK because FrameTransform.Find() doesn't scan
                var ft = FrameTransform.Find(pipeline, frame, transformSource);
                if (ft != null)
                {
                    ft.Delete(pipeline);
                }
            }
        }

        private void SaveCalves(IEnumerable<Node> aligned)
        {
            if (options.CalfMode == CalfMode.None)
            {
                return;
            }

            var calfNames = new HashSet<string>(siteDrives);
            foreach (var node in aligned)
            {
                calfNames.Remove(node.Name);
                calfNames.Remove(node.Parent.Name);
            }

            var calves = calfNames.Select(name => siteDriveToNode[name]);

            switch (options.CalfMode)
            {
                case CalfMode.Centroid:
                    {
                        var centroid = new Dictionary<string, Vector2>();
                        foreach (var sd in siteDrives)
                        {
                            var c = new Vector2(bevs[sd].Width, bevs[sd].Height) * 0.5;
                            centroid[sd] = c - bevOrigins[sd];
                        }
                        foreach (var calf in calves)
                        {
                            double closestDistSq = double.PositiveInfinity;
                            Node closestParent = null;
                            foreach (var node in aligned)
                            {
                                var d2 = Vector2.DistanceSquared(centroid[calf.Name], centroid[node.Name]);
                                if (d2 < closestDistSq)
                                {
                                    closestDistSq = d2;
                                    closestParent = node;
                                }
                            }
                            calf.Parent = closestParent;
                        }
                        break;
                    }

                case CalfMode.Temporal:
                    {
                        foreach (var calf in calves)
                        {
                            int closestDist = int.MaxValue;
                            Node closestParent = null;
                            foreach (var node in aligned)
                            {
                                var d = Math.Abs((int)(new SiteDrive(calf.Name)) - (int)(new SiteDrive(node.Name)));
                                if (d < closestDist)
                                {
                                    closestDist = d;
                                    closestParent = node;
                                }
                            }
                            calf.Parent = closestParent;
                        }
                        break;
                    }
            }

            foreach (var calf in calves)
            {
                if (calf.Parent != null)
                {
                    var calfToWorldPrior = SiteDriveTransform(calf.Name);
                    var parentToWorldPrior = SiteDriveTransform(calf.Parent.Name);
                    //row matrix transforms compose left to right
                    var calfToParent = calfToWorldPrior * Matrix.Invert(parentToWorldPrior);
                    calf.WorldTransform = calfToParent * calf.Parent.WorldTransform.Value;
                    
                }
                else
                {
                    calf.WorldTransform = null;
                }
            }

            pipeline.LogInfo("birds eye view calf mode: {0}", options.CalfMode);
            var calvesFor = new Dictionary<string, List<string>>();
            foreach (var calf in calves)
            {
                if (calf.Parent != null)
                {
                    if (!calvesFor.ContainsKey(calf.Parent.Name))
                    {
                        calvesFor[calf.Parent.Name] = new List<string>();
                    }
                    calvesFor[calf.Parent.Name].Add(calf.Name);
                }
            }

            pipeline.LogInfo("{0} birds eye view calves: {1}",
                             calves.Count(), String.Join(", ", calves.Select(n => n.Name)));

            foreach (var parent in calvesFor.Keys)
            {
                pipeline.LogInfo("{0} calves for site drive {1}: {2}",
                                 calvesFor[parent].Count, parent, String.Join(", ", calvesFor[parent]));
            }

            SaveTransforms(calves.Where(calf => calf.WorldTransform.HasValue), TransformSource.LandformBEVCalf);
        }
                
        /// <summary>
        /// spatialMatches -> LandformBEV aligned FrameTransforms
        /// </summary>
        private int Align()
        {
            switch (options.AlignmentMode)
            {
                case AlignmentMode.Simultaneous: return SimultaneousAlign();
                case AlignmentMode.PairwiseMaximal: return PairwiseAlign(maximal: true);
                case AlignmentMode.PairwiseMinimal: return PairwiseAlign(maximal: false);
            }
            return 0;
        }

        /// <summary>
        /// simultaneous align all sitedrives that have a sufficent number of spatialized ransac feature matches
        /// then compute the adjusted sitedrive -> root transforms and write them back to the database
        /// using TransformSource = LandformBEV
        /// </summary>
        private int SimultaneousAlign()
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("simultaneous aligning...");

            MakeGraph();

            foreach (var node in nodes)
            {
                node.WorldTransform = SiteDriveTransform(node.Name);
            }

            var nodesToAlign = new List<Node>();
            foreach (var node in nodes)
            {
                if ((node.Parent != null || node.Children.Count > 0) && !fixedNodes.Contains(node.Name))
                {
                    nodesToAlign.Add(node);
                }
            }

            //TODO fix at least one node in each connected component
            
            //TODO
            throw new NotImplementedException("simultaneous align not implemented yet");

            //SaveTransforms(nodesToAlign, TransformSource.LandformBEV);
            //SaveTransforms(TODO, TransformSource.LandformBEVRoot);
            //SaveCalves(nodesToAlign);

            //pipeline.LogInfo("simultaneous aligned {0} nodes ({1:F3}s)", nodesToAlign.Count, UTCTime.Now() - startSec);

            //return nodesToAlign.Length;
        }

        /// <summary>
        /// pairwise align all sitedrives that have a sufficent number of spatialized ransac feature matches
        /// then compute the adjusted sitedrive -> root transforms and write them back to the database
        /// using TransformSource = LandformBEV
        /// </summary>
        private int PairwiseAlign(bool maximal)
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("pairwise aligning...");

            MakeGraph();

            //BFS the graph to set the best parent for each node
            //the best parent is the one to follow to get to a root along a best path
            foreach (var node in nodes)
            {
                node.Depth = maximal ? int.MinValue : int.MaxValue;
            }
            foreach (var node in nodes.Where(n => n.Parent == null))
            {
                node.Depth = 0;
                var queue = new Queue<Node>();
                queue.Enqueue(node);
                while (queue.Count > 0)
                {
                    var parent = queue.Dequeue();
                    var depth = parent.Depth + 1;
                    foreach (var child in parent.Children)
                    {
                        if ((maximal && child.Depth < depth) || (!maximal && child.Depth > depth))
                        {
                            child.Parent = parent;
                            child.Depth = depth;
                            queue.Enqueue(child);
                        }
                    }
                }
            }

            var closures = new HashSet<string>();
            foreach (var node in nodes)
            {
                foreach (var child in node.Children)
                {
                    if (child.Parent != node)
                    {
                        closures.Add(node.Name + "-" + child.Name);
                    }
                }
            }
            pipeline.LogInfo("{0} birds eye view loop closures: {1}", closures.Count, String.Join(", ", closures));

            //align every node to its a parent
            //a node has a parent iff we found enough ransac matches from that node to a higher-priority sitedrive
            var nodesToAlign = nodes
                .Where(n => n.Parent != null)
                .Where(n => !fixedNodes.Contains(n.Name))
                .ToList();
            pipeline.LogInfo("pairwise aligning {0} site drives", nodesToAlign.Count);
            int nc = 0, np = 0;
            var aligned = new HashSet<string>();
            CoreLimitedParallel.ForEach(nodesToAlign, node => {

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("pairwise aligning {0} site drives in parallel, completed {1}/{2}",
                                         np, nc, nodesToAlign.Count);
                    }

                    var model = node.Parent.Name;
                    var data = node.Name;
                    
                    var modelToRootPrior = SiteDriveTransform(model);
                    var dataToRootPrior = SiteDriveTransform(data);
                    var rootToModelPrior = Matrix.Invert(modelToRootPrior);
                    
                    //the spatial matches are in root frame, transform them to model prior frame
                    var pairName = model + "-" + data;
                    var sm = spatialMatches[pairName];
                    var modelPts = sm.Select(m => Vector3.Transform(m.ModelPoint, rootToModelPrior)).ToArray();
                    var dataPts = sm.Select(m => Vector3.Transform(m.DataPoint, rootToModelPrior)).ToArray();
                    
                    double priorResidual = 0;
                    for (int i = 0; i < modelPts.Length; i++)
                    {
                        priorResidual += Vector3.DistanceSquared(modelPts[i], dataPts[i]);
                    }
                    priorResidual = Math.Sqrt(priorResidual / modelPts.Length);
                    
                    //compute transform adj that best aligns data points to model points
                    var residual = Procrustes.CalculateRigid(dataPts, modelPts, out Matrix adj);
                    
                    pipeline.LogInfo("aligned {0} ({1} matches), residual {2}->{3}m",
                                     pairName, sm.Length, priorResidual, residual);

                    aligned.Add(pairName);
                    
                    //row matrix transforms compose left to right
                    var dataToModelPrior = dataToRootPrior * rootToModelPrior;
                    
                    //adjusted transform taking points in data frame to points in model frame
                    node.Transform = dataToModelPrior * adj;

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            //compute a world transform for each node (i.e. sitedrive to root transform)
            //for a node with no parent this is just the prior
            //otherwise it's the concatenation of adjusted transforms along ancestor chain from node to world
            foreach (var node in nodes.Where(n => n.Parent == null))
            {
                node.WorldTransform = SiteDriveTransform(node.Name);
            }
            foreach (var node in nodesToAlign)
            {
                var stack = new Stack<Node>();
                for (var n = node; n.WorldTransform == null; n = n.Parent)
                {
                    stack.Push(node);
                }
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    //row matrix transforms compose left to right
                    n.WorldTransform = n.Transform * n.Parent.WorldTransform.Value;
                }
            }

            SaveTransforms(nodesToAlign, TransformSource.LandformBEV);

            var roots = nodesToAlign.Select(n => n.Parent).Where(n => n.Parent == null).Distinct();
            pipeline.LogInfo("{0} birds eye view roots: {1}",
                             roots.Count(), String.Join(", ", roots.Select(node => node.Name)));

            SaveTransforms(roots, TransformSource.LandformBEVRoot);

            SaveCalves(nodesToAlign);

            pipeline.LogInfo("pairwise aligned {0} nodes ({1:F3}s)", nodesToAlign.Count, UTCTime.Now() - startSec);

            return nodesToAlign.Count;
        }
    }
}

