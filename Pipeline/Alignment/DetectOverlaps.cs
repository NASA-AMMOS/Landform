using log4net;
using OPS.Alignment;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Plumbing;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    class DetectOverlaps : PipelineRoutine
    {
        public DetectOverlaps(PipelineCore pipeline) : base(pipeline)
        {

        }

        public IEnumerable<Overlap> Run(List<Observation> toConsider, ILog logger = null)
        {
            if (toConsider.Count < 1)
            {
                yield break;
            }
            // Step 1: construct minimal scene graph containing observations
            AlignmentScene scene = new AlignmentScene();
            string project;
            Frame rootFrame;
            {
                var first = toConsider.First();
                project = first.ProjectName;
                rootFrame = Frame.Find(Pipeline.DynamoContext, project, first.FrameName);
            }

            Memoizer<string, SceneNode> frameToNode = null;
            frameToNode = new Memoizer<string, SceneNode>((fn) =>
            {
                Frame f = Frame.Find(Pipeline.DynamoContext, project, fn);
                NodeTransform parent = null;
                if (f.ParentName != null) parent = frameToNode[f.ParentName].Transform;
                SceneNode res = new SceneNode(f.Name, parent);

                FrameTransform transform = FrameTransform.Find(Pipeline.DynamoContext, f);
                NodeUncertainTransform nut = res.AddComponent<NodeUncertainTransform>();
                nut.UncertainTransform = transform.Transform;
                return res;
            });

            Dictionary<ImageRef, Observation> refToObservation = new Dictionary<ImageRef, Observation>();
            foreach (var obs in toConsider)
            {
                var node = frameToNode[obs.FrameName];
                var imgRef = new ObservationImageRef(obs);
                node.AddComponent<NodeImageReference>().Reference = imgRef;
                refToObservation[imgRef] = obs;
            }

            // Find real root
            while (rootFrame.ParentName != null && frameToNode.ContainsKey(rootFrame.ParentName))
            {
                rootFrame = Frame.Find(DynamoDB, project, rootFrame.ParentName);
            }
            scene.Root = frameToNode[rootFrame.Name];

            // Step 2: do the thing
            FrustumOverlapDetector fod = new FrustumOverlapDetector(Pipeline);
            fod.Detect(scene);

            logger.Info("Found overlaps: " + scene.Overlaps.Count);

            foreach(var overlap in scene.Overlaps)
            {
                var one = refToObservation[overlap.One];
                var two = refToObservation[overlap.Two];
                var steve = ThroughputManager.Run(() => Overlap.Create(Pipeline.DynamoContext, one, two));
                if(steve != null)
                {
                    yield return steve;
                }
            }
        }
    }
}
