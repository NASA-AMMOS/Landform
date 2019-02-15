using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using MathNet.Numerics.LinearAlgebra;
using log4net;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class BuildSceneGraph : PipelineRoutine
    {
        public delegate bool IncludeFrameDelegate(Frame frame);
        public delegate bool IncludeObservationDelegate(Observation observation);

        /// <summary>
        /// By default only image observations marked UseForReconstruction are loaded.  
        /// </summary>
        public class Options
        {
            public bool UseTransformPriors = false;
            public bool PreloadCaches = true;
            public bool LoadObservations = true;
            public bool LoadFeatures = false; //implies LoadObservations
            public bool LoadOverlaps = false; //implies LoadFeatures
            public bool LoadCorrespondences = false; //implies LoadOverlaps
            public bool OnlyKeepImagesWithFeatures = false;
            public bool OnlyKeepBestImages = false;
            public bool OnlyLoadObservationsForReconstruction = true;
            public bool OnlyLoadImageObservations = true;
            public bool OnlyCrossSiteDriveOverlaps = false;
            public IncludeFrameDelegate IncludeFrame = _ => true;
            public IncludeObservationDelegate IncludeObservation = _ => true;
        }
        private readonly Options options;

        private readonly string projectName;

        public enum BuildDirection { TopDown, BottomUp }

        public BuildSceneGraph(PipelineCore pipeline, string projectName, Options options = null) : base(pipeline)
        {
            this.projectName = projectName;
            this.options = options ?? new Options();
        }

        private static bool ValidGuid(Guid g)
        {
            return g != null && g != Guid.Empty;
        }

        private static readonly string imageObs = ObservationType.Image.ToString();
        private static bool IsImage(Observation obs)
        {
            return obs.ObservationType == imageObs;
        }

        public AlignmentScene BuildTopDown(Frame root)
        {
            return Build(BuildDirection.TopDown, new Frame[] { root });
        }

        public AlignmentScene BuildTopDown(string rootName)
        {
            return Build(BuildDirection.TopDown, new string[] { rootName });
        }

        public AlignmentScene BuildBottomUp(IEnumerable<Frame> leaves)
        {
            return Build(BuildDirection.BottomUp, leaves);
        }

        public AlignmentScene BuildBottomUp(IEnumerable<string> leafNames)
        {
            return Build(BuildDirection.BottomUp, leafNames);
        }

        public AlignmentScene Build(BuildDirection direction, IEnumerable<string> startFrames,
                                    FrameCache frameCache = null, ObservationCache observationCache = null,
                                    OverlapCache overlapCache = null)
        {
            if (frameCache == null)
            {
                frameCache = new FrameCache(pipeline, projectName);
            }
            return Build(direction, startFrames.Select(n => frameCache.GetFrame(n)),
                         frameCache, observationCache, overlapCache);
        }

        public AlignmentScene Build(BuildDirection direction, IEnumerable<Frame> startFrames,
                                    FrameCache frameCache = null, ObservationCache observationCache = null,
                                    OverlapCache overlapCache = null)
        {
            if (options.LoadCorrespondences)
            {
                options.LoadOverlaps = true;
            }

            if (options.LoadOverlaps)
            {
                options.LoadFeatures = true;
            }

            if (options.LoadFeatures)
            {
                options.LoadObservations = true;
            }

            pipeline.LogInfo("building scene graph for project {0}, {1}loading observations, {2}loading overlaps, "
                             + "{3}loading features, {4}loading correspondences", projectName,
                             options.LoadObservations ? "" : "not ", options.LoadOverlaps ? "" : "not ",
                             options.LoadFeatures ? "" : "not ", options.LoadCorrespondences ? "" : "not ");

            double lastSpew = UTCTime.Now();

            var project = Project.Find(pipeline, projectName);
            if (frameCache == null)
            {
                frameCache = new FrameCache(pipeline, projectName);
            }
            AlignmentScene scene = new AlignmentScene();

            if (options.PreloadCaches)
            {
                pipeline.LogInfo("preloading frame cache for project {0}", projectName);
                int numPreloaded = frameCache.Preload(loadTransforms: !options.UseTransformPriors,
                                                      loadPriors: options.UseTransformPriors);
                pipeline.LogInfo("preloaded {0} frames for project {1}", numPreloaded, projectName);
            }

            if (options.LoadObservations)
            {
                if (observationCache == null)
                {
                    observationCache = new ObservationCache(pipeline, projectName);
                }
                if (options.PreloadCaches)
                {
                    pipeline.LogInfo("preloading observation cache for project {0}", projectName);
                    Func<Observation, bool> filter =
                        obs => !options.OnlyLoadObservationsForReconstruction || obs.UseForReconstruction;
                    int numPreloaded = observationCache.Preload(filter);
                    pipeline.LogInfo("preloaded {0} observations for project {1}", numPreloaded, projectName);
                }
            }

            int numFeatures = 0;
            Dictionary<string, Observation> loadedObservations = new Dictionary<string, Observation>();
            void addObservation(Observation obs, SceneNode node)
            {
                if (IsImage(obs))
                {
                    if (options.LoadFeatures && ValidGuid(obs.FeaturesGuid))
                    {
                        var feat = pipeline.GetDataProduct<DetectedFeatures>(project.ProductPath, obs.FeaturesGuid,
                                                                             projectName);
                        scene.DetectedFeatures[obs.Url] = feat.Features;
                        numFeatures++;
                    }
                    var img = node.AddComponent<NodeImage>();
                    img.CameraModel = (CameraModel)JsonHelper.FromJson(obs.CameraModel);
                    img.Size = new Vector2(obs.Width, obs.Height);
                    img.Url = obs.Url;
                }
                node.AddComponent<NodeObservation>().Observation = obs;
                scene.ObservationUrlToNode[obs.Url] = node;
                loadedObservations[obs.Name] = obs;
            }

            HashSet<string> loadedNodes = new HashSet<string>();
            SceneNode addNode(Frame frame)
            {
                if (loadedNodes.Contains(frame.Name) || !options.IncludeFrame(frame))
                {
                    return null;
                }

                var node = new SceneNode(frame.Name);

                node.AddComponent<NodeFrame>().Frame = frameCache.GetFrame(frame.Name);

                UncertainRigidTransform ut = null;
                if (options.UseTransformPriors)
                {
                    var tp = frameCache.GetTransformPrior(frame);
                    if (tp == null)
                    {
                        throw new Exception("failed to get transform prior for frame " + frame.Name);
                    }
                    ut = tp.Transform;
                }
                else
                {
                    var ft = frameCache.GetTransform(frame);
                    if (ft == null)
                    {
                        throw new Exception("failed to get transform for frame " + frame.Name);
                    }
                    ut = ft.Transform;
                }
                node.GetOrAddComponent<NodeUncertainTransform>().UncertainTransform = ut;

                if (options.LoadObservations)
                {
                    var obsForFrame = observationCache.GetAllObservationsForFrame(frame)
                        .Where(o => options.IncludeObservation(o))
                        .Where(o => !options.OnlyLoadObservationsForReconstruction || o.UseForReconstruction)
                        .Where(o => !options.OnlyLoadImageObservations || IsImage(o))
                        .Where(o => !options.OnlyKeepImagesWithFeatures || !IsImage(o) || ValidGuid(o.FeaturesGuid))
                        .Cast<RoverObservation>()
                        .ToArray();

                    if (options.OnlyKeepBestImages && obsForFrame.Length > 0)
                    {
                        obsForFrame = new RoverObservation[] { MSLProject.FindBestImage(obsForFrame) };
                    }

                    if (obsForFrame.Length == 1)
                    {
                        addObservation(obsForFrame[0], node);
                    }
                    else 
                    {
                        //a SceneNode can have only one component of each type
                        //there are multiple Observations for this frame
                        //so in this case we create a child to hold each Observation
                        foreach (var obs in obsForFrame)
                        {
                            var obsNode = new SceneNode(obs.Name, node.Transform); //identity transform
                            obsNode.AddComponent<NodeUncertainTransform>(); //no uncertainty
                            addObservation(obs, obsNode);
                        }
                    }
                }

                loadedNodes.Add(node.Name);

                double now = UTCTime.Now();
                if (now - lastSpew > 10)
                {
                    pipeline.LogInfo("loaded {0} nodes, {1} observations, {2} feature products",
                                     loadedNodes.Count, loadedObservations.Count, numFeatures);
                    lastSpew = now;
                }

                return node;
            }

            switch (direction)
            {
                case BuildDirection.TopDown:
                {
                    SceneNode spawn(Frame frame, SceneNode parent)
                    {
                        var node = addNode(frame);
                        if (node != null)
                        {
                            node.Parent = parent;
                            foreach (var child in frameCache.GetChildren(frame))
                            {
                                spawn(child, node);
                            }
                        }
                        return node;
                    }
                    scene.Root = spawn(startFrames.First(), null);
                    break;
                }
                case BuildDirection.BottomUp:
                {
                    void spawn(Frame frame, SceneNode child)
                    {
                        var node = addNode(frame);
                        if (node != null)
                        {
                            if (child != null)
                            {
                                child.Parent = node;
                            }
                            if (!string.IsNullOrEmpty(frame.ParentName))
                            {
                                spawn(frameCache.GetFrame(frame.ParentName), node);
                            }
                            else
                            {
                                scene.Root = node;
                            }
                        }
                    }
                    foreach (var frame in startFrames)
                    {
                        spawn(frame, null);
                    }
                    break;
                }
            }

            pipeline.LogInfo("built scene graph: spawned {0} nodes, {1} observations, {2} feature products",
                             loadedNodes.Count, loadedObservations.Count, numFeatures);

            if (options.LoadOverlaps)
            {
                LoadOverlaps(project, scene, loadedObservations, observationCache, overlapCache);
            }

            return scene;
        }

        private void LoadOverlaps(Project project, AlignmentScene scene,
                                  Dictionary<string, Observation> loadedObservations,
                                  ObservationCache observationCache, OverlapCache overlapCache)
        {
            pipeline.LogInfo("adding overlaps{0} to scene{1}",
                             options.LoadCorrespondences ? " and computed correspondences" : "",
                             options.OnlyCrossSiteDriveOverlaps ? ", only overlaps between different site-drives" : "");

            if (overlapCache == null)
            {
                overlapCache = new OverlapCache(pipeline, projectName);
            }
            if (options.PreloadCaches)
            {
                pipeline.LogInfo("preloading overlap cache for project {0}", projectName);
                int numPreloaded = overlapCache.Preload();
                pipeline.LogInfo("preloaded {0} overlaps for project {1}", numPreloaded, projectName);
            }

            double lastSpew = UTCTime.Now();
            int numOverlaps = 0, numProcessed = 0, numSkipped = 0, numCorrespondences = 0;
            foreach (var obs in loadedObservations.Values)
            {
                foreach (var overlap in overlapCache.GetAllOverlapsForObservation(obs))
                {
                    var n1 = overlap.ObservationNameOne;
                    var n2 = overlap.ObservationNameTwo;

                    // Skip any overlaps that involve an observation we didn't ingest
                    if (!loadedObservations.ContainsKey(n1) || !loadedObservations.ContainsKey(n2))
                    {
                        continue;
                    }

                    var o1 = observationCache.GetObservation(n1);
                    var o2 = observationCache.GetObservation(n2);

                    if (options.OnlyCrossSiteDriveOverlaps && o1 is RoverObservation && o2 is RoverObservation)
                    {
                        var ro1 = o1 as RoverObservation;
                        var ro2 = o2 as RoverObservation;
                        var sd1 = new SiteDrive(ro1.Site, ro1.Drive);
                        var sd2 = new SiteDrive(ro2.Site, ro2.Drive);
                        if (sd1 == sd2)
                        {
                            numSkipped++;
                            continue;
                        }
                    }

                    numOverlaps++;
                    var pair = new URLPair(o1.Url, o2.Url);
                    scene.Overlaps.Add(pair);

                    if (options.LoadCorrespondences && ValidGuid(overlap.MatchGuid))
                    {
                        var match = pipeline.GetDataProduct<ComputedCorrespondence>(project.ProductPath,
                                                                                    overlap.MatchGuid,
                                                                                    projectName);
                        if (match != null)
                        {
                            scene.Correspondences[pair] = match.Correspondence;
                            numCorrespondences++;
                        }
                    }
                }

                numProcessed++;

                double now = UTCTime.Now();
                if (now - lastSpew > 10)
                {
                    pipeline.LogInfo("processed {0}/{1} observations, "
                                     + "added {2} overlaps, {3} skipped, {4} correspondence products",
                                     numProcessed, loadedObservations.Count, numOverlaps, numCorrespondences);
                    lastSpew = now;
                }
            }

            pipeline.LogInfo("done adding overlaps: processed {0}/{1} observations, "
                             + "added {2} overlaps, {3} skipped, {4} correspondence products",
                             numProcessed, loadedObservations.Count, numOverlaps, numSkipped, numCorrespondences);
        }
    }
}
