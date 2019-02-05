using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using MathNet.Numerics.LinearAlgebra;
using OPS.Util;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline.AlignmentServer;

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

            /// <summary>
            /// if true, an observation requires features to have been detected to have its image URL
            /// added to the scene graph nodes
            /// </summary>
            public bool? RequireFeaturesForImageURLs;
        }

        public UncertainRigidTransform StandardFrameTransform(Frame frame, SceneNode parent)
        {
            var transform = FrameTransform.Find(pipeline, frame);
            if (transform == null)
            {
                logger.Error("No transform found for frame " + frame.Name);
                return null;
            }
            return transform.Transform;
        }
        public UncertainRigidTransform CertainFrameTransform(Frame frame, SceneNode parent)
        {
            var transform = FrameTransform.Find(pipeline, frame);
            if (transform == null)
            {
                logger.Error("No transform found for frame " + frame.Name);
                return null;
            }
            return new UncertainRigidTransform(transform.Transform.Mean, CreateMatrix.Dense<double>(6,6));
        }

        static bool ValidGuid(Guid g)
        {
            return g != null && g != Guid.Empty;
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
            if (options.RequireFeaturesForImageURLs == null)
            {
                options.RequireFeaturesForImageURLs = true;
            }

            AlignmentScene scene = new AlignmentScene();

            var project = Project.Find(pipeline, root.ProjectName);

            Action<Observation, SceneNode> addObservation = (obs, node) =>
            {
                var imgUrl = obs.Url;

                DetectedFeatures feat = null;

                if (ValidGuid(obs.FeaturesGuid))
                {
                    feat = pipeline.GetDataProduct<DetectedFeatures>(project.ProductPath, obs.FeaturesGuid, project.Name);
                    scene.DetectedFeatures[imgUrl] = feat.Features;
                }
                else if (options.RequireFeaturesForImageURLs.Value)
                {
                    return;
                }

                scene.ImageToNode[imgUrl] = node;
                node.AddComponent<NodeImageUrl>().Url = imgUrl;
                node.AddComponent<NodeObservation>().observation = obs;
            };

            List<Observation> observations = new List<Observation>();
            HashSet<string> observationNames = new HashSet<string>();
            Func<Frame, SceneNode, SceneNode> spawn = null;

            {
                FrameCache frameCache = new FrameCache(pipeline, root.ProjectName);

                spawn = (frame, parent) =>
                {
                    var res = new SceneNode(frame.Name, parent?.Transform);
                    var ut = options.GetTransform(frame, parent);
                    if (ut == null) return null;

                    //add transforms for parents so they bet bundle adjusted
                    res.GetOrAddComponent<NodeUncertainTransform>().UncertainTransform = ut;

                    // Add any observations to the node
                    var obs = Observation.Find(pipeline, frame).Where(o => options.IncludeObservation(o, res)).ToArray();
                    observations.AddRange(obs);
                    foreach (var o in obs)
                    {
                        observationNames.Add(o.Name);
                    }

                    if (obs.Length == 1)
                    {
                        res.GetOrAddComponent<NodeUncertainTransform>().UncertainTransform = ut;
                        addObservation(obs[0], res);
                    }
                    else if (obs.Length > 1)
                    {
                        foreach (var o in obs)
                        {
                            var obsNode = new SceneNode(o.Name, res.Transform);
                            obsNode.Transform.Matrix = Matrix.Identity;
                            obsNode.GetOrAddComponent<NodeUncertainTransform>().UncertainTransform = ut;
                            addObservation(o, obsNode);
                        }
                    }

                    // Add child frames
                    var frames = frame.GetChildren(pipeline).ToList();
                    foreach (var childFrame in frames)
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
            }

            // Add all overlaps and computed correspondences
            foreach (var obs in observations)
            {
                foreach (var overlap in Overlap.Find(pipeline, obs))
                {
                    var n1 = overlap.ObservationNameOne;
                    var n2 = overlap.ObservationNameTwo;
                    // Skip any overlaps that involve an observation we didn't ingest
                    if (!observationNames.Contains(n1) || !observationNames.Contains(n2))
                    {
                        continue;
                    }

                    var o1 = Observation.Find(pipeline, overlap.ProjectName, n1);
                    var o2 = Observation.Find(pipeline, overlap.ProjectName, n2);
                    var pair = new URLPair(o1.Url, o2.Url);
                    scene.Overlaps.Add(pair);

                    if (ValidGuid(overlap.MatchGuid))
                    {
                        var match = pipeline.GetDataProduct<ComputedCorrespondence>(project.ProductPath,
                                                                                    overlap.MatchGuid, project.Name);
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
