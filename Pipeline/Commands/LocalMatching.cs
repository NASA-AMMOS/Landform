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

        [Option(HelpText = "Recreate frustum overlaps that already exist", Default = false)]
        public bool RedoOverlaps { get; set; }

        [Option(HelpText = "Recreate matches that already exist", Default = false)]
        public bool RedoMatches { get; set; }

        [Option(HelpText = "Find feature matches for images within the same site drive", Default = false)]
        public bool MatchWithinSiteDrives { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }
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

            var scene = ImageMatching.BuildSceneAndDetectOverlaps(this, project, loadFeatures: true,
                                                                  redoOverlaps: options.RedoOverlaps,
                                                                  onlyCrossSite: !options.MatchWithinSiteDrives);
            int no = scene.Overlaps.Count;

            LogInfo("finding feature matches for {0} image pairs", no);

            double startSec = UTCTime.Now();
            int nc = 0, np = 0, ne = 0, nr = 0, ns = 0;
            Parallel.ForEach(scene.Overlaps, pair => {
                string pairName = pair.ToStringShort();
                var modelUrl = pair.One;
                var dataUrl = pair.Two;
                var modelNode = scene.ObservationUrlToNode[modelUrl];
                var dataNode = scene.ObservationUrlToNode[dataUrl];
                var modelObs = modelNode.GetComponent<NodeObservation>().Observation.Name;
                var dataObs = dataNode.GetComponent<NodeObservation>().Observation.Name;
                if (!options.RedoMatches)
                {
                    //only hit the database if we need to
                    var overlap = Overlap.Find(this, project.Name, modelObs, dataObs);
                    if (overlap != null)
                    {
                        Interlocked.Increment(ref ne);
                        LogVerbose("not recomputing feature matches for image pair {0}", pairName);
                        return;
                    }
                }
                Interlocked.Increment(ref np);
                if (!options.NoProgress)
                {
                    LogInfo("processing {0} image pairs in parallel, completed {1}/{2}", np, nc, no);
                }
                LogVerbose("computing features matches for image pair {0}", pairName);
                var result = ImageMatching.ComputeCorrespondence(this, scene, modelUrl, dataUrl);
                var guid = Guid.Empty;
                if (result != null)
                {
                    Interlocked.Increment(ref nr);
                    SaveDataProduct(project.ProductPath, result, project.Name);
                    guid = result.Guid;
                }
                if (ImageMatching.SaveOverlap(this, project.Name, guid, modelObs, dataObs))
                {
                    Interlocked.Increment(ref ns);
                }
                Interlocked.Decrement(ref np);
                Interlocked.Increment(ref nc);
            });

            LogInfo("processed {0} image pairs ({1:F3}s), computed {2} correspondences ({3} existing), saved {4}",
                    nc, UTCTime.Now() - startSec, nr, ne, ns);

            return 0;
        }
    }
}
