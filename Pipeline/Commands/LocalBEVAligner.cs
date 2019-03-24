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
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public enum ColorMode { Texture, Tilt, Elevation };

    public enum SiteDrivePriority { NewestFirst, OldestFirst, BiggestFirst, SmallestFirst };
    
    public enum AlignmentMode { PairwiseMinimal, PairwiseMaximal, Simultaneous, None };

    [Verb("local-bev-align", HelpText = "birds eye view align locally")]
    public class LocalBEVAlignerOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "Project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Only generate products for specific site drives, comma separated", Default = null)]
        public string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Don't adjust specified site drives (or \"newest\", \"oldest\", \"largest\", \"smallest\"), comma separated", Default = null)]
        public string FixSiteDrives { get; set; }

        [Option(HelpText = "Alignment algorithm: PairwiseMinimal, PairwiseMaximal, Simultaneous, None (match only)", Default = AlignmentMode.PairwiseMaximal)]
        public AlignmentMode AlignmentMode { get; set; }

        [Option(HelpText = "In pairwise alignment modes lower priority site drives will be aligned to higher priority ones (NewestFirst, OldestFirst, BiggestFirst, SmallestFirst)", Default = SiteDrivePriority.NewestFirst)]
        public SiteDrivePriority SiteDrivePriority { get; set; }

        [Option(HelpText = "Only generate products for specific cameras, comma separated (FrontHazcamLeft, FrontHazcamRight, RearHazcamLeft, RearHazcamRight, NavcamLeft, NavcamRight, MastcamLeft, MastcamRight, MAHLI)", Default = "NavcamLeft")]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Wedge mesh decimation blocksize", Default = 4)]
        public int DecimateWedgeMeshes { get; set; }

        [Option(HelpText = "Wedge image decimation blocksize", Default = 2)]
        public int DecimateWedgeImages { get; set; }

        [Option(HelpText = "Max triangle aspect ratio for organized mesh reconstruction", Default = 20)]
        public double MaxTriangleAspect { get; set; }

        [Option(HelpText = "Birds eye view meters per pixel", Default = 0.005)]
        public double BEVMetersPerPixel { get; set; }

        [Option(HelpText = "Birds eye view blend mode (Over, Average, Max, Min)", Default = Meshing.BlendMode.Max)]
        public Meshing.BlendMode BEVBlending { get; set; }

        [Option(HelpText = "Birds eye view coloring (Texture, Tilt, Elevation}", Default = ColorMode.Tilt)]
        public ColorMode BEVColoring { get; set; }

        [Option(HelpText = "Birds eye view sparse invalidation blocksize, relative to largest image dimension if < 1, disabled if 0", Default = 0.005)]
        public double BEVSparseBlocksize { get; set; }

        [Option(HelpText = "Birds eye view sparse invalidation block threshold", Default = 0.8)]
        public double BEVMinValidBlockRatio { get; set; }

        [Option(HelpText = "Birds eye view smoothing box size (should be odd)", Default = 1)]
        public int BEVSmoothing { get; set; }

        [Option(HelpText = "Birds eye view decimation", Default = 2)]
        public int BEVDecimation { get; set; }

        [Option(HelpText = "Inpaint birds eye view images by this many pixels", Default = 20)]
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

        [Option(HelpText = "Debug output directory", Default = null)]
        public string DebugOutputFolder { get; set; }

        [Option(HelpText = "Mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public string MeshFormat { get; set; }

        [Option(HelpText = "Image format, e.g. png, jpg, help for list", Default = "png")]
        public string ImageFormat { get; set; }

        [Option(HelpText = "Recompute existing BEVs", Default = false)]
        public bool RedoBEVs { get; set; }

        [Option(HelpText = "Search radius for feature matching in meters", Default = 2)]
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

        [Option(HelpText = "Min RANSAC feature separation meters", Default = 0.1)]
        public double MinRansacSeparation { get; set; }

        [Option(HelpText = "Min RANSAC good matches", Default = 50)]
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

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }
    }

    public class LocalBEVAligner
    {
        private LocalBEVAlignerOptions options;
        private PipelineCore pipeline;

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
        private ConcurrentDictionary<string, List<FeatureMatch>> matches =
            new ConcurrentDictionary<string, List<FeatureMatch>>();

        //modelSiteDrive-dataSiteDrive => feature matches
        private ConcurrentDictionary<string, List<FeatureMatch>> ransacMatches =
            new ConcurrentDictionary<string, List<FeatureMatch>>();

        //modelSiteDrive-dataSiteDrive => (modelPoint, dataPoint), (modelPoint, dataPoint), ...
        private ConcurrentDictionary<string, List<Tuple<Vector3, Vector3>>> spatialMatches =
            new ConcurrentDictionary<string, List<Tuple<Vector3, Vector3>>>();

        //(modelSiteDrive, dataSiteDrive), (modelSiteDrive, dataSiteDrive), ...
        List<Tuple<string, string>> siteDrivePairs = new List<Tuple<string, string>>();
        
        private const string cacheImageExt = ".tif";
        private const string cacheMaskExt = ".png";

        private double MetersPerPixel { get { return options.BEVMetersPerPixel * options.BEVDecimation; } }
        private double PixelsPerMeter { get { return 1 / MetersPerPixel; } }

        /// <summary>
        /// map a 3D point in meters from a given site drive to a 2D pint in pixels in a given site drive
        /// </summary>
        private Vector2 PointToPixel(Vector3 srcPoint, string srcSiteDrive, string dstSiteDrive)
        {
            var srcToRoot = frameCache.GetBestTransform(srcSiteDrive).Transform.Mean;
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

        public LocalBEVAligner(LocalBEVAlignerOptions options)
        {
            this.options = options;
            if (options.Cloud)
            {
                this.pipeline = new CloudPipeline(options, initQueues: false);
            }
            else
            {
                this.pipeline = new LocalPipeline(options);
            }
        }

        public int Run()
        {
            var project = Project.Find(pipeline, options.ProjectName);

            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            outputPath = pipeline.GetLocalDebugFolder(options.DebugOutputFolder, "alignment/AdjustProducts/",
                                                      project.Name);

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

            bool allowMastcam = false;
            bool requireNormals = options.BEVColoring == ColorMode.Tilt;
            bool requireTextures = options.BEVColoring == ColorMode.Texture;
            observations = Meshing.CollectMeshObservations(frameCache, observationCache,
                                                           allowMastcam, requireNormals, requireTextures,
                                                           options.OnlyForSiteDrives, options.OnlyForCameras);

            //for now lexicographically sort siteDrives so that older ones come before newer
            //just to give a canonical order
            //later in ComputePairs we may change the order depending on options
            siteDrives = observations.Select(obs => obs.SiteDrive.ToString()).Distinct().OrderBy(sd => sd).ToArray();

            pipeline.LogInfo("computing birds eye view alignment for {0} observations, {1} site drives",
                             siteDrives.Length, observations.Count());

            RenderBEVs(); //observations -> bevs, dems

            DetectFeatures(); //bevs -> features

            ComputePairs(); //siteDrives -> siteDrivePairs

            int nm = MatchPairs(); //siteDrivePairs, features -> spatialMatches

            //spatialMatches -> LandformBEV aligned FrameTransforms
            int na = 0;
            bool matchOnly = false;
            switch (options.AlignmentMode)
            {
                case AlignmentMode.Simultaneous: { na = SimultaneousAlign(); break; }
                case AlignmentMode.PairwiseMaximal: { na = PairwiseAlign(maximal: true); break; }
                case AlignmentMode.PairwiseMinimal: { na = PairwiseAlign(maximal: false); break; }
                case AlignmentMode.None: { matchOnly = true; na = 0; break; }
            }

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

            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(observations, obs => { 

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("computing products for {0} observations in parallel, completed {1}/{2}",
                                         np, nc, no);
                    }

                    Mesh mesh = Meshing.BuildOrganizedMesh(pipeline, obs, frameCache, "root", usePriors: true,
                                                           decimate: options.DecimateWedgeMeshes);

                    Image img = null;
                    if (options.BEVColoring == ColorMode.Texture && obs.Texture != null)
                    {
                        img = pipeline.LoadImage(obs.Texture.Url);
                        if (options.DecimateWedgeImages > 1)
                        {
                            img = img.Decimated(options.DecimateWedgeImages);
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
        /// populates bevs, dems, and bevOrigins from observations
        /// </summary>
        private void RenderBEVs()
        {
            if (!options.RedoBEVs && LoadCachedBEVs())
            {
                return;
            }

            BuildWedgeMeshes();

            double startSec = UTCTime.Now();
            pipeline.LogInfo("rendering {0} birds eye views...", siteDrives.Length);

            var bevOptions = new Meshing.BEVOptions
            {
                BlendMode = options.BEVBlending,
                MetersPerPixel = options.BEVMetersPerPixel,
                SparseBlockSize = options.BEVSparseBlocksize,
                MinSparseBlockValidRatio = options.BEVMinValidBlockRatio,
                Inpaint = options.BEVInpaint,
                Blur = options.BEVSmoothing,
                Decimate = options.BEVDecimation
            };

            var demOptions = (Meshing.BEVOptions)(bevOptions.Clone());
            demOptions.BlendMode = Meshing.BlendMode.Average;

            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(siteDrives, siteDrive => {

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("rendering {0} birds eye views in parallel, completed {1}/{2}",
                                         np, nc, siteDrives.Length);
                    }

                    //ensure inpurs are in a canonical order particularly for BEVBlending = Over
                    var inputs = mergeInputs[siteDrive]
                        .OrderBy(inp => inp.Item1) //order by observation name
                        .Distinct() //ConcurrentBag is not necessarily a set
                        .Select(inp => new Tuple<Mesh, Image>(inp.Item2, inp.Item3))
                        .ToArray();

                    Mesh mesh = null;
                    Image img = null;
                    if (options.BEVColoring == ColorMode.Texture)
                    {
                        var pair = Meshing.MergeMeshesAndTextures(inputs);
                        mesh = pair.Item1;
                        img = pair.Item2;
                    }
                    else
                    {
                        mesh = Mesh.Merge(inputs.Select(pr => pr.Item1).ToArray());
                    }

                    switch (options.BEVColoring)
                    {
                        case ColorMode.Texture: break;
                        case ColorMode.Tilt:
                        {
                            Meshing.ColorMeshByNormals(mesh, Meshing.TiltMode.InvAcos);
                            break;
                        }
                        case ColorMode.Elevation:
                        {
                            Meshing.ColorMeshByElevation(mesh, absolute: true);
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

                    var bev = Meshing.RenderBirdsEyeView(mesh, img, out Vector2 origin, bevOptions);

                    pipeline.LogVerbose("birds eye view for site drive {0}: {1}x{2}, origin ({3}, {4}), " +
                                        "{5} meters/pixel ({6} with decimation), sparse block size {7}, " +
                                        "valid block ratio {8}, inpaint {9}, smoothing {10}, decimation {11}",
                                        siteDrive, bev.Width, bev.Height, (int)origin.X, (int)origin.Y,
                                        options.BEVMetersPerPixel, 1 / PixelsPerMeter, options.BEVSparseBlocksize,
                                        options.BEVMinValidBlockRatio, options.BEVInpaint, options.BEVSmoothing,
                                        options.BEVDecimation);
                    
                    bevs[siteDrive] = bev;
                    bevOrigins[siteDrive] = origin;
                    
                    if (options.BEVColoring == ColorMode.Elevation && options.BEVBlending == Meshing.BlendMode.Average)
                    {
                        dems[siteDrive] = (options.StretchContrast || options.BEVThreshold > 0) ? new Image(bev) : bev;
                    }
                    else
                    {
                        Meshing.ColorMeshByElevation(mesh, absolute: true);
                        var dem = Meshing.RenderBirdsEyeView(mesh, null, out Vector2 demOrigin, demOptions);
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
                        dems[siteDrive] = dem;
                    }
                        
                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            PostProcessBEVs(out double min, out double max);

            if (options.WriteDebug)
            {
                foreach (var pair in bevs)
                {
                    var siteDrive = pair.Key;
                    var bev = pair.Value;
                    if (!options.StretchContrast && options.BEVColoring == ColorMode.Elevation)
                    {
                        bev = new Image(bev);
                        bev.ScaleValues((float)min, (float)max, 0, 1);
                    }
                    PathHelper.EnsureExists(outputPath);
                    bev.Save<byte>(outputPath + siteDrive + "_BirdsEyeView" + imageExt);
                }
            }

            SaveCachedBEVs();

            pipeline.LogInfo("generated {0} birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// TODO HACK - migrate to use pipeline database and storage if we keep this code
        /// </summary>
        private bool LoadCachedBEVs()
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("checking cache for {0} birds eye views...", siteDrives.Length);

            var available = pipeline.SearchFiles(outputPath, recursive: false).ToArray();

            int nc = 0;
            foreach (var siteDrive in siteDrives)
            {
                string basename = siteDrive + "_BirdsEyeView_cached";
                foreach (var url in available)
                {
                    var file = StringHelper.GetLastUrlPathSegment(url);
                    if (file.StartsWith(basename) && file.EndsWith(cacheImageExt))
                    {
                        var parts = file.Split('.');
                        var baseUrl = url.Substring(0, url.Length - file.Length) + parts[0];
                        var maskUrl = baseUrl + "_mask" + cacheMaskExt;
                        var demUrl = baseUrl + "_DEM" + cacheImageExt;
                        if (!Array.Exists(available, u => u == maskUrl) || !Array.Exists(available, u => u == demUrl) )
                        {
                            continue;
                        }

                        var bev = pipeline.LoadImage(url);
                        var dem = pipeline.LoadImage(demUrl);
                        var mask = pipeline.LoadImage(maskUrl);

                        bev.UnionMask(mask, new float[] { 1 });
                        dem.UnionMask(mask, new float[] { 1 });

                        bevs[siteDrive] = bev;
                        dems[siteDrive] = dem;

                        var origin = new Vector2(0.001 * double.Parse(parts[parts.Length - 3]),
                                                 0.001 * double.Parse(parts[parts.Length - 2]));
                        bevOrigins[siteDrive] = origin;

                        pipeline.LogInfo("loaded cached {0}x{1} birds eye view for site drive {2}, origin {3}",
                                         bev.Width, bev.Height, siteDrive, origin);
                        nc++;
                        break;
                    }
                }
            }

            if (nc == siteDrives.Length)
            {
                pipeline.LogInfo("loaded {0} cached birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
                return true;
            }

            if (nc > 0)
            {
                pipeline.LogWarn("loaded only {0} of {1} birds eye views from cache, must regenerate all",
                                 nc, siteDrives.Length);
                bevs.Clear();
                dems.Clear();
                bevOrigins.Clear();
            }

            return false;
        }

        /// <summary>
        /// TODO HACK - migrate to use pipeline database and storage if we keep this code
        /// </summary>
        private void SaveCachedBEVs()
        {
            foreach (var pair in bevs)
            {
                var siteDrive = pair.Key;
                var bev = pair.Value;
                var x = (long)(1000 * bevOrigins[siteDrive].X);
                var y = (long)(1000 * bevOrigins[siteDrive].Y);
                string file = outputPath + siteDrive + "_BirdsEyeView_cached." + x + "." + y + cacheImageExt;
                pipeline.LogInfo("caching {0}x{1} birds eye view {2}", bev.Width, bev.Height, file);
                PathHelper.EnsureExists(outputPath);
                bev.Save<float>(file);
                bev.MaskToImage().Save<byte>(outputPath + siteDrive + "_BirdsEyeView_cached_mask" + cacheMaskExt);
                dems[siteDrive].Save<float>(outputPath + siteDrive + "_BirdsEyeView_cached_DEM" + cacheImageExt);
            }
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
            if (options.StretchContrast || options.BEVColoring == ColorMode.Elevation)
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
        /// populate features from bevs  
        /// </summary>
        private void DetectFeatures()
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("detecting {0} features in {1} birds eye views...", options.DetectorType, bevs.Count);

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
            FeatureDetector detector = new FeatureDetector(pipeline, detectorOpts);

            int nc = 0, np = 0;
            CoreLimitedParallel.ForEach(siteDrives, siteDrive => {

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

                    if (options.WriteDebug)
                    {
                        var img = FeatureDetecting.DrawFeaturesEmgu(bev, mask, feat, siteDrive, stretch: false);
                        for (int i = 0; i < 2; i++)
                        {
                            var pixel = PointToPixel(Vector3.Zero, siteDrives[i], siteDrive);
                            var other = siteDrives[i] != siteDrive;
                            var color = new Vector3(other ? 0 : 1, other ? 1 : 0, 0);
                            DrawOrigin(img, pixel, color);
                        }
                        PathHelper.EnsureExists(outputPath);
                        img.ToOPSImage().Save<byte>(outputPath + siteDrive + "_BirdsEyeView_Features" + imageExt);
                    }

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

            var matchList = best.Keys.OrderBy(m => m.DescriptorDistance).ToList();

            matches[pair] = matchList;

            if (options.Verbose)
            {
                var histogram = new Histogram(50, pair + " matches", "distance");
                foreach (var match in matchList)
                {
                    histogram.Add(match.DescriptorDistance);
                }
                histogram.Dump(pipeline);
            }

            if (options.WriteDebug)
            {
                var modelFeatures = features[modelSiteDrive];
                var dataFeatures = features[dataSiteDrive];
                var pairs = matchList.Select(m => new KeyValuePair<int, int>(m.DataIndex, m.ModelIndex)).ToArray();
                var img = ImageMatching.DrawMatches(bevs[modelSiteDrive], bevs[dataSiteDrive],
                                                    modelFeatures, dataFeatures, pairs,
                                                    modelSiteDrive, dataSiteDrive, stretch: false);
                PathHelper.EnsureExists(outputPath);
                img.Save<byte>(outputPath + pair + "_BirdsEyeView_Matches" + imageExt);
            }

            int nm = matchList.Count;
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
            var matchList = matches[pair];
            var nm = matchList.Count;

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
            var modelPts = matchList
                .Select(m => modelFeatures[m.ModelIndex].Location - dataOriginInModel)
                .ToArray();

            //pixel offsets corresponding to data features relative to data sitedrive origin in model BEV
            var dataPtsInModel = matchList
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

            pipeline.LogInfo("RANSACing {0} feature pairs for {1}", maxTests, pair);
            int nt;
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

            ransacMatches[pair] = bestMatches.Select(m => matchList[m]).ToList();

            if (options.WriteDebug)
            {
                var d2m = bestMatches
                    .Select(m => new KeyValuePair<int, int>(matchList[m].DataIndex, matchList[m].ModelIndex))
                    .ToArray();
                PathHelper.EnsureExists(outputPath);
                var matchImg = ImageMatching.DrawMatches(bevs[modelSiteDrive], bevs[dataSiteDrive],
                                                         modelFeatures, dataFeatures, d2m,
                                                         modelSiteDrive, dataSiteDrive, stretch: false);
                matchImg.Save<byte>(outputPath + pair + "_BirdsEyeView_RANSAC_Matches" + imageExt);

                var mfColor = new Bgr(255, 0, 0); //actually RGB
                var dfColor = new Bgr(0, 255, 0); //actually RGB

                var mf = bestMatches
                    .Select(m => modelFeatures[matchList[m].ModelIndex])
                    .Cast<SIFTFeature>()
                    .CastToMKeyPoint()
                    .ToArray();

                void writeImage(string suffix, Func<Vector2, Vector2> dataPointTransform)
                {
                    var df = bestMatches
                        .Select(m =>
                                {
                                    var f = new SIFTFeature((SIFTFeature)(dataFeatures[matchList[m].DataIndex]));
                                    f.Location = dataPointTransform(dataPtsInModel[m]) + dataOriginInModel;
                                    return f;
                                })
                        .CastToMKeyPoint()
                        .ToArray();
                    
                    var img = bevs[modelSiteDrive].ToEmgu<Bgr>();
                    
                    Features2DToolbox.DrawKeypoints(img, new VectorOfKeyPoint(mf), img, mfColor,
                                                    Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
                    
                    Features2DToolbox.DrawKeypoints(img, new VectorOfKeyPoint(df), img, dfColor,
                                                    Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
                    
                    PathHelper.EnsureExists(outputPath);
                    img.ToOPSImage().Save<byte>(outputPath + pair + "_BirdsEyeView_RANSAC" + suffix + imageExt);
                }
                
                writeImage("_0_priors", pt => pt);
                writeImage("_1_rotation", pt => bestTransform.Rotate(pt));
                writeImage("_2_solved", pt => bestTransform.Transform(pt));
            }

            nm = bestMatches.Count;
            pipeline.LogInfo("performed {0}/{1} ransac tests for {2} ({3} total combinations), best transform " +
                             "({4:F3}m, {5:F3}m, {6:F3}deg), residual {7:F3}m, {8} matches ({9:F3}s)",
                             nt, maxTests, pair, totalCombinations,
                             bestTransform.Translation.X * MetersPerPixel, bestTransform.Translation.Y * MetersPerPixel,
                             MathHelper.ToDegrees(bestTransform.Rotation), bestResidual * MetersPerPixel,
                             nm, UTCTime.Now() - startSec);
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

            var pairs = new List<Tuple<Vector3, Vector3>>();
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
                pairs.Add(new Tuple<Vector3, Vector3>(mp, dp));
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
                pairs = pairs.Where(pr => Math.Abs(Vector3.Distance(pr.Item1, pr.Item2) - median) < threshold).ToList();
                int nn = pairs.Count();
                if (nn < n)
                {
                    pipeline.LogInfo("{0} outlier spatial matches for {1}, median {2:F3}, threshold {3:F3} ({4} MAD)",
                                     n - nn, pair, median, threshold, options.SpatialOutlierMADs);
                }
                n = nn;
            }
                
            if (options.WriteDebug)
            {
                var mesh = ImageMatching.MakeMatchMesh(pairs.Select(p => p.Item1).ToArray(),
                                                       pairs.Select(p => p.Item2).ToArray());
                PathHelper.EnsureExists(outputPath);
                mesh.Save(outputPath + pair + "_matches" + meshExt);
            }

            spatialMatches[pair] = pairs;

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
                    siteDrives = siteDrives.OrderByDescending(sd => BEVArea(sd)).ToArray();
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

            pipeline.LogInfo("site drive pairs: {0}",
                             string.Join(", ",
                                         siteDrivePairs.Select(pr => string.Format("({0}, {1})", pr.Item1, pr.Item2))));
        }

        /// <summary>
        /// compute matches, ransacMatches, and spatialMatches from siteDrivePairs and features  
        /// </summary>
        private int MatchPairs()
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("matching features in birds eye views for {0} site drive pairs...", siteDrivePairs.Count);

            var histogram = new Histogram(10, "pairs", "matches");
            int nc = 0, np = 0, ng = 0;
            var good = new ConcurrentDictionary<string, bool>();
            CoreLimitedParallel.ForEach(siteDrivePairs, pair => {
                    
                    Interlocked.Increment(ref np);
                    
                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("matching {0} sitedrive pairs in parallel, completed {1}/{2}",
                                         np, nc, siteDrivePairs.Count);
                    }

                    var model = pair.Item1;
                    var data = pair.Item2;

                    int nm = MatchFeatures(model, data); //features -> matches

                    if (nm > options.MinRansacMatches)
                    {
                        nm = RansacMatches(model, data); //matches -> ransacMatches

                        if (nm > 0)
                        {
                            nm = SpatializeMatches(model, data); //ransacMatches -> spatialMatches
                            
                            if (nm >= options.MinRansacMatches)
                            {
                                Interlocked.Increment(ref ng);
                                good[model] = true;
                                good[data] = true;
                            }
                        }
                    }

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            if (options.Verbose)
            {
                histogram.Dump(pipeline);
            }

            pipeline.LogInfo("matched features in birds eye views for {0} site drive pairs, " +
                             "{1} with at least threshold {2} matches ({3:F3}s)",
                             siteDrivePairs.Count, ng, options.MinRansacMatches, UTCTime.Now() - startSec);

            return good.Keys.Count;
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
        /// at this stage the graph can is a DAG because a node can be a child of more than one parent
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

            var fx = (options.FixSiteDrives ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

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
                if (spatialMatches.ContainsKey(key) && spatialMatches[key].Count >= options.MinRansacMatches)
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
        private void SaveTransforms(IEnumerable<Node> aligned)
        {
            var unaligned = new HashSet<string>(siteDrives);
            var transformSource = TransformSource.LandformBEV;
            foreach (var node in aligned)
            {
                unaligned.Remove(node.Name);
                var ut = new UncertainRigidTransform(node.WorldTransform.Value);
                var frame = frameCache.GetFrame(node.Name);
                var ft = FrameTransform.FindOrCreate(pipeline, frame, transformSource, ut);
                ft.Transform = ut;
                ft.Save(pipeline);
                if (frame.AddTransform(ft))
                {
                    frame.Save(pipeline);
                }
                pipeline.LogInfo("saved {0} adjusted transform for site drive {1}", transformSource, node.Name);
            }
            foreach (var sd in unaligned)
            {
                var frame = frameCache.GetFrame(sd);
                if (frame.RemoveTransform(transformSource))
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
                node.WorldTransform = Meshing.GetTransform(node.Name, "root", frameCache, usePriors: true).Mean;
            }

            var nodesToAlign = new List<Node>();
            foreach (var node in nodes)
            {
                if ((node.Parent != null || node.Children.Count > 0) && !fixedNodes.Contains(node.Name))
                {
                    nodesToAlign.Add(node);
                }
            }

            //TODO
            throw new NotImplementedException("simultaneous align not implemented yet");

            //SaveTransforms(nodesToAlign);

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
            //the best parent is the one to follow to get to the root along a best path
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

            //align every node to its a parent
            //a node has a parent iff we found enough ransac matches from that node to a higher-priority sitedrive
            var nodesToAlign = nodes
                .Where(n => n.Parent != null)
                .Where(n => !fixedNodes.Contains(n.Name))
                .ToList();
            pipeline.LogInfo("pairwise aligning {0} site drives", nodesToAlign.Count);
            int nc = 0, np = 0;
            CoreLimitedParallel.ForEach(nodesToAlign, node => {

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("pairwise aligning {0} site drives in parallel, completed {1}/{2}",
                                         np, nc, nodesToAlign.Count);
                    }

                    var model = node.Parent.Name;
                    var data = node.Name;
                    
                    var modelToRootPrior = Meshing.GetTransform(model, "root", frameCache, usePriors: true).Mean;
                    
                    var dataToRootPrior = Meshing.GetTransform(data, "root", frameCache, usePriors: true).Mean;
                    
                    var rootToModelPrior = Matrix.Invert(modelToRootPrior);
                    
                    //the spatial matches are in root frame, transform them to model prior frame
                    var matches = spatialMatches[model + "-" + data];
                    var modelPts = matches.Select(m => Vector3.Transform(m.Item1, rootToModelPrior)).ToArray();
                    var dataPts = matches.Select(m => Vector3.Transform(m.Item2, rootToModelPrior)).ToArray();
                    
                    double priorResidual = 0;
                    for (int i = 0; i < modelPts.Length; i++)
                    {
                        priorResidual += Vector3.DistanceSquared(modelPts[i], dataPts[i]);
                    }
                    priorResidual = Math.Sqrt(priorResidual / modelPts.Length);
                    
                    //compute transform adj that best aligns data points to model points
                    var residual = Procrustes.CalculateRigid(dataPts, modelPts, out Matrix adj);
                    
                    pipeline.LogInfo("aligned sitedrive {0} to {1}, residual {2}->{3}m", data, model,
                                     priorResidual, residual);
                    
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
                node.WorldTransform = Meshing.GetTransform(node.Name, "root", frameCache, usePriors: true).Mean;
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

            SaveTransforms(nodesToAlign);

            pipeline.LogInfo("pairwise aligned {0} nodes ({1:F3}s)", nodesToAlign.Count, UTCTime.Now() - startSec);

            return nodesToAlign.Count;
        }
    }
}

