using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Imaging;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline.AlignmentServer;

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
                rootFrame = Frame.Find(pipeline, project, first.FrameName);
            }

            Memoizer<string, SceneNode> frameToNode = null;
            frameToNode = new Memoizer<string, SceneNode>((fn) =>
            {
                Frame f = Frame.Find(pipeline, project, fn);
                NodeTransform parent = null;
                if (f.ParentName != null) parent = frameToNode[f.ParentName].Transform;
                SceneNode res = new SceneNode(f.Name, parent);

                FrameTransform transform = FrameTransform.Find(pipeline, f);
                NodeUncertainTransform nut = res.AddComponent<NodeUncertainTransform>();
                nut.UncertainTransform = transform.Transform;
                return res;
            });

            Dictionary<string, Observation> imgfToObservation = new Dictionary<string, Observation>();
            foreach (var obs in toConsider)
            {
                var node = frameToNode[obs.FrameName];
                node.AddComponent<NodeImageUrl>().Url = obs.Url;
                imgfToObservation[obs.Url] = obs;
            }

            // Find real root
            while (rootFrame.ParentName != null && frameToNode.ContainsKey(rootFrame.ParentName))
            {
                rootFrame = Frame.Find(pipeline, project, rootFrame.ParentName);
            }
            scene.Root = frameToNode[rootFrame.Name];

            // Step 2: do the thing
            FrustumOverlapDetector fod = new FrustumOverlapDetector(pipeline);
            fod.Detect(scene);

            logger.Info("Found overlaps: " + scene.Overlaps.Count);

            foreach(var overlap in scene.Overlaps)
            {
                yield return Overlap.Create(pipeline, imgfToObservation[overlap.One], imgfToObservation[overlap.Two]);
            }
        }
    }
}
