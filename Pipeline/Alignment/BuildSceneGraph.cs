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
        public delegate bool IncludeOverlapDelegate(string observationName1, string observationName2);

        /// <summary>
        /// By default only image observations marked UseForAlignment are loaded.  
        /// </summary>
        public class Options
        {
            public bool UseTransformPriors = false;
            public bool PreloadCaches = true;
            public bool LoadObservations = true;
            public bool LoadFeatures = false; //implies LoadObservations
            public bool LoadOverlaps = false; //implies LoadObservations
            public bool LoadCorrespondences = false; //implies LoadOverlaps
            public bool OnlyKeepImagesWithFeatures = false;
            public bool OnlyKeepBestImages = false;
            public bool OnlyLoadObservationsForAlignment = true;
            public bool OnlyLoadImageObservations = true;
            public bool OnlyCrossSiteDriveOverlaps = false;
            public IncludeFrameDelegate IncludeFrame = _ => true;
            public IncludeObservationDelegate IncludeObservation = _ => true;
            public IncludeOverlapDelegate IncludeOverlap = (n1, n2) => true;
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

        private static bool IsImage(Observation obs)
        {
            return obs is RoverObservation && ((RoverObservation)obs).ObservationType == RoverProductType.Image;
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
                options.LoadObservations = true;
            }

            if (options.LoadFeatures)
            {
                options.LoadObservations = true;
            }

            pipeline.LogInfo("building scene graph for project {0}, {1}loading observations, {2}loading overlaps, "
                             + "{3}loading features, {4}loading correspondences", projectName,
                             options.LoadObservations ? "" : "not ", options.LoadOverlaps ? "" : "not ",
                             options.LoadFeatures ? "" : "not ", options.LoadCorrespondences ? "" : "not ");

            double startTime = UTCTime.Now();

            var project = Project.Find(pipeline, projectName);
            if (frameCache == null)
            {
                frameCache = new FrameCache(pipeline, projectName);
            }
            AlignmentScene scene = new AlignmentScene();

            if (options.PreloadCaches)
            {
                pipeline.LogInfo("preloading frame cache for project {0}", projectName);
                double start = UTCTime.Now();
                int numPreloaded = frameCache.Preload();
                pipeline.LogInfo("preloaded {0} frames for project {1} in {2:F3}s",
                                 numPreloaded, projectName, UTCTime.Now() - start);
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
                        obs => !obs.IsOrbital && (!options.OnlyLoadObservationsForAlignment || obs.UseForAlignment);
                    double start = UTCTime.Now();
                    int numPreloaded = observationCache.Preload(filter);
                    pipeline.LogInfo("preloaded {0} observations for project {1} in {2:F3}s",
                                     numPreloaded, projectName, UTCTime.Now() - start);
                }
            }

            int numFeatures = 0;
            Dictionary<string, Observation> loadedObservations = new Dictionary<string, Observation>();
            void addObservation(Observation obs, SceneNode node)
            {
                if (IsImage(obs))
                {
                    pipeline.LogDebug("adding image observation {0} to node {1}", obs.Url, node.Name);
                    if (options.LoadFeatures && ValidGuid(obs.FeaturesGuid))
                    {
                        pipeline.LogDebug("adding detected features for image obseration {0} to node {1}",
                                          obs.Url, node.Name);
                        var feat = pipeline.GetDataProduct<DetectedFeatures>(project.ProductPath, obs.FeaturesGuid,
                                                                             projectName);
                        scene.DetectedFeatures[obs.Url] = feat.Features;
                        numFeatures++;
                    }
                    var img = node.AddComponent<NodeImage>();
                    img.CameraModel = obs.CameraModel;
                    img.Size = new Vector2(obs.Width, obs.Height);
                    img.Url = obs.Url;
                }
                node.AddComponent<NodeObservation>().Observation = obs;
                scene.ObservationUrlToNode[obs.Url] = node;
                loadedObservations[obs.Name] = obs;
            }

            pipeline.LogInfo("building scene graph {0}", direction);

            double lastSpew = startTime;
            Dictionary<string, SceneNode> loadedNodes = new Dictionary<string, SceneNode>();
            SceneNode addOrGetNode(Frame frame)
            {
                if (!options.IncludeFrame(frame))
                {
                    return null;
                }

                if (loadedNodes.ContainsKey(frame.Name))
                {
                    return loadedNodes[frame.Name];
                } 

                var node = new SceneNode(frame.Name);
                loadedNodes[node.Name] = node;

                node.AddComponent<NodeFrame>().Frame = frameCache.GetFrame(frame.Name);

                UncertainRigidTransform ut = null;
                if (options.UseTransformPriors)
                {
                    var tp = frameCache.GetBestPrior(frame);
                    if (tp == null)
                    {
                        throw new Exception("failed to get transform prior for frame " + frame.Name);
                    }
                    ut = tp.Transform;
                }
                else
                {
                    var ft = frameCache.GetBestTransform(frame);
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
                        .Where(o => o is RoverObservation)
                        .Where(o => options.IncludeObservation(o))
                        .Where(o => !options.OnlyLoadObservationsForAlignment || o.UseForAlignment)
                        .Where(o => !options.OnlyLoadImageObservations || IsImage(o))
                        .Where(o => !options.OnlyKeepImagesWithFeatures || !IsImage(o) || ValidGuid(o.FeaturesGuid))
                        .Cast<RoverObservation>()
                        .ToArray();

                    pipeline.LogDebug("kept {0} observations for frame {1}", obsForFrame.Length, frame.Name);

                    if (options.OnlyKeepBestImages && obsForFrame.Length > 0)
                    {
                        var comparator = MissionSpecific.GetInstance(project.Mission).GetRoverObservationComparator();
                        obsForFrame = new RoverObservation[] { obsForFrame.OrderBy(obs => obs, comparator).First() };
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
                            loadedNodes[obsNode.Name] = obsNode;
                            obsNode.AddComponent<NodeUncertainTransform>(); //no uncertainty
                            addObservation(obs, obsNode);
                        }
                    }
                }

                double now = UTCTime.Now();
                if (now - lastSpew > 5)
                {
                    pipeline.LogInfo("loaded {0} nodes, {1} observations, {2} feature products...",
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
                        var node = addOrGetNode(frame);
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
                    pipeline.LogDebug("start frames: {0}",
                                      string.Join(", ", startFrames.Select(f => f != null ? f.Name : "null")));
                    void spawn(Frame frame, SceneNode child)
                    {
                        var node = addOrGetNode(frame);
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

            pipeline.LogInfo("loaded {0} nodes, {1} observations, {2} feature products",
                             loadedNodes.Count, loadedObservations.Count, numFeatures);

            if (options.LoadOverlaps)
            {
                LoadOverlaps(project, scene, loadedObservations, observationCache, overlapCache);
            }

            pipeline.LogInfo("built scene graph ({0:F3}s): {1} nodes, {2} observations, " +
                             "{3} feature products, {4} overlaps, {5} correspondences",
                             UTCTime.Now() - startTime, loadedNodes.Count, loadedObservations.Count,
                             numFeatures, scene.Overlaps.Count, scene.Correspondences.Count);

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
                double start = UTCTime.Now();
                int numPreloaded = overlapCache.Preload();
                pipeline.LogInfo("preloaded {0} overlaps for project {1} in {2:F3}s",
                                 numPreloaded, projectName, UTCTime.Now() - start);
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

                    if (!options.IncludeOverlap(n1, n2) && !options.IncludeOverlap(n2, n1))
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

                    var pair = new URLPair(o1.Url, o2.Url);

                    if (!scene.Overlaps.Contains(pair))
                    {
                        scene.Overlaps.Add(pair);
                        numOverlaps++;
                        if (options.LoadCorrespondences && ValidGuid(overlap.MatchGuid) &&
                            !scene.Correspondences.ContainsKey(pair))
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
                }

                numProcessed++;

                double now = UTCTime.Now();
                if (now - lastSpew > 5)
                {
                    pipeline.LogInfo("processed {0}/{1} observations, "
                                     + "added {2} overlaps, {3} skipped, {4} correspondence products...",
                                     numProcessed, loadedObservations.Count, numOverlaps, numSkipped, numCorrespondences);
                    lastSpew = now;
                }
            }

            pipeline.LogInfo("done adding overlaps: processed {0}/{1} observations, "
                             + "added {2} overlaps, {3} skipped, {4} correspondence products",
                             numProcessed, loadedObservations.Count, numOverlaps, numSkipped, numCorrespondences);
        }
    }
}
