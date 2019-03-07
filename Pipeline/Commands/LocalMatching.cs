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

        [Option(HelpText = "Write match meshes (in root frame using transform priors) for debugging", Default = true)]
        public bool WriteMatchMeshes { get; set; }

        [Option(HelpText = "Output directory for debug products, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        [Option(HelpText = "Debug image format, e.g. png, jpg, help for list", Default = "png")]
        public string ImageFormat { get; set; }

        [Option(HelpText = "Debug mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public string MeshFormat { get; set; }

        [Option(HelpText = "Histogram bucket size", Default = 10)]
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

    public class LocalMatching
    {
        private LocalMatchingOptions options;
        private PipelineCore pipeline;
        private string dbgDir;
        private string imageExt;
        private string meshExt;

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

            dbgDir = pipeline.GetLocalDebugFolder(options.OutputFolder, "alignment/MatchingProducts", project.Name);

            if (options.WriteMatchImages)
            {
                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} match images to {1}", imageExt, dbgDir);
            }

            if (options.WriteMatchMeshes)
            {
                meshExt = MeshSerializers.Instance.CheckFormat(options.MeshFormat, pipeline);
                if (meshExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} match meshes to {1}", meshExt, dbgDir);
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
                        if ((options.WriteMatchImages || options.WriteMatchMeshes || options.TallyExisting) &&
                            overlap.MatchGuid != Guid.Empty)
                        {
                            var product =
                                pipeline.GetDataProduct<ComputedCorrespondence>(project.ProductPath, overlap.MatchGuid);

                            if (options.WriteMatchImages)
                            {
                                WriteMatchImage(product, scene, modelObs, dataObs);
                            }
                            if (options.WriteMatchMeshes)
                            {
                                WriteMatchMesh(product, scene, modelObs, dataObs);
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
                    if (!options.NoSave)
                    {
                        pipeline.SaveDataProduct(project.ProductPath, result, project.Name);
                        guid = result.Guid;
                    }
                    AddToHistogram(result, histogram);
                }
                else
                {
                    rejectionTallies.AddOrUpdate(rejectionReason, _ => 1, (_, count) => count + 1);
                }

                if (!options.NoSave && ImageMatching.SaveOverlap(pipeline, project.Name, guid, modelObs.Name, dataObs.Name))
                {
                    Interlocked.Increment(ref ns);
                }

                if (options.WriteMatchImages && result != null)
                {
                    WriteMatchImage(result, scene, modelObs, dataObs);
                }

                if (options.WriteMatchMeshes && result != null)
                {
                    WriteMatchMesh(result, scene, modelObs, dataObs);
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

        private void WriteMatchImage(ComputedCorrespondence product, AlignmentScene scene,
                                     Observation modelObs, Observation dataObs)
        {
            var modelImg = pipeline.LoadImage(modelObs.Url);
            var dataImg = pipeline.LoadImage(dataObs.Url);
            var modelFeat = scene.DetectedFeatures[modelObs.Url];
            var dataFeat = scene.DetectedFeatures[dataObs.Url];
            var d2m = product.Correspondence.DataToModel;
            var modelFile = StringHelper.GetLastUrlPathSegment(modelObs.Url);
            var dataFile = StringHelper.GetLastUrlPathSegment(dataObs.Url);
            var ret = ImageMatching.DrawMatches(modelImg, dataImg, modelFeat, dataFeat, d2m, modelFile, dataFile);
            PathHelper.EnsureExists(dbgDir);
            ret.Save<byte>(string.Format("{0}{1}-{2}-Matches{3}", dbgDir, modelObs.Name, dataObs.Name, imageExt));
        }

        private void WriteMatchMesh(ComputedCorrespondence product, AlignmentScene scene,
                                    Observation modelObs, Observation dataObs)
        {
            var modelNode = scene.ObservationUrlToNode[modelObs.Url];
            var dataNode = scene.ObservationUrlToNode[dataObs.Url];
            var modelCam = modelNode.GetComponent<NodeImage>().CameraModel;
            var dataCam = dataNode.GetComponent<NodeImage>().CameraModel;
            var modelFeat = scene.DetectedFeatures[modelObs.Url];
            var dataFeat = scene.DetectedFeatures[dataObs.Url];
            var d2m = product.Correspondence.DataToModel;
            var modelToRoot = modelNode.GetComponent<NodeUncertainTransform>().To(scene.Root).Mean;
            var dataToRoot = dataNode.GetComponent<NodeUncertainTransform>().To(scene.Root).Mean;
            var ret = ImageMatching.MakeMatchMesh(modelCam, dataCam, modelFeat, dataFeat, modelToRoot, dataToRoot, d2m);
            PathHelper.EnsureExists(dbgDir);
            ret.Save(string.Format("{0}{1}-{2}-PriorMatches{3}", dbgDir, modelObs.Name, dataObs.Name, meshExt));
        }
    }
}
