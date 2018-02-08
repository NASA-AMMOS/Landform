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

        public IEnumerable<Overlap> Run(List<Observation> toConsider)
        {
            if (toConsider.Count < 1) yield break;

            // Step 1: construct minimal scene graph containing observations
            AlignmentScene scene = new AlignmentScene();
            string project;
            Frame rootFrame;
            {
                var first = toConsider.First();
                project = first.ProjectName;
                rootFrame = Frame.Find(Pipeline.DynamoDB, project, first.FrameName);
            }

            Memoizer<string, SceneNode> frameToNode = null;
            frameToNode = new Memoizer<string, SceneNode>((fn) =>
            {
                Frame f = Frame.Find(Pipeline.DynamoDB, project, fn);
                if (fn == rootFrame.ParentName)
                {
                    rootFrame = f;
                }
                SceneNode res = new SceneNode(f.Name, frameToNode[f.ParentName].Transform);

                FrameTransform transform = FrameTransform.Find(Pipeline.DynamoDB, f);
                NodeUncertainTransform nut = res.AddComponent<NodeUncertainTransform>();
                nut.UncertainTransform = transform.Transform;
                return res;
            });

            scene.Root = frameToNode[rootFrame.Name];

            Dictionary<ImageRef, Observation> refToObservation = new Dictionary<ImageRef, Observation>();
            foreach (var obs in toConsider)
            {
                var node = frameToNode[obs.FrameName];
                var imgRef = new ObservationImageRef(obs);
                node.AddComponent<NodeImageReference>().Reference = imgRef;
                refToObservation[imgRef] = obs;
            }

            // Step 2: do the thing
            FrustumOverlapDetector fod = new FrustumOverlapDetector(Pipeline);
            fod.Detect(scene);

            foreach (var overlap in scene.Context.Overlaps)
            {
                var one = refToObservation[overlap.One];
                var two = refToObservation[overlap.Two];
                var steve = Overlap.Create(Pipeline.DynamoDB, one.Name, two.Name, one.ProjectName);
                if (steve != null) yield return steve;
            }
        }
    }
}
