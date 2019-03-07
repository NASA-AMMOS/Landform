using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    [Verb("local-features", HelpText = "create image features locally")]
    public class LocalFeaturesOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Detector type", Default = DetectorType.ASIFT)]
        public DetectorType DetectorType { get; set; }

        [Option(HelpText = "Maximum number of features per image", Default = FeatureDetector.DEF_MAX_FEATURES)]
        public int MaxFeaturesPerImage { get; set; }

        [Option(HelpText = "Minimum feature size to keep", Default = FeatureDetector.DEF_MIN_FEATURE_SIZE)]
        public double MinFeatureSize { get; set; }

        [Option(HelpText = "Recreate features that already exist", Default = false)]
        public bool RedoFeatures { get; set; }

        [Option(HelpText = "Don't try to add range data to features", Default = false)]
        public bool NoRange { get; set; }

        [Option(HelpText = "Write feature images for debugging", Default = false)]
        public bool WriteFeatureImages { get; set; }

        [Option(HelpText = "Output directory for debug images, or omit to save to project storage", Default = null)]
        public string ImageOutputFolder { get; set; }

        [Option(HelpText = "Debug image format, e.g. png, jpg, help for list", Default = "png")]
        public string ImageFormat { get; set; }

        [Option(HelpText = "Histogram bucket size", Default = 50)]
        public int HistogramBucketSize { get; set; }

        [Option(HelpText = "Include existing products in histogram", Default = false)]
        public bool TallyExisting { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }

        [Option(HelpText = "Disable saving results to database", Default = false)]
        public bool NoSave { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }
    }

    public class LocalFeatures
    {
        private LocalFeaturesOptions options;
        private PipelineCore pipeline;
        private string imageDir;
        private string imageExt;

        public LocalFeatures(LocalFeaturesOptions options)
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

            imageDir =
                pipeline.GetLocalDebugFolder(options.ImageOutputFolder, "alignment/FeatureProducts", project.Name);

            if (options.WriteFeatureImages)
            {
                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} feature images to {1}", imageExt, imageDir);
            }

            var frameCache = new FrameCache(pipeline, options.ProjectName);
            frameCache.Preload(loadTransforms: false);

            var observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.Preload(obs => obs.UseForReconstruction);

            var imageType = ObservationType.Image.ToString();
            var maskType = ObservationType.RoverMask.ToString();
            var pointsType = ObservationType.Points.ToString();

            var obsForFrame = new Dictionary<string, List<Observation>>();
            foreach (var obs in observationCache.GetAllObservations())
            {
                if (!obsForFrame.ContainsKey(obs.FrameName))
                {
                    obsForFrame[obs.FrameName] = new List<Observation>();
                }
                obsForFrame[obs.FrameName].Add(obs);
            }

            int no = obsForFrame.Values.Count(obsGroup => obsGroup.Any(obs => obs.ObservationType == imageType));
            pipeline.LogInfo("computing {0} features for {1} reconstruction images", options.DetectorType, no);

            FeatureDetector detector = new FeatureDetector(pipeline, options.DetectorType, options.MaxFeaturesPerImage,
                                                           options.MinFeatureSize);

            var histogram = new ConcurrentDictionary<int, int>();
            double startSec = UTCTime.Now();
            int nc = 0, ne = 0, nf = 0, np = 0, wr = 0, tf = 0, trf = 0;
            CoreLimitedParallel.ForEach(obsForFrame.Values, obsGroup => { 

                    var observations = obsGroup
                    .Cast<RoverObservation>()
                    .ToList();
                    observations.Sort(MSLProject.RoverObservationComparison);
                    
                    var imageObs = observations.Find(obs => obs.ObservationType == imageType);
                    if (imageObs != null)
                    {
                        var maskObs = observations.Find(obs => obs.ObservationType == maskType &&
                                                        obs.Width == imageObs.Width && obs.Height == imageObs.Height);
                        var maskUrl = maskObs != null ? maskObs.Url : null;

                        if (imageObs.FeaturesGuid != null && imageObs.FeaturesGuid != Guid.Empty)
                        {
                            Interlocked.Increment(ref ne);
                            if (!options.RedoFeatures)
                            {
                                pipeline.LogVerbose("not recomputing features for observation {0}", imageObs.Name);
                                if (options.WriteFeatureImages || options.TallyExisting)
                                {
                                    var product =
                                        pipeline.GetDataProduct<DetectedFeatures>(project.ProductPath,
                                                                                  imageObs.FeaturesGuid, project.Name);
                                    if (options.WriteFeatureImages)
                                    {
                                        WriteFeatureImage(product, maskUrl, imageObs);
                                    }
                                    if (options.TallyExisting)
                                    {
                                        AddToHistogram(product, histogram);
                                    }
                                }
                                return;
                            }
                            else
                            {
                                pipeline.LogVerbose("recomputing features for observation {0}", imageObs.Name);
                            }
                        }
                        else
                        {
                            pipeline.LogVerbose("computing features for observation {0}", imageObs.Name);
                        }
                        
                        Interlocked.Increment(ref nf);
                        Interlocked.Increment(ref np);
                        
                        if (!options.NoProgress)
                        {
                            pipeline.LogInfo("computing features for {0} images in parallel, completed {1}/{2}",
                                             np, nc, no);
                        }
                        
                        var result = detector.Detect(imageObs.Url, maskUrl, project.Name, project.ProductPath);
                        if (result != null)
                        {
                            Interlocked.Add(ref tf, result.Features.Length);
                            var pointsObs =
                                observations.Find(obs => obs.ObservationType == pointsType &&
                                                  obs.Width == imageObs.Width && obs.Height == imageObs.Height);
                            if (!options.NoRange && pointsObs != null)
                            {
                                Interlocked.Increment(ref wr);
                                int rf = FeatureDetecting.AddRange(result.Features,
                                                                   pipeline.LoadImage(imageObs.Url),
                                                                   pipeline.LoadImage(pointsObs.Url));
                                Interlocked.Add(ref trf, rf);
                            }
                            imageObs.FeaturesGuid = result.Guid;
                            if (!options.NoSave)
                            {
                                pipeline.SaveDataProduct(project.ProductPath, result, project.Name);
                                imageObs.Save(pipeline);
                            }
                            AddToHistogram(result, histogram);
                        }
                        
                        if (options.WriteFeatureImages)
                        {
                            WriteFeatureImage(result, maskUrl, imageObs);
                        }

                        Interlocked.Decrement(ref np);
                        Interlocked.Increment(ref nc);
                    }
                });
            double totalSec = UTCTime.Now() - startSec;
            
            foreach (var bucket in histogram.Keys.OrderBy(n => n))
            {
                pipeline.LogInfo("{0} images with {1} to {2} features", histogram[bucket],
                                 bucket * options.HistogramBucketSize, (bucket + 1) * options.HistogramBucketSize - 1);
            }
            pipeline.LogInfo("processed {0} reconstruction images ({1:F3}s), computed features for {2} images, " +
                             "{3} with range, {4} existing", nc, totalSec, nf, wr, ne);
            pipeline.LogInfo("total {0} features, {1} with range", tf, trf);
            
            return 0;
        }

        private void AddToHistogram(DetectedFeatures product, ConcurrentDictionary<int, int> histogram)
        {
            int bucket = product.Features.Length / options.HistogramBucketSize;
            histogram.AddOrUpdate(bucket, _ => 1, (_, count) => count + 1);
        }

        private void WriteFeatureImage(DetectedFeatures product, string maskUrl, Observation imageObs)
        {
            //avoid using product.ImageUrl here for legacy compat reasons
            //fortunately we can use imageObs.Url instead
            var img = new Image(pipeline.LoadImage(imageObs.Url)); //don't mutate original image
            var mask = FeatureDetecting.MakeMask(pipeline, maskUrl, img, imageObs.Name);
            img = FeatureDetecting.DrawFeatures(img, mask, product.Features,
                                                StringHelper.GetLastUrlPathSegment(imageObs.Url));
            PathHelper.EnsureExists(imageDir);
            img.Save<byte>(string.Format("{0}{1}-Features{2}", imageDir, imageObs.Name, imageExt));
        }
    }
}
