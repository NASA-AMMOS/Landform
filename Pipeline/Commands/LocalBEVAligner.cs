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
    public enum ColorMode { Tilt, Elevation };

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

        [Option(HelpText = "Max triangle aspect ratio for organized mesh reconstruction", Default = 20)]
        public double MaxTriangleAspect { get; set; }

        [Option(HelpText = "Birds eye view meters per pixel", Default = 0.005)]
        public double BEVMetersPerPixel { get; set; }

        [Option(HelpText = "Birds eye view coloring (Tilt, Elevation}", Default = ColorMode.Tilt)]
        public ColorMode BEVColoring { get; set; }

        [Option(HelpText = "Birds eye view sparse invalidation blocksize", Default = "0.5%")]
        public string BEVSparseBlocksize { get; set; }

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
        private string imageExt;
        private string meshExt;

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

            string outputPath = pipeline.GetLocalDebugFolder(options.DebugOutputFolder,
                                                             "alignment/AdjustProducts/",
                                                             project.Name);

            if (options.WriteDebug)
            {
                meshExt = MeshSerializers.Instance.CheckFormat(options.MeshFormat, pipeline);
                if (meshExt == null)
                {
                    return 0;
                }
                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing debug data to {0}", outputPath);
            }

            var frameCache = new FrameCache(pipeline, options.ProjectName);
            frameCache.Preload(loadTransforms: true, transformFilter: ft => ft.IsPrior());

            var observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.Preload();

            var observations = Meshing.CollectMeshObservations(frameCache, observationCache, requireNormals: true);

            SiteDrive getSiteDrive(MeshObservations obs)
            {
                var ro = obs.Points as RoverObservation;
                return new SiteDrive(ro.Site, ro.Drive);
            }

            string getCamera(MeshObservations obs)
            {
                return (obs.Points as RoverObservation).Sensor;
            }

            SiteDrive[] siteDrives = (options.OnlyForSiteDrives ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => new SiteDrive(s.Trim()))
                .Cast<SiteDrive>()
                .ToArray();

            if (siteDrives.Length > 0)
            {
                observations = observations.Where(obs => siteDrives.Any(sd => sd == getSiteDrive(obs))).ToList();
            }

            string[] cameras = (options.OnlyForCameras ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            if (cameras.Length > 0)
            {
                observations = observations.Where(obs => cameras.Any(cam => cam == getCamera(obs))).ToList();
            }
                                                  
            int no = observations.Count();

            siteDrives = observations.Select(obs => getSiteDrive(obs)).Distinct().ToArray();
            if (siteDrives.Length != 2)
            {
                pipeline.LogError("current implementatino can only BEV align two sitedrives");
                return 1;
            }

            pipeline.LogInfo("computing birds eye view alignment for site drives {0} and {1}, {2} observations",
                             siteDrives[0], siteDrives[1], no);

            //sitedrive => mesh, mesh, ...
            var mergeInputs = new ConcurrentDictionary<string, ConcurrentBag<Mesh>>();

            double startSec = UTCTime.Now();
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(observations, obs => { 

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("computing products for {0} observations in parallel, completed {1}/{2}",
                                         np, nc, no);
                    }

                    Mesh mesh = Meshing.BuildOrganizedMesh(pipeline, obs, frameCache, "root", true,
                                                           options.DecimateWedgeMeshes);

                    mergeInputs.AddOrUpdate(getSiteDrive(obs).ToString(),
                                            _ => new ConcurrentBag<Mesh>(new [] { mesh }),
                                            (_, bag) => { bag.Add(mesh); return bag; });

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            var bevs = new Dictionary<string, Image>();
            var origins = new Dictionary<string, Vector2>();
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;
            foreach (var siteDrive in mergeInputs.Keys.OrderBy(name => name))
            {
                pipeline.LogInfo("generating merged mesh for site drive {0}", siteDrive);
                var mesh = Mesh.Merge(mergeInputs[siteDrive].Distinct().ToArray());
                double lower = double.PositiveInfinity;
                double upper = double.NegativeInfinity;
                switch (options.BEVColoring)
                {
                    case ColorMode.Tilt:
                    {
                        Meshing.ColorMeshByNormals(mesh, out lower, out upper, Meshing.TiltMode.InvAcos);
                        break;
                    }
                    case ColorMode.Elevation:
                    {
                        Meshing.ColorMeshByElevation(mesh, out lower, out upper, absolute: true);
                        break;
                    }
                }
                min = Math.Min(min, lower);
                max = Math.Max(max, upper);
                if (options.WriteDebug)
                {
                    string file = outputPath + siteDrive + meshExt;
                    pipeline.LogVerbose("saving merged sitedrive mesh {0}", file);
                    PathHelper.EnsureExists(outputPath);
                    mesh.Save(file);
                }

                pipeline.LogInfo("generating birds eye view for site drive {0}", siteDrive);
                Vector2 origin;
                var bev = Meshing.RenderBirdsEyeView(mesh, null, options.BEVMetersPerPixel, true, out origin);
                var bs = (int)ParsePercent(options.BEVSparseBlocksize, Math.Max(bev.Width, bev.Height));
                if (bs > 0)
                {
                    pipeline.LogVerbose("sparse block size {0}, min valid block ratio {1}",
                                        bs, options.BEVMinValidBlockRatio);
                    bev = bev.InvalidateSparseExternalBlocks(bs, options.BEVMinValidBlockRatio);
                    bev = bev.RemoveAllButLargestValidBlob();
                    bev = bev.Trim(out Vector2 ulc);
                    origin -= ulc;
                }
                if (options.BEVInpaint > 0)
                {
                    //inpaint just the interior holes
                    //we do this by first creating a mask by floodfilling exterior invalid regions
                    Image mask = new Image(1, bev.Width, bev.Height);
                    mask.ApplyInPlace(v => 1); //mask=1 means valid
                    Meshing.AddOuterRegionsToMask(bev, mask);
                    bev.Inpaint(options.BEVInpaint); //inpaint all invalid regions
                    bev.UnionMask(mask, new float[] { 0 } ); //re-apply the exterior mask
                }
                if (options.BEVSmoothing > 0)
                {
                    bev = bev.GaussianBoxBlur(options.BEVSmoothing);
                }
                if (options.BEVDecimation > 1)
                {
                    bev = bev.Decimated(options.BEVDecimation);
                }
                pipeline.LogInfo("generated {0}x{1} birds eye view image for site drive {2}, mesh origin at pixel {3}",
                                 bev.Width, bev.Height, siteDrive, origin);
                bevs[siteDrive] = bev;
                origins[siteDrive] = origin;
            }

            pipeline.LogInfo("min global intensity {0}, max {1}", min, max);

            if (options.StretchContrast)
            {
                pipeline.LogInfo("stretching contrast");
                int n = 0;
                double mean = 0;
                foreach (var bev in bevs.Values)
                {
                    foreach (ImageCoordinate ic in bev.Coordinates(includeInvalidValues: false))
                    {
                        mean += bev[0, ic.Row, ic.Col];
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
                double stddev = Math.Sqrt(variance);

                int nStddev = 3;
                min = Math.Max(mean - stddev * nStddev, min);
                max = Math.Min(mean + stddev * nStddev, max);

                pipeline.LogInfo("{0} valid pixels, mean {1}, stddev {2}, stretch range [{3}, {4}] ({5} stddev)",
                                 n, mean, stddev, min, max, nStddev);
            }

            pipeline.LogInfo("normalizing [{0}, {1}] to [0, 1]", min, max);
            foreach (var bev in bevs.Values)
            {
                bev.ScaleValues((float)min, (float)max, 0, 1);
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
                    string file = outputPath + siteDrive + "_BirdsEyeView" + imageExt;
                    pipeline.LogVerbose("saving {0}x{1} merged sitedrive birds eye view {2}",
                                        bev.Width, bev.Height, file);
                    PathHelper.EnsureExists(outputPath);
                    bev.Save<byte>(file);
                }
            }

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

            var features = new Dictionary<string, ImageFeature[]>();
            FeatureDetector detector = new FeatureDetector(pipeline, detectorOpts);
            foreach (var pair in bevs)
            {
                var siteDrive = pair.Key;
                var bev = pair.Value;
                pipeline.LogInfo("detecting {0} features in {1}x{2} birds eye view for {3}",
                                 options.DetectorType, bev.Width, bev.Height, siteDrive);
                var mask = bev.MaskToImage(valid: 1, invalid: 0);
                features[siteDrive] = detector.Detect(bev, mask);
                pipeline.LogInfo("detected {0} {1} features in birds eye view for {2}",
                                 features[siteDrive].Length, options.DetectorType, siteDrive);
                if (options.WriteDebug)
                {
                    var img = FeatureDetecting.DrawFeatures(bev, mask, features[siteDrive], siteDrive);
                    string file = outputPath + siteDrive + "_BirdsEyeView_Features" + imageExt;
                    PathHelper.EnsureExists(outputPath);
                    img.Save<byte>(file);
                }
            }
            detector.DumpHistograms(pipeline);

            double totalSec = UTCTime.Now() - startSec;
            pipeline.LogInfo("aligned {0} observations ({1:F3}s)", no, totalSec);

            return 0;
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
    }
}
