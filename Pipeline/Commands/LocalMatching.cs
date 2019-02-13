using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Pipeline.AlignmentServer;
using OPS.Geometry;

namespace OPS.Pipeline
{
    [Verb("local-matching", HelpText = "match features in overlapping images")]
    public class LocalMatchingOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "project name", Default = null)]
        public string ProjectName { get; set; }
    }

    public class LocalMatching : LocalPipeline
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(LocalMatching));

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

            var fod = new FrustumOverlapDetector(this);
            var sb = new BuildSceneGraph(this);

            var scene = sb.Build(Frame.Find(this, options.ProjectName, "root"), new BuildSceneGraph.Options()
            {
                GetTransform = sb.StandardFrameTransform,
                IncludeObservation = (obs, _) => obs.UseForReconstruction
            });
            fod.Detect(scene);

            foreach (var pair in scene.Overlaps)
            {
                string modelUrl = pair.One;
                string dataUrl = pair.Two;

                SceneNode modelNode = scene.ImageToNode[modelUrl];
                SceneNode dataNode = scene.ImageToNode[dataUrl];

                RoverObservation modelObs = modelNode.GetComponent<NodeObservation>().observation as RoverObservation;
                RoverObservation dataObs = dataNode.GetComponent<NodeObservation>().observation as RoverObservation;

                ComputedCorrespondence result = MatchImages.DoCorrespondence(this, project,
                        modelUrl, dataUrl, modelObs.FeaturesGuid, dataObs.FeaturesGuid, modelObs.FrameName, dataObs.FrameName);

                var dbOverlap = Overlap.Create(this, modelObs, dataObs);
                if (dbOverlap != null)
                {
                    if (result != null && result.Correspondence != null)
                    {
                        dbOverlap.Status = Overlap.StatusType.Matched;
                        dbOverlap.MatchGuid = result.Guid;
                    }
                    else
                    {
                        dbOverlap.Status = Overlap.StatusType.Rejected;
                        dbOverlap.MatchGuid = Guid.Empty;
                    }
                    dbOverlap.TrySave(this);
                }
            }

            return 0;
        }
    }
}
