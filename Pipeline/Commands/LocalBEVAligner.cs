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
using OPS.Pipeline.Alignment;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public enum ColorMode { Texture, Tilt, Elevation };

    [Verb("local-bev-align", HelpText = "birds eye view align locally")]
    public class LocalBEVAlignerOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "Project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Only generate products for specific site drives, comma separated", Default = null)]
        public string OnlyForSiteDrives { get; set; }

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

        [Option(HelpText = "Birds eye view coloring (Texture, Tilt, Elevation}", Default = ColorMode.Tilt)]
        public ColorMode BEVColoring { get; set; }

        [Option(HelpText = "Birds eye view sparse invalidation blocksize, relative to largest image dimension if < 1, disabled if 0", Default = 0.005)]
        public double BEVSparseBlocksize { get; set; }

        [Option(HelpText = "Birds eye view sparse invalidation block threshold", Default = 0.8)]
        public double BEVMinValidBlockRatio { get; set; }

        [Option(HelpText = "Birds eye view smoothing box size (should be odd)", Default = 3)]
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

        [Option(HelpText = "Max descriptor distance ratio", Default = 0.9)]
        public double MaxDescriptorDistanceRatio { get; set; }

        [Option(HelpText = "Max descriptor distance", Default = 300)]
        public double MaxDescriptorDistance { get; set; }

        [Option(HelpText = "Max RANSAC tests", Default = 100000)]
        public int MaxRansacTests { get; set; }

        [Option(HelpText = "Max RANSAC residual in meters", Default = 0.02)]
        public double MaxRansacResidual { get; set; }

        [Option(HelpText = "Max RANSAC feature match radius meters", Default = 0.1)]
        public double RansacMatchRadius { get; set; }

        [Option(HelpText = "Min RANSAC good matches", Default = 10)]
        public int MinRansacAgreement { get; set; }

        [Option(HelpText = "Optimize contrast", Default = true)]
        public bool StretchContrast { get; set; }

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

        //sitedrive name => (mesh, image), (mesh, image), ...
        private ConcurrentDictionary<string, ConcurrentBag<Tuple<Mesh, Image>>> mergeInputs =
            new ConcurrentDictionary<string, ConcurrentBag<Tuple<Mesh, Image>>>();

        //sitedrive name => BEV image
        private ConcurrentDictionary<string, Image> bevs = new ConcurrentDictionary<string, Image>();

        //sitedrive name => DEM image
        private ConcurrentDictionary<string, Image> dems = new ConcurrentDictionary<string, Image>();

        //sitedrive name => pixel in BEV image corresponding to world frame origin (based on priors)
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
            new ConcurrentDictionary<string, List<Tuple<Vector3, Vector3>>();

        //(modelSiteDrive, dataSiteDrive), (modelSiteDrive, dataSiteDrive), ...
        List<Tuple<string, string>> allPairs = new List<Tuple<string, string>>();
        
        private const string cacheImageExt = ".tif";
        private const string cacheMaskExt = ".png";

        private double GetPixelsPerMeter()
        {
            return 1 / (options.BEVMetersPerPixel * options.BEVDecimation);
        }

        /// <summary>
        /// map a 3D point in meters from a given site drive to a 2D pint in pixels in a given site drive
        /// </summary>
        private Vector2 PointToPixel(Vector3 srcPoint, string srcSiteDrive, string dstSiteDrive)
        {
            var srcSiteDriveToRoot = frameCache.GetBestTransform(srcSiteDrive).Transform;
            var ptInRoot = Vector3.Transform(srcPoint, srcSiteDriveToRoot.Mean);
            var pixelsPerMeter = GetPixelsPerMeter();
            var pixelInRoot = ptInRoot * pixelsPerMeter;
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

            //lexicographically sort siteDrives so that older ones come before newer
            //also this ensures that multiple runs with the same sitedrives but possibly different observations
            //will consider them in the same order
            siteDrives = observations.Select(obs => obs.SiteDrive.ToString()).Distinct().OrderBy(sd => sd).ToArray();

            pipeline.LogInfo("computing birds eye view alignment for site drives {0} and {1}, {2} observations",
                             siteDrives[0], siteDrives[1], observations.Count());

            RenderBEVs(); //observations -> bevs, dems

            DetectFeatures(); //bevs -> features

            ComputeSiteDrivePairs(); //siteDrives -> allPairs

            int nc = 0, np = 0;
            CoreLimitedParallel.ForEach(pair => {
                    
                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("aligning {0} sitedrive pairs in parallel, completed {1}/{2}",
                                         np, nc, allPairs.Count);
                    }

                    var model = pair.Item1;
                    var data = pair.Item2;

                    MatchFeatures(model, data); //features -> matches

                    RansacMatches(model, data); //matches -> ransacMatches

                    SpatializeMatches(model, data); //ransacMatches -> spatialMatches

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            int na = ProcrustesAlign(); //spatialMatches -> LandformBEV aligned FrameTransforms

            pipeline.LogInfo("aligned {0} site drives from {1} birds eye views ({1:F3}s)", na, bevs.Count,
                             UTCTime.Now() - startSec);

            return 0;
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

            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(siteDrives, siteDrive => {

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("rendering {0} birds eye views in parallel, completed {1}/{2}",
                                         np, nc, siteDrives.Length);
                    }

                    Mesh mesh = null;
                    Image img = null;
                    if (options.BEVColoring == ColorMode.Texture)
                    {
                        var pair = Meshing.MergeMeshesAndTextures(mergeInputs[siteDrive].Distinct().ToArray());
                        mesh = pair.Item1;
                        img = pair.Item2;
                    }
                    else
                    {
                        mesh = Mesh.Merge(mergeInputs[siteDrive].Distinct().Select(pr => pr.Item1).ToArray());
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
                        string file = outputPath + siteDrive + meshExt;
                        PathHelper.EnsureExists(outputPath);
                        mesh.Save(file, imageFilename);
                    }

                    var bev = RenderBEV(mesh, img, out Vector2 origin);

                    pipeline.LogVerbose("birds eye view for site drive {0}: {1}x{2}, origin ({3}, {4}), " +
                                        "{5} meters/pixel, sparse block size {6}, valid block ratio {7}, " +
                                        "inpaint {8}, smoothing {9}, decimation {10}",
                                        siteDrive, bev.Width, bev.Height, (int)origin.X, (int)origin.Y,
                                        options.BEVMetersPerPixel, options.BEVSparseBlocksize,
                                        options.BEVMinValidBlockRatio, options.BEVInpaint, options.BEVSmoothing,
                                        options.BEVDecimation);
                    
                    bevs[siteDrive] = bev;
                    bevOrigins[siteDrive] = origin;
                    
                    if (options.BEVColoring == ColorMode.Elevation)
                    {
                        dems[siteDrive] = (options.StretchContrast || options.BEVThreshold > 0) ? new Image(bev) : bev;
                    }
                    else
                    {
                        Meshing.ColorMeshByElevation(mesh, absolute: true);
                        var dem = RenderBEV(mesh, null, out Vector2 demOrigin);
                        if (dem.Width != bev.Width || dem.Height != bev.Height)
                        {
                            throw new Exception("DEM dimensions {0}x{1} don't match BEV {2}x{3}",
                                                dem.Width, dem.Height, bev.Width, bev.Height);
                        }
                        if (demOrigin != origin)
                        {
                            throw new Exception("DEM origin {0} doesn't match BEV {1}", demOrigin, origin);
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
                    string file = outputPath + siteDrive + "_BirdsEyeView" + imageExt;
                    PathHelper.EnsureExists(outputPath);
                    bev.Save<byte>(file);
                }
            }

            SaveCachedBEVs();

            pipeline.LogInfo("generated {0} birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
        }

        private Image RenderBEV(Mesh mesh, Image img, out Vector2 origin)
        {
            bool greyscale = true;
            bool ccw = false;
            return Meshing.RenderBirdsEyeView(mesh, img, options.BEVMetersPerPixel, greyscale, out origin,
                                              ccw, options.BEVSparseBlocksize, options.BEVMinValidBlockRatio,
                                              options.BEVInpaint, options.BEVSmoothing,
                                              options.BEVDecimation);
        }

        /// <summary>
        /// TODO HACK - migrate to use pipeline database and storage if we keep this code
        /// </summary>
        private bool LoadCachedBEVs()
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("checking cache for {0} birds eye views...", siteDrives.Length);

            int nc = 0;
            foreach (var siteDrive in siteDrives)
            {
                string basename = siteDrive + "_BirdsEyeView_cached";
                var available = pipeline.SearchFiles(outputPath, recursive: false).ToArray();
                foreach (var url in available)
                {
                    var file = StringHelper.GetLastUrlPathSegment(url);
                    if (file.StartsWith(basename) && file.EndsWith(cacheImageExt))
                    {
                        var parts = file.Split('.');
                        var baseUrl = url.Substring(0, url.Length - file.Length) + parts[0];
                        var maskUrl = baseUrl + "_mask" + cacheMaskExt;
                        var demUrl = baseUrl + "_DEM" + cacheMaskExt;
                        if (!Array.Exists(available, u => u == maskUrl) || !Array.Exists(available, u => u == demUrl) )
                        {
                            continue;
                        }
                        var bev = pipeline.LoadImage(url);
                        bev.UnionMask(pipeline.LoadImage(maskUrl), new float[] { 1 });
                        bevs[siteDrive] = bev;
                        var origin = new Vector2(int.Parse(parts[parts.Length - 3]),
                                                 int.Parse(parts[parts.Length - 2]));
                        bevOrigins[siteDrive] = origin;
                        dems[siteDrive] = pipeline.LoadImage(demUrl);
                        pipeline.LogInfo("loaded cached {0}x{1} birds eye view for site drive {2}, origin {3}",
                                         bev.Width, bev.Height, siteDrive, origin);
                        nc++;
                        break;
                    }
                }
            }

            if (nc == siteDrives.Length)
            {
                pipeline.LogInfo("loaded {0} cached birds eye views ({1:F3}s)",
                                 bevs.Count, UTCTime.Now() - startSec);
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
                int x = (int)bevOrigins[siteDrive].X;
                int y = (int)bevOrigins[siteDrive].Y;
                string file = outputPath + siteDrive + "_BirdsEyeView_cached." + x + "." + y + cacheImageExt;
                pipeline.LogInfo("caching {0}x{1} birds eye view {2}", bev.Width, bev.Height, file);
                PathHelper.EnsureExists(outputPath);
                bev.Save<float>(file);
                bev.MaskToImage().Save<byte>(outputPath + siteDrive + "_BirdsEyeView_cached_mask" + cacheMaskExt);
                dems[siteDrive].Save<float>(outputPath + siteDrive + "_BirdsEyeView_cached_DEM" + cacheImageExt);
            }
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

                    var pair = new Tuple<Mesh, Image>(mesh, img);
                    mergeInputs.AddOrUpdate(obs.SiteDrive.ToString(),
                                            _ => new ConcurrentBag<Tuple<Mesh, Image>>(new [] { pair }),
                                            (_, bag) => { bag.Add(pair); return bag; });

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            pipeline.LogInfo("created wedge meshes for {0} observations ({1:F3}s)", nc, UTCTime.Now() - startSec);
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
                int nStddev = 3;
                double lower = Math.Max(mean - stddev * nStddev, min);
                double upper = Math.Min(mean + stddev * nStddev, max);
                pipeline.LogInfo("stretching [{0}, {1}] -> [0, 1] ({2} stddev)", lower, upper, nStddev);
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

            pipeline.LogInfo("{0} valid pixels, min {1}, max{2}, mean {3}, stddev {4}", n, min, max, mean, stddev);
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
                        string file = outputPath + siteDrive + "_BirdsEyeView_Features" + imageExt;
                        PathHelper.EnsureExists(outputPath);
                        img.ToOPSImage().Save<byte>(file);
                    }

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            detector.DumpHistograms(pipeline);

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
            var pixelsPerMeter = GetPixelsPerMeter();
            if (crossRadius > 0)
            {
                var cr = crossRadius * pixelsPerMeter;
                img.Draw(ToLineSegment2DF(pixel + new Vector2(-cr, 0), pixel + new Vector2(cr, 0)), bgr, 2);
                img.Draw(ToLineSegment2DF(pixel + new Vector2(0, -cr), pixel + new Vector2(0, cr)), bgr, 2);
            }
            if (circleRadius > 0)
            {
                var cr = circleRadius * pixelsPerMeter;
                img.Draw(new CircleF(ToPointF(pixel), (float)cr), bgr, 2);
            }
        }

        /// <summary>
        /// populates matches[modelSiteDrive-dataSiteDrive] from features
        /// assumes features[siteDrive] are sorted by increasing distance to origin of siteDrive
        /// </summary>
        private void MatchFeatures(string modelSiteDrive, string dataSiteDrive)
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("matching features in birds eye views for site drives {0} (model) and  {1} (data)...",
                             modelSiteDrive, dataSiteDrive);

            var modelFeatures = features[modelSiteDrive];
            var dataFeatures = features[dataSiteDrive];

            //pixel corresponding to origin of model sitedrive in model BEV
            var modelOrigin = PointToPixel(Vector3.Zero, modelSiteDrive, modelSiteDrive);

            //pixel corresponding to origin of data sitedrive in data BEV
            var dataOrigin = PointToPixel(Vector3.Zero, dataSiteDrive, dataSiteDrive);

            //pixel corresponding to origin of data sitedrive in model BEV
            var dataOriginInModel = PointToPixel(Vector3.Zero, dataSiteDrive, modelSiteDrive);

            //distance in pixels of each model feature to pixel corresponding to origin of model sitedrive in model BEV
            var modelDistances = modelFeatures.Select(f => Vector2.Distance(f.Location, modelOrigin)).ToArray();

            //NOTE: features for a site drive are already sorted by distance to origin of that site drive

            double radius = options.MatchRadius * GetPixelsPerMeter();

            var matchList = new List<FeatureMatch>();
            var histogram =
                new Histogram(50, string.Format("{0}-{1} matches", modelSiteDrive, dataSiteDrive), "distance");
            for (int i = 0; i < dataFeatures.Length; i++)
            {
                var df = dataFeatures[i];
                var dfInModel = dataOriginInModel + (df.Location - dataOrigin);
                var r = Vector2.Distance(dfInModel, modelOrigin);
                double minRadius = r - radius, maxRadius = r + radius;
                int minSearchIndex = BinarySearch(modelDistances, minRadius);
                int maxSearchIndex = BinarySearch(modelDistances, maxRadius) - 1;
                if (maxSearchIndex >= minSearchIndex)
                {
                    var match = BruteForceMatcher
                        .FindBestModelFeatureForDataFeature(modelFeatures, dataFeatures, i,
                                                            options.MaxDescriptorDistanceRatio,
                                                            mf => Vector2.Distance(mf.Location, dfInModel) <= radius,
                                                            minSearchIndex,
                                                            maxSearchIndex);
                    if (match != null && match.DescriptorDistance < options.MaxDescriptorDistance)
                    {
                        matchList.Add(match);
                        histogram.Add(match.DescriptorDistance);
                    }
                }
            }

            matches[modelSiteDrive + "-" + dataSiteDrive] = matchList;

            if (options.Verbose)
            {
                histogram.Dump(pipeline);
            }

            if (options.WriteDebug)
            {
                var d2m = matchList.Select(m => new KeyValuePair<int, int>(m.DataIndex, m.ModelIndex)).ToArray();
                var img = ImageMatching.DrawMatches(bevs[modelSiteDrive], bevs[dataSiteDrive],
                                                    modelFeatures, dataFeatures, d2m,
                                                    modelSiteDrive, dataSiteDrive, stretch: false);
                string file = outputPath + modelSiteDrive + "-" + dataSiteDrive + "_BirdsEyeView_Matches" + imageExt;
                PathHelper.EnsureExists(outputPath);
                img.Save<byte>(file);
            }

            pipeline.LogInfo("{0} feature matches for site drives {1} (model) and  {2} (data) ({3:F3}s)",
                             matchList.Count, modelSiteDrive, dataSiteDrive, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// populates ransacMatches[modelSiteDrive-dataSiteDrive] from corresponding matches and features
        /// </summary>
        private void RansacMatches(string modelSiteDrive, string dataSiteDrive)
        {
            var matchList = matches[modelSiteDrive + "-" + dataSiteDrive];

            double startSec = UTCTime.Now();
            pipeline.LogInfo("RANSACing {0} feature matches for stite drives {1} (model) and  {2} (data)...",
                             matchList.Count, modelSiteDrive, dataSiteDrive);

            var modelFeatures = features[modelSiteDrive];
            var dataFeatures = features[dataSiteDrive];

            //pixel corresponding to origin of model sitedrive in model BEV
            var modelOrigin = PointToPixel(Vector3.Zero, modelSiteDrive, modelSiteDrive);

            //pixel corresponding to origin of data sitedrive in data BEV
            var dataOrigin = PointToPixel(Vector3.Zero, dataSiteDrive, dataSiteDrive);

            //pixel corresponding to origin of data sitedrive in model BEV
            var dataOriginInModel = PointToPixel(Vector3.Zero, dataSiteDrive, modelSiteDrive);

            //pixel offsets corresponding to model features relative to data sitedrive origin in model BEV
            var modelPts = matches
                .Select(m => modelFeatures[m.ModelIndex].Location - dataOriginInModel)
                .ToArray();

            //pixel offsets corresponding to data features relative to data sitedrive origin in model BEV
            var dataPtsInModel = matches
                .Select(m => dataFeatures[m.DataIndex].Location - dataOrigin)
                .ToArray();

            var random = NumberHelper.MakeRandomGenerator();

            var bestTransform = new RigidTransform2D();
            var bestMatches = new List<int>(matches.Count);
            double bestResidual = double.PositiveInfinity;

            double radiusSquared = options.RansacMatchRadius * GetPixelsPerMeter();
            radiusSquared = radiusSquared * radiusSquared;

            var pixelsPerMeter = GetPixelsPerMeter();
            var metersPerPixel = 1 / pixelsPerMeter;

            var maxResidual = options.MaxRansacResidual * pixelsPerMeter;

            int i;
            for (i = 0; i < options.MaxRansacTests; i++)
            {
                var seeds = new [] { random.Next(0, matches.Count), random.Next(0, matches.Count) };
                if (seeds[0] == seeds[1])
                {
                    continue;
                }

                var xform = RigidTransform2D.Estimate(seeds.Select(s => dataPtsInModel[s]).ToArray(), 
                                                      seeds.Select(s => modelPts[s]).ToArray(),
                                                      out double residual);
                if (residual > bestResidual)
                {
                    continue;
                }

                bestMatches.Clear();
                residual = 0;
                for (int j = 0; j < matches.Count; j++)
                {
                    var d = Vector2.DistanceSquared(xform.Transform(dataPtsInModel[j]), modelPts[j]);
                    if (d < radiusSquared)
                    {
                        bestMatches.Add(j);
                        residual += d * d;
                    }
                }

                if (bestMatches.Count < options.MinRansacAgreement)
                {
                    continue;
                }

                xform = RigidTransform2D.Estimate(bestMatches.Select(j => dataPtsInModel[j]).ToArray(),
                                                  bestMatches.Select(j => modelPts[j]).ToArray(),
                                                  out residual);

                if (residual < bestResidual)
                {
                    bestResidual = residual;
                    bestTransform = xform;
                }

                if (bestResidual < maxResidual)
                {
                    break;
                }
            }

            if (options.WriteDebug)
            {
                var mfColor = new Bgr(255, 0, 0); //actually RGB
                var dfColor = new Bgr(0, 255, 0); //actually RGB

                var mf = bestMatches
                    .Select(m => modelFeatures[matches[m].ModelIndex])
                    .Cast<SIFTFeature>()
                    .CastToMKeyPoint()
                    .ToArray();

                var df = bestMatches
                    .Select(m =>
                            {
                                var f = new SIFTFeature((SIFTFeature)dataFeatures[matches[m].DataIndex]);
                                f.Location = bestTransform.Transform(dataPtsInModel[m]) + dataOriginInModel;
                                return f;
                            })
                    .CastToMKeyPoint()
                    .ToArray();

                var img = bevs[modelSiteDrive].ToEmgu<Bgr>();

                Features2DToolbox.DrawKeypoints(img, new VectorOfKeyPoint(mf), img, mfColor,
                                                Features2DToolbox.KeypointDrawType.DrawRichKeypoints);

                Features2DToolbox.DrawKeypoints(img, new VectorOfKeyPoint(df), img, dfColor,
                                                Features2DToolbox.KeypointDrawType.DrawRichKeypoints);

                string file = outputPath + modelSiteDrive + "-" + dataSiteDrive + "_BirdsEyeView_RANSAC" + imageExt;
                PathHelper.EnsureExists(outputPath);
                img.ToOPSImage().Save<byte>(file);
            }

            bestTransform.Translation *= metersPerPixel;
            bestResidual *= metersPerPixel;

            pipeline.LogInfo("{0} ransac tests, best transform ({1:F3}m, {2:F3}m, {3:F3}deg), residual {4:F3}m, " +
                             "{5} matches ({6:F3}s)", i, bestTransform.Translation.X, bestTransform.Translation.Y,
                             MathHelper.ToDegrees(bestTransform.Rotation), bestResidual, bestMatches.Count,
                             UTCTime.Now() - startSec);

            ransacMatches[modelSiteDrive + "-" + dataSiteDrive] = bestMatches.Select(m => matches[m]).ToList();
        }

        /// <summary>
        /// compute spatialMatches from ransacMatches, features, and dems
        /// </summary>
        private void SpatializeMatches(string modelSiteDrive, string dataSiteDrive)
        {
            var modelFeatures = features[modelSiteDrive];
            var dataFeatures = features[dataSiteDrive];

            //pixel corresponding to world origin in model BEV
            var modelOrigin = bevOrigins[modelSiteDrive];

            //pixel corresponding to world origin in data BEV
            var dataOrigin = bevOrigins[dataSiteDrive];

            var modelDEM = dems[modelSiteDrive];
            var dataDEM = dems[dataSiteDrive];

            var key = modelSiteDrive + "-" + dataSiteDrive;

            var pairs = new List<Tuple<Vector3, Vector3>>();
            foreach (var match in ransacMatches[key])
            {
                var mf = modelFeatures[match.ModelIndex];
                var df = dataFeatures[match.DataIndex];

                var mz = modelDem[(int)mf.Location.Y, (int)mf.Location.X];
                var mxy = (mf.Location - modelOrigin) * metersPerPixel;

                var dz = dataDem[(int)df.Location.Y, (int)df.Location.X];
                var dxy = (df.Location - dataOrigin) * metersPerPixel;

                pairs.Add(new Vector3(mxy.X, mxy.Y, mz), new Vector3(dxy.X, dxy.Z, dz));
            }

            if (options.WriteDebug)
            {
                var mesh = ImageMatching.MakeMatchMesh(pairs.Select(p => p.Item1), pairs.Select(p => p.Item2));
                string file = outputPath + siteDrive + "_matches" + meshExt;
                PathHelper.EnsureExists(outputPath);
                mesh.Save(file);
            }

            spatialMatches[key] = pairs;
        }

        /// <summary>
        /// compute allPairs of sitedrive pairs that are candidates for relative alignment  
        /// </summary>
        private void ComputeSiteDrivePairs()
        {
            //siteDrives has already been lexicograpically sorted
            //so that for all j > i siteDrives[j] > siteDrives[i]
            //there are a few different strategies we could consider here
            //like choosing "model" and "data" based on number of features in each
            //but for now lets call "model" the older sitedrive and "data" the newer one
            for (int i = 0; i < siteDrives.Length; i++)
            {
                for (int j = i + 1; j < siteDrives.Length; j++)
                {
                    allPairs.Add(new Tuple<string, string>(siteDrives[i], siteDrives[j]));
                }
            }
        }

        private class Node
        {
            public string Name;

            public Node Parent;

            public List<Node> Chidren = new List<Node>();

            public int Depth = int.MaxValue; //length of shortest path to world

            public Matrix Transform; //to parent

            public Matrix? WorldTransform; //to world

            public Node(string name)
            {
                this.Name = name;
            }
        }

        /// <summary>
        /// Procrustes align all sitedrive pairs that have a sufficent number of spatialized ransac feature matches
        /// then compute the adjusted sitedrive -> root transforms and write them back to the database
        /// using TransformSource = LandformBEV
        /// </summary>
        private int ProcrustesAlign()
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("Procrustes aligning site drives...");

            var nodes = new Dictionary<string, Node>();
            foreach (var sd in siteDrives)
            {
                nodes[sd] = new Node(sd);
            }

            //for each pair of sitedrives for which we have a sufficient spatial match
            //add the "data" sitedrive as a child of the "model" sitedrive
            //at this stage the graph can is a DAG because a node can be a child of more than one parent
            //the graph is also possibly disconnected (i.e. there can be more than one node with no parent)
            foreach (var pair in allPairs)
            {
                var model =  pair.Item1;
                var data =  pair.Item2;
                var key = model + "-" + data;
                if (spatialMatches.ContainsKey(key) && spatialMatches[key].Count >= options.MinRansacAgreement)
                {
                    var parent = nodes[model];
                    var child = nodes[data];
                    parent.Children.Add(child);
                    child.Parent = parent; //for now any parent will do
                }
            }

            //BFS the graph to set the best parent for each node
            //the best parent is the one to follow to get to the root along a shortest path
            foreach (var node in nodes.Where(n => n.Parent == null))
            {
                node.Depth = 0;
                var queue = new Queue<Node>();
                queue.Enqueue(node);
                while (queue.Count > 0)
                {
                    var parent = queue.Dequeue();
                    foreach (var child in parent.Children)
                    {
                        if (child.Depth > parent.Depth + 1)
                        {
                            child.Parent = parent;
                            child.Depth = parent.Depth + 1;
                            queue.Enqueue(child);
                        }
                    }
                }
            }

            //Procrustes align every node that has a parent
            //a node has a parent iff we found enough ransac matches from that node to an earlier sitedrive
            var nodesToAlign = nodes.Where(n => n.Parent != null).ToArray();
            pipeline.LogInfo("Procrustes aligning {0} site drives", nodesToAlign.Length);
            int nc = 0, np = 0;
            CoreLimitedParallel.ForEach(nodesToAlign, node => {

                    Interlocked.Increment(np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("Procrustes aligning for {0} site drives in parallel, completed {1}/{2}",
                                         np, nc, nodesToAlign.Length);
                    }

                    var model = node.Parent.Name;
                    var data = node.Name;
                    
                    var modelToRootPrior = Meshing.GetTransform(model, "root", frameCache, usePriors: true);
                    
                    var dataToRootPrior = Meshing.GetTransform(data, "root", frameCache, usePriors: true);
                    
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
                    
                    pipeline.LogInfo("Procrustes aligned sitedrive {0} to {1}, residual {2}->{3}m", data, model,
                                     priorResidual, residual);
                    
                    //row matrix transforms compose left to right
                    var dataToModelPrior = dataToRootPrior * rootToModelPrior;
                    
                    //adjusted transform taking points in data frame to points in model frame
                    node.Transform = dataToModelPrior * adj;

                    Interlocked.Decrement(np);
                    Interlocked.Increment(nc);
                });

            //compute a world transform for each node (i.e. sitedrive to root transform)
            //for a node with no parent this is just the prior
            //otherwise it's the concatenation of adjusted transforms on shortest path from the node to the world frame
            foreach (var node in nodes)
            {
                if  (node.WorldTransform == null)
                {
                    var stack = new Stack<Node>();
                    do
                    {
                        stack.Push(node);
                        node = node.Parent;
                    }
                    while (node != null && node.WorldTransform == null);

                    while (stack.Count > 0)
                    {
                        node = stack.Pop();
                        if (node.Parent == null)
                        {
                            node.WorldTransform = Meshing.GetTransform(node.Name, "root", frameCache, usePriors: true);
                        }
                        else
                        {
                            //row matrix transforms compose left to right
                            node.WorldTransform = node.Transform * node.Parent.WorldTransform.Value;
                        }
                    }
                }
            }

            //write out sitedrive -> root adjusted transforms
            foreach (var node in nodesToAlign)
            {
                var transformSource = TransformSource.LandformBEV;
                var ut = new UncertainRigidTransform(node.WorldTransform.Value);
                var ft = FrameTransform.FindOrCreate(pipeline, node.Name, transformSource, ut);
                ft.Transform = ut;
                ft.Save(pipeline);
                if (frame.AddTransform(ft))
                {
                    frame.Save(pipeline);
                }
                pipeline.LogInfo("saved {0} adjusted transform for site drive {1}", transformSource, node.Name)
            }

            pipeline.LogInfo("Procrustes aligned {0} nodes ({1:F3}s)", nodesToAlign.Length, UTCTime.Now() - startSec);

            return nodesToAlign.Length;
        }
    }
}

