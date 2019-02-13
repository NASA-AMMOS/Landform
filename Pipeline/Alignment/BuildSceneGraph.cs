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
        private FrameCache frameCache = null;

        public BuildSceneGraph(PipelineCore pipeline) : base(pipeline) { }

        public delegate bool IncludeFrameDelegate(Frame frame, SceneNode parent);
        public delegate bool IncludeObservationDelegate(Observation observation, SceneNode parent);
        public delegate UncertainRigidTransform FrameTransformDelegate(Frame frame, SceneNode parent);

        public UncertainRigidTransform StandardFrameTransform(Frame frame, SceneNode parent)
        {
            var transform = frameCache.GetTransform(frame);
            if (transform == null)
            {
                pipeline.LogError("no transform found for frame {0}", frame.Name);
                return null;
            }
            return transform.Transform;
        }

        public UncertainRigidTransform CertainFrameTransform(Frame frame, SceneNode parent)
        {
            var transform = frameCache.GetTransform(frame);
            if (transform == null)
            {
                pipeline.LogError("No transform found for frame {0}", frame.Name);
                return null;
            }
            return new UncertainRigidTransform(transform.Transform.Mean, CreateMatrix.Dense<double>(6,6));
        }

        static bool ValidGuid(Guid g)
        {
            return g != null && g != Guid.Empty;
        }

        public class Options
        {
            /// <summary>
            /// Return true if a given frame should be included in the graph.
            /// 
            /// Defaults to always true.
            /// </summary>
            public IncludeFrameDelegate IncludeFrame = (f, p) => true;

            /// <summary>
            /// Return true if a given observation should be included in the graph.
            /// 
            /// Defaults to always true.
            /// </summary>
            public IncludeObservationDelegate IncludeObservation = (f, p) => true;

            /// <summary>
            /// Return the uncertain transform associated with a frame.
            /// 
            /// Defaults to values in the FrameTransforms table.
            /// </summary>
            public FrameTransformDelegate GetTransform;

            /// <summary>
            /// If true, an observation requires features to have been detected to have its image URL
            /// added to the scene graph nodes.
            ///
            /// Defaults true.
            /// </summary>
            public bool RequireFeaturesForObservations = true;

            public bool LoadDetectedFeatures = true;

            public bool LoadCorrespondences = true;
        }

        public AlignmentScene Build(Frame root, Options options)
        {
            frameCache = new FrameCache(pipeline, root.ProjectName);
            var observationCache = new ObservationCache(pipeline, root.ProjectName);

            if (options.GetTransform == null)
            {
                options.GetTransform = StandardFrameTransform;
            }

            pipeline.LogInfo("building scene graph for project {0}, {1}loading features, {2}loading correspondences",
                             root.ProjectName,
                             options.LoadDetectedFeatures ? "" : "not ", options.LoadCorrespondences ? "" : "not ");

            pipeline.LogInfo("preloading frame cache for project {0}", root.ProjectName);
            int numPreloaded = frameCache.Preload();
            pipeline.LogInfo("preloaded {0} frames for project {1}", numPreloaded, root.ProjectName);

            pipeline.LogInfo("preloading observation cache for project {0}", root.ProjectName);
            numPreloaded = observationCache.Preload();
            pipeline.LogInfo("preloaded {0} observations for project {1}", numPreloaded, root.ProjectName);

            AlignmentScene scene = new AlignmentScene();

            var project = Project.Find(pipeline, root.ProjectName);

            Action<Observation, SceneNode> addObservation = (obs, node) =>
            {
                var imgUrl = obs.Url;

                if (options.LoadDetectedFeatures)
                {
                    if (ValidGuid(obs.FeaturesGuid))
                    {
                        var feat = pipeline.GetDataProduct<DetectedFeatures>(project.ProductPath, obs.FeaturesGuid,
                                                                             project.Name);
                        scene.DetectedFeatures[imgUrl] = feat.Features;
                    }
                    else if (options.RequireFeaturesForObservations)
                    {
                        return;
                    }
                }

                scene.ImageToNode[imgUrl] = node;
                node.AddComponent<NodeImageUrl>().Url = imgUrl;
                node.AddComponent<NodeObservation>().observation = obs;
            };

            List<Observation> observations = new List<Observation>();
            HashSet<string> observationNames = new HashSet<string>();

            double lastSpew = UTCTime.Now();
            int numNodes = 0, numObs = 0;
            SceneNode spawn(Frame frame, SceneNode parent)
            {
                var res = new SceneNode(frame.Name, parent?.Transform);
                numNodes++;

                var ut = options.GetTransform(frame, parent);
                if (ut == null) return null;
                
                //add transforms for parents so they get bundle adjusted
                res.GetOrAddComponent<NodeUncertainTransform>().UncertainTransform = ut;
                
                // Add any observations to the node
                var obs = observationCache.GetAllObservationsForFrame(frame)
                    .Where(o => options.IncludeObservation(o, res))
                    .ToArray();
                numObs += obs.Length;

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
                
                double now = UTCTime.Now();
                if (now - lastSpew > 10)
                {
                    pipeline.LogInfo("spawned {0} nodes, {1} observations", numNodes, numObs);
                    lastSpew = now;
                }

                // Add child frames
                foreach (var childFrame in frameCache.GetChildren(frame))
                {
                    if (!options.IncludeFrame(childFrame, res))
                    {
                        continue;
                    }
                    
                    spawn(childFrame, res);
                }

                return res;
            }
            scene.Root = spawn(root, null);

            pipeline.LogInfo("built scene graph: spawned {0} nodes, {1} observations", numNodes, numObs);

            pipeline.LogInfo("adding overlaps{0} to scene",
                             options.LoadCorrespondences ? " and computed correspondences" : "");
            lastSpew = UTCTime.Now();
            int numOverlaps = 0, numProcessed = 0;
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

                    numOverlaps++;

                    var o1 = observationCache.GetObservation(n1);
                    var o2 = observationCache.GetObservation(n2);
                    var pair = new URLPair(o1.Url, o2.Url);

                    scene.Overlaps.Add(pair);

                    if (options.LoadCorrespondences)
                    {
                        if (ValidGuid(overlap.MatchGuid))
                        {
                            var match =
                                pipeline.GetDataProduct<ComputedCorrespondence>(project.ProductPath, overlap.MatchGuid,
                                                                                project.Name);
                            if (match != null)
                            {
                                scene.Correspondences[pair] = match.Correspondence;
                            }
                        }
                    }
                }
                numProcessed++;
                double now = UTCTime.Now();
                if (now - lastSpew > 10)
                {
                    pipeline.LogInfo("processed {0}/{1} observations, added {2} overlaps",
                                     numProcessed, numObs, numOverlaps);
                    lastSpew = now;
                }
            }

            pipeline.LogInfo("done adding overlaps: processed {0}/{1} observations, added {2} overlaps",
                             numProcessed, numObs, numOverlaps);

            frameCache = null;
            observationCache = null;

            return scene;
        }
    }
}
