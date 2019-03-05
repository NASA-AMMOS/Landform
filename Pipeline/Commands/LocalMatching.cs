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
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    [Verb("local-matching", HelpText = "match features in overlapping images")]
    public class LocalMatchingOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Recreate frustum overlaps that already exist", Default = false)]
        public bool RedoOverlaps { get; set; }

        [Option(HelpText = "Recreate matches that already exist", Default = false)]
        public bool RedoMatches { get; set; }

        [Option(HelpText = "Only keep image correspondences with at least this many matches", Default = 20)]
        public int MinMatchesPerPair { get; set; }

        [Option(HelpText = "Find feature matches for images within the same site drive", Default = false)]
        public bool MatchWithinSiteDrives { get; set; }

        [Option(HelpText = "Write match images for debugging", Default = false)]
        public bool WriteMatchImages { get; set; }

        [Option(HelpText = "Output directory for debug images, or omit to save to project storage", Default = null)]
        public string ImageOutputFolder { get; set; }

        [Option(HelpText = "Debug image format, e.g. png, jpg, help for list", Default = "png")]
        public string ImageFormat { get; set; }

        [Option(HelpText = "Histogram bucket size", Default = 10)]
        public int HistogramBucketSize { get; set; }

        [Option(HelpText = "Include existing products in histogram", Default = false)]
        public bool TallyExisting { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }
    }

    public class LocalMatching
    {
        private LocalMatchingOptions options;
        private PipelineCore pipeline;
        private string imageDir;
        private string imageExt;

        public LocalMatching(LocalMatchingOptions options)
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
                pipeline.GetLocalDebugFolder(options.ImageOutputFolder, "alignment/MatchingProducts", project.Name);

            if (options.WriteMatchImages)
            {
                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} match images to {1}", imageExt, imageDir);
            }

            var scene = ImageMatching.BuildSceneAndDetectOverlaps(pipeline, project, loadFeatures: true,
                                                                  redoOverlaps: options.RedoOverlaps,
                                                                  onlyCrossSite: !options.MatchWithinSiteDrives);
            int no = scene.Overlaps.Count;

            pipeline.LogInfo("finding feature matches for {0} image pairs", no);

            var histogram = new ConcurrentDictionary<int, int>();
            var rejectionTallies = new ConcurrentDictionary<string, int>();
            double startSec = UTCTime.Now();
            int nc = 0, np = 0, ne = 0, nr = 0, ns = 0;
            CoreLimitedParallel.ForEach(scene.Overlaps, pair => {

                string pairName = pair.ToStringShort();
                var modelUrl = pair.One;
                var dataUrl = pair.Two;
                var modelNode = scene.ObservationUrlToNode[modelUrl];
                var dataNode = scene.ObservationUrlToNode[dataUrl];
                var modelObs = modelNode.GetComponent<NodeObservation>().Observation;
                var dataObs = dataNode.GetComponent<NodeObservation>().Observation;

                if (!options.RedoMatches)
                {
                    //only hit the database if we need to
                    var overlap = Overlap.Find(pipeline, project.Name, modelObs.Name, dataObs.Name);
                    if (overlap != null)
                    {
                        Interlocked.Increment(ref ne);
                        pipeline.LogVerbose("not recomputing feature matches for image pair {0}", pairName);
                        if ((options.WriteMatchImages || options.TallyExisting) && overlap.MatchGuid != Guid.Empty)
                        {
                            var product =
                                pipeline.GetDataProduct<ComputedCorrespondence>(project.ProductPath, overlap.MatchGuid);

                            if (options.WriteMatchImages)
                            {
                                WriteMatchImage(product, project, modelObs.Name, dataObs.Name);
                            }
                            if (options.TallyExisting)
                            {
                                AddToHistogram(product, histogram);
                            }
                        }
                        return;
                    }
                }

                Interlocked.Increment(ref np);
                if (!options.NoProgress)

                {
                    pipeline.LogInfo("processing {0} image pairs in parallel, completed {1}/{2}", np, nc, no);
                }
                string rejectionReason;
                var result = ImageMatching.ComputeCorrespondence(pipeline, scene, modelUrl, dataUrl,
                                                                 out rejectionReason, options.MinMatchesPerPair);
                var guid = Guid.Empty;
                if (result != null)
                {
                    Interlocked.Increment(ref nr);
                    pipeline.SaveDataProduct(project.ProductPath, result, project.Name);
                    guid = result.Guid;
                    AddToHistogram(result, histogram);
                }
                else
                {
                    rejectionTallies.AddOrUpdate(rejectionReason, _ => 1, (_, count) => count + 1);
                }

                if (ImageMatching.SaveOverlap(pipeline, project.Name, guid, modelObs.Name, dataObs.Name))
                {
                    Interlocked.Increment(ref ns);
                }

                if (options.WriteMatchImages && result != null)
                {
                    WriteMatchImage(result, project, modelObs.Name, dataObs.Name);
                }

                Interlocked.Decrement(ref np);
                Interlocked.Increment(ref nc);
            });

            foreach (var bucket in histogram.Keys.OrderBy(n => n))
            {
                pipeline.LogInfo("{0} correspondences with {1} to {2} matches", histogram[bucket],
                                 bucket * options.HistogramBucketSize, (bucket + 1) * options.HistogramBucketSize - 1);
            }
            foreach (var reason in rejectionTallies.Keys.OrderBy(r => r))
            {
                pipeline.LogInfo("rejected {0} image pairs because {1}", rejectionTallies[reason], reason);
            }

            pipeline.LogInfo("processed {0} image pairs ({1:F3}s), computed {2} correspondences ({3} existing), " +
                             "saved {4}", nc, UTCTime.Now() - startSec, nr, ne, ns);

            return 0;
        }

        private void AddToHistogram(ComputedCorrespondence product, ConcurrentDictionary<int, int> histogram)
        {
            int bucket = product.Correspondence.Count / options.HistogramBucketSize;
            histogram.AddOrUpdate(bucket, _ => 1, (_, count) => count + 1);
        }

        private void WriteMatchImage(ComputedCorrespondence product, Project project, string modelObsName,
                                     string dataObsName)
        {
            var modelFeatGuid = product.ModelFeaturesGuid;
            var dataFeatGuid = product.DataFeaturesGuid;
            var d2m = product.Correspondence.DataToModel;
            var modelUrl = product.Correspondence.ModelImageUrl;
            var dataUrl = product.Correspondence.DataImageUrl;
            var modelImg = pipeline.LoadImage(modelUrl);
            var dataImg = pipeline.LoadImage(dataUrl);
            var modelName = StringHelper.GetLastUrlPathSegment(modelUrl);
            var dataName = StringHelper.GetLastUrlPathSegment(dataUrl);
            var modelFeat = pipeline.GetDataProduct<DetectedFeatures>(project.ProductPath, modelFeatGuid, project.Name);
            var dataFeat = pipeline.GetDataProduct<DetectedFeatures>(project.ProductPath, dataFeatGuid, project.Name);
            var img = ImageMatching.DrawMatches(modelImg, dataImg, modelFeat.Features, dataFeat.Features, d2m,
                                                modelName, dataName);
            PathHelper.EnsureExists(imageDir);
            img.Save<byte>(string.Format("{0}{1}-{2}-Matches{3}", imageDir, modelObsName, dataObsName, imageExt));
        }
    }
}
