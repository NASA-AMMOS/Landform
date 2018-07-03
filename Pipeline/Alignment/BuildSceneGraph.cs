using log4net;
using MathNet.Numerics.LinearAlgebra;
using OPS.Alignment;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    public class BuildSceneGraph : PipelineRoutine
    {
        static ILog logger = LogManager.GetLogger(typeof(BuildSceneGraph));

        public BuildSceneGraph(PipelineCore pipeline) : base(pipeline) { }

        public delegate bool IncludeFrameDelegate(Frame frame, SceneNode parent);
        public delegate bool IncludeObservationDelegate(Observation observation, SceneNode parent);
        public delegate UncertainRigidTransform FrameTransformDelegate(Frame frame, SceneNode parent);

        public struct Options
        {
            /// <summary>
            /// Return true if a given frame should be included in the graph.
            /// 
            /// If null, defaults to always true.
            /// </summary>
            public IncludeFrameDelegate IncludeFrame;
            /// <summary>
            /// Return true if a given observation should be included in the graph.
            /// 
            /// If null, defaults to always true.
            /// </summary>
            public IncludeObservationDelegate IncludeObservation;
            /// <summary>
            /// Return the uncertain transform associated with a frame.
            /// 
            /// If null, defaults to values in the FrameTransforms table.
            /// </summary>
            public FrameTransformDelegate GetTransform;
        }

        public UncertainRigidTransform StandardFrameTransform(Frame frame, SceneNode parent)
        {
            var transform = FrameTransform.Find(DynamoDB, frame);
            if (transform == null)
            {
                logger.Error("No transform found for frame " + frame.Name);
                return null;
            }
            return transform.Transform;
        }
        public UncertainRigidTransform CertainFrameTransform(Frame frame, SceneNode parent)
        {
            var transform = FrameTransform.Find(DynamoDB, frame);
            if (transform == null)
            {
                logger.Error("No transform found for frame " + frame.Name);
                return null;
            }
            return new UncertainRigidTransform(transform.Transform.Mean, CreateMatrix.Dense<double>(6,6));
        }

        public AlignmentScene Build(Frame root, Options options)
        {
            // Initialize default options
            if (options.IncludeFrame == null)
            {
                options.IncludeFrame = (f, p) => true;
            }
            if (options.IncludeObservation == null)
            {
                options.IncludeObservation = (f, p) => true;
            }
            if (options.GetTransform == null)
            {
                options.GetTransform = StandardFrameTransform;
            }

            AlignmentScene scene = new AlignmentScene();

            Action<Observation, SceneNode> addObservation = (obs, node) =>
            {
                if (obs.FeaturesGuid == null || obs.FeaturesGuid == Guid.Empty) return;
                var feat = Get<DetectedFeatures>(obs.ProjectName, obs.FeaturesGuid);
                var imgRef = new ObservationImageRef(obs);

                scene.DetectedFeatures[imgRef] = feat.Features;
                scene.ImageToNode[imgRef] = node;
                node.AddComponent<NodeImageReference>().Reference = imgRef;
            };

            List<Observation> observations = new List<Observation>();
            HashSet<string> observationNames = new HashSet<string>();
            Func<Frame, SceneNode, SceneNode> spawn = null;
            spawn = (frame, parent) =>
            {
                var res = new SceneNode(frame.Name, parent?.Transform);
                var ut = options.GetTransform(frame, parent);
                if (ut == null) return null;
                res.GetOrAddComponent<NodeUncertainTransform>().UncertainTransform = ut;

                // Add any observations to the node
                var obs = Observation.Find(DynamoDB, frame).Where(o => options.IncludeObservation(o, res)).ToArray();
                observations.AddRange(obs);
                foreach (var o in obs)
                {
                    observationNames.Add(o.Name);
                }

                if (obs.Length == 1)
                {
                    addObservation(obs[0], res);
                }
                else if (obs.Length > 1)
                {
                    foreach (var o in obs)
                    {
                        var obsNode = new SceneNode(o.Name, res.Transform);
                        obsNode.Transform.Matrix = Matrix.Identity;
                        addObservation(o, obsNode);
                    }
                }
                
                // Add child frames
                foreach (var childFrame in frame.GetChildren(DynamoDB))
                {
                    if (!options.IncludeFrame(childFrame, res))
                    {
                        continue;
                    }

                    spawn(childFrame, res);
                }

                return res;
            };

            scene.Root = spawn(root, null);

            // Add all overlaps and computed correspondences
            foreach (var obs in observations)
            {
                foreach (var overlap in Overlap.Find(DynamoDB, obs))
                {
                    var o1 = overlap.ObservationNameOne;
                    var o2 = overlap.ObservationNameTwo;
                    // Skip any overlaps that involve an observation we didn't ingest
                    if (!observationNames.Contains(o1) || !observationNames.Contains(o2))
                    {
                        continue;
                    }

                    var imgOne = new ObservationImageRef(Observation.Find(DynamoDB, overlap.ProjectName, o1));
                    var imgTwo = new ObservationImageRef(Observation.Find(DynamoDB, overlap.ProjectName, o2));
                    var pair = new UnorderedImagePair(imgOne, imgTwo);
                    scene.Overlaps.Add(pair);

                    if (overlap.MatchGuid != null && overlap.MatchGuid != Guid.Empty)
                    {
                        var match = Get<ComputedCorrespondence>(overlap.ProjectName, overlap.MatchGuid);
                        if (match != null)
                        {
                            scene.Correspondences[pair] = match.Correspondence;
                        }
                    }
                }
            }

            return scene;
        }
    }
}
