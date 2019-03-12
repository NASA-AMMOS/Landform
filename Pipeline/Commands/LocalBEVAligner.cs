using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
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
        ConcurrentDictionary<string, Image> bevs = new ConcurrentDictionary<string, Image>();

        //sitedrive name => pixel in BEV image corresponding to worl frame origin (based on priors)
        ConcurrentDictionary<string, Vector2> bevOrigins = new ConcurrentDictionary<string, Vector2>();

        //indexed by sitedrive name
        ConcurrentDictionary<string, ImageFeature[]> features = new ConcurrentDictionary<string, ImageFeature[]>();

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

            siteDrives = observations.Select(obs => obs.SiteDrive.ToString()).Distinct().OrderBy(sd => sd).ToArray();
            if (siteDrives.Length != 2)
            {
                pipeline.LogError("current implementation can only BEV align two sitedrives");
                return 1;
            }

            pipeline.LogInfo("computing birds eye view alignment for site drives {0} and {1}, {2} observations",
                             siteDrives[0], siteDrives[1], observations.Count());

            RenderBEVs();

            DetectFeatures();

            return 0;
        }

        private void BuildWedgeMeshes()
        {
            double startSec = UTCTime.Now();
            int no = observations.Count(), np = 0, nc = 0;
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

        private double ParsePercent(string val, double total)
        {
            if (val.EndsWith("%"))
            {
                return double.Parse(val.Substring(0, val.Length - 1)) * 0.01 * total;
            }
            else
            {
                return double.Parse(val);
            }
        }

        private void RenderBEVs()
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("rendering {0} birds eye views...", siteDrives.Length);

            //TODO HACK - migrate to use pipeline database and storage if we keep this code
            string cacheImageExt = ".tif";
            string cacheMaskExt = ".png";
            if (!options.RedoBEVs)
            {
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
                            var maskUrl =
                                url.Substring(0, url.Length - file.Length) + parts[0] + "_mask" + cacheMaskExt;
                            if (!Array.Exists(available, u => u == maskUrl))
                            {
                                continue;
                            }
                            var bev = pipeline.LoadImage(url);
                            bev.UnionMask(pipeline.LoadImage(maskUrl), new float[] { 1 });
                            bevs[siteDrive] = bev;
                            var origin = new Vector2(int.Parse(parts[parts.Length - 3]),
                                                     int.Parse(parts[parts.Length - 2]));
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
                    pipeline.LogInfo("loaded {0} cached birds eye views ({1:F3}s)",
                                     bevs.Count, UTCTime.Now() - startSec);
                    return;
                }
                else if (nc > 0)
                {
                    pipeline.LogWarn("loaded only {0} of {1} birds eye views from cache, must regenerate all",
                                     nc, siteDrives.Length);
                    bevs.Clear();
                    bevOrigins.Clear();
                }
            }

            BuildWedgeMeshes();

            CoreLimitedParallel.ForEach(siteDrives, siteDrive => {

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
                            Meshing.ColorMeshByElevation(mesh);
                            break;
                        }
                    }
                    
                    if (options.WriteDebug)
                    {
                        string imageFilename = null;
                        if (img != null)
                        {
                            imageFilename = siteDrive + imageExt;
                            pipeline.LogInfo("saving merged sitedrive texure {0}", outputPath + imageFilename);
                            PathHelper.EnsureExists(outputPath);
                            img.Save<byte>(outputPath + imageFilename);
                        }
                        string file = outputPath + siteDrive + meshExt;
                        pipeline.LogInfo("saving merged sitedrive mesh {0}", file);
                        PathHelper.EnsureExists(outputPath);
                        mesh.Save(file, imageFilename);
                    }

                    pipeline.LogInfo("generating birds eye view for site drive {0}", siteDrive);
                    pipeline.LogInfo("{0} meters per pixel, sparse block size {1}, valid block ratio {2}, " +
                                     "inpaint {3}, smoothing {4}, decimation {5}",
                                     options.BEVMetersPerPixel, options.BEVSparseBlocksize,
                                     options.BEVMinValidBlockRatio, options.BEVInpaint, options.BEVSmoothing,
                                     options.BEVDecimation);
                    Vector2 origin;
                    bool greyscale = true;
                    bool ccw = false;
                    var bev = Meshing.RenderBirdsEyeView(mesh, img, options.BEVMetersPerPixel, greyscale, out origin,
                                                         ccw, options.BEVSparseBlocksize, options.BEVMinValidBlockRatio,
                                                         options.BEVInpaint, options.BEVSmoothing,
                                                         options.BEVDecimation);
                    pipeline.LogInfo("generated {0}x{1} birds eye view for site drive {2}, origin {3}",
                                     bev.Width, bev.Height, siteDrive, origin);
                    
                    bevs[siteDrive] = bev;
                    bevOrigins[siteDrive] = origin;
                });

            int n = 0;
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;
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
                    pipeline.LogInfo("saving {0}x{1} birds eye view {2}", bev.Width, bev.Height, file);
                    PathHelper.EnsureExists(outputPath);
                    bev.Save<byte>(file);
                }
            }

            //TODO HACK
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
                PathHelper.EnsureExists(outputPath);
                bev.MaskToImage().Save<byte>(outputPath + siteDrive + "_BirdsEyeView_cached_mask" + cacheMaskExt);
            }

            pipeline.LogInfo("generated {0} birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
        }

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

            CoreLimitedParallel.ForEach(siteDrives, siteDrive => {
                    var bev = bevs[siteDrive];
                    pipeline.LogInfo("detecting {0} features in {1}x{2} birds eye view for {3}",
                                     options.DetectorType, bev.Width, bev.Height, siteDrive);
                    pipeline.LogInfo("max features {0}, extra invalid radius {1}, FAST threshold {2}",
                                     options.MaxFeaturesPerImage, options.FeatureExtraInvalidRadius,
                                     options.FASTThreshold);
                    var mask = bev.MaskToImage(valid: 1, invalid: 0);
                    var feat = features[siteDrive] = detector.Detect(bev, mask);
                    pipeline.LogInfo("detected {0} {1} features in birds eye view for {2}", feat.Length,
                                     options.DetectorType, siteDrive);
                    if (options.WriteDebug)
                    {
                        var img = FeatureDetecting.DrawFeatures(bev, mask, feat, siteDrive, stretch: false);
                        string file = outputPath + siteDrive + "_BirdsEyeView_Features" + imageExt;
                        PathHelper.EnsureExists(outputPath);
                        img.Save<byte>(file);
                    }
                });
            detector.DumpHistograms(pipeline);

            pipeline.LogInfo("detected features for {0} birds eye views ({1:F3}s)",
                             features.Count, UTCTime.Now() - startSec);
        }
    }
}
