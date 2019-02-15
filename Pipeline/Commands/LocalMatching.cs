using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
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

        [Option(HelpText = "Recreate matches that already exist", Default = false)]
        public bool RedoMatches { get; set; }
    }

    public class LocalMatching : LocalPipeline
    {
        private LocalMatchingOptions options;

        public LocalMatching(LocalMatchingOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var project = Project.Find(this, options.ProjectName);
            if (project == null)
            {
                LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            var sb = new BuildSceneGraph(this, options.ProjectName, new BuildSceneGraph.Options()
                                         {
                                             UseTransformPriors = true,
                                             LoadFeatures = true,
                                             OnlyKeepImagesWithFeatures = true,
                                             OnlyKeepBestImages = true
                                         });
            var scene = sb.BuildTopDown(project.RootFrame);

            var fod = new FrustumOverlapDetector(this, Logger);
            fod.Detect(scene);

            LogInfo("finding feature matches for {0} image pairs", scene.Overlaps.Count);
            double startSec = UTCTime.Now();
            int no = 0, np = 0, nc = 0, ns = 0, ng = 0;
            Parallel.ForEach(scene.Overlaps, pair => {
                var modelUrl = pair.One;
                var dataUrl = pair.Two;
                var modelNode = scene.ObservationUrlToNode[modelUrl];
                var dataNode = scene.ObservationUrlToNode[dataUrl];
                var modelObs = modelNode.GetComponent<NodeObservation>().Observation.Name;
                var dataObs = dataNode.GetComponent<NodeObservation>().Observation.Name;
                Interlocked.Increment(ref no);
                   
                var overlap = Overlap.Find(this, project.Name, modelObs, dataObs);
                if (overlap != null && overlap.Status == Overlap.StatusType.Matched && !options.RedoMatches)
                { 
                    LogInfo("not recomputing features matches for {0}", overlap.CombinedName);
                    Interlocked.Increment(ref ns);
                }
                else 
                {
                    Interlocked.Increment(ref np);
                    LogVerbose("processing {0} image pairs in parallel", np);
                    LogVerbose("computing features matches for {0}", new OverlapName(modelObs,dataObs).CombinedName);
                    var result = ImageMatching.ComputeCorrespondence(this, scene, modelUrl, dataUrl);
                    if (result != null)
                    {
                        Interlocked.Increment(ref nc);
                        SaveDataProduct(project.ProductPath, result, project.Name);
                        ImageMatching.SaveOverlap(this, project.Name, scene, result);
                        Interlocked.Increment(ref ng);
                    }
                    Interlocked.Decrement(ref np);
                }
            });
            LogInfo("processed {0} image pairs in {1:F3} sec, computed {2} correspondences, skipped {3}, good {4}",
                    no, UTCTime.Now() - startSec, nc, ns, ng);

            return 0;
        }
    }
}
