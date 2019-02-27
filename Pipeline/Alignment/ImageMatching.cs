using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using log4net;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class ImageMatching
    {
        private static readonly int MIN_MATCHES = 20;

        public static ComputedCorrespondence ComputeCorrespondence(PipelineCore pipeline, string projectName,
                                                                   string modelUrl, string dataUrl,
                                                                   string modelFrameName, string dataFrameName)
        {
            //build a minimal scene graph containing these two frames and their ancestors
            var opts = new BuildSceneGraph.Options() {
                PreloadCaches = false, //in this situation it will in general be a loss to preload caches
                UseTransformPriors = true, //we build the scene graph bottom-up so the frame cache doesn't scan
                LoadFeatures = true //observation cache doesn't scan
            };
            BuildSceneGraph builder = new BuildSceneGraph(pipeline, projectName, opts);
            AlignmentScene scene = builder.BuildBottomUp(new[] { modelFrameName, dataFrameName });
            (new FrustumOverlapDetector(pipeline, pipeline)).MakeHulls(scene);
            return ComputeCorrespondence(pipeline, scene, modelUrl, dataUrl);
        }

        public static ComputedCorrespondence ComputeCorrespondence(PipelineCore pipeline, AlignmentScene scene,
                                                                   string modelUrl, string dataUrl)
        {
            Debug.Assert(scene.ObservationUrlToNode.ContainsKey(modelUrl));
            Debug.Assert(scene.ObservationUrlToNode.ContainsKey(dataUrl));
            Debug.Assert(scene.DetectedFeatures.ContainsKey(modelUrl));
            Debug.Assert(scene.DetectedFeatures.ContainsKey(dataUrl));

            var modelNode = scene.ObservationUrlToNode[modelUrl];
            var dataNode = scene.ObservationUrlToNode[dataUrl];

            var modelObs = modelNode.GetComponent<NodeObservation>().Observation;
            var dataObs = dataNode.GetComponent<NodeObservation>().Observation;


            string pairName = (new URLPair(modelUrl, dataUrl)).ToStringShort();
            pipeline.LogVerbose("{0} ({1} features, {2} features): (re)computing feature matches",
                                pairName,
                                scene.DetectedFeatures[modelUrl].Length,
                                scene.DetectedFeatures[dataUrl].Length);

            if (modelObs is RoverObservation && dataObs is RoverObservation)
            {
                var mro = modelObs as RoverObservation;
                var dro = dataObs as RoverObservation;
                pipeline.LogVerbose("{0} SiteDrives: {1}, {2}",
                                    pairName,
                                    (new SiteDrive(mro.Site, mro.Drive)).ToString(),
                                    (new SiteDrive(dro.Site, dro.Drive)).ToString());
            }

            //IFeatureMatcher matcher = new EmguSIFTMatcher();
            //IFeatureMatcher matcher = new KnownGeometryMatcher();
            //IFeatureMatcher matcher = new BruteForceMatcher();
            IFeatureMatcher matcher = new CascadeHashingMatcher();
            var matches = matcher.Match(scene, modelUrl, dataUrl);
            if (matches.Count < MIN_MATCHES)
            {
                pipeline.LogVerbose("{0} {1}: {2} < {3} matches, discarding", pairName, matcher.GetType().Name,
                                    matches.Count, MIN_MATCHES);
                return null;
            }
            pipeline.LogVerbose("{0} {1}: {2} matches", pairName, matcher.GetType().Name, matches.Count);

            List<IMatchFilter> filters = new List<IMatchFilter>();
            filters.Add(new KnownGeometryFilter(pipeline));
            filters.Add(new MoisanStivalFilter(pipeline));
            //filters.Add(new GTMFilter());

            foreach (var filter in filters)
            {
                int oldCount = matches.Count;
                matches = filter.Filter(scene, matches);
                if (matches.Count < MIN_MATCHES)
                {
                    pipeline.LogVerbose("{0} {1}: {2} < {3} matches, discarding", pairName, filter.GetType().Name,
                                        matches.Count, MIN_MATCHES);
                    return null;
                }
                pipeline.LogVerbose("{0} {1}: {2} -> {3}", pairName, filter.GetType().Name, oldCount, matches.Count);
            }

            pipeline.LogVerbose("{0} {1}: {2} -> {3} feature matches", pairName, matcher.GetType().Name,
                                string.Join(", ", filters.Select(f => f.GetType().Name)), matches.Count);

            return new ComputedCorrespondence()
            {
                ModelFeaturesGuid = modelObs.FeaturesGuid,
                DataFeaturesGuid = dataObs.FeaturesGuid,
                Correspondence = matches
            };
        }

        public static bool SaveOverlap(PipelineCore pipeline, string projectName, Guid matchGuid,
                                       string modelObsName, string dataObsName)
        {
            var dbOverlap = Overlap.Create(pipeline, projectName, modelObsName, dataObsName);
            if (dbOverlap == null)
            {
                return false;
            }
            dbOverlap.MatchGuid = matchGuid;
            dbOverlap.Status = matchGuid != Guid.Empty ? Overlap.StatusType.Matched : Overlap.StatusType.Rejected;
            return dbOverlap.TrySave(pipeline);
        }

        public static AlignmentScene BuildSceneAndDetectOverlaps(PipelineCore pipeline, Project project,
                                                                 bool loadFeatures = true, bool redoOverlaps = false,
                                                                 bool onlyCrossSite = true,
                                                                 Func<Observation, bool> filter = null)
        {
            pipeline.LogInfo("building scene graph for {0}image matching",
                             onlyCrossSite ? "cross-site " : "");
            var sb = new BuildSceneGraph(pipeline, project.Name, new BuildSceneGraph.Options()
                                         {
                                             UseTransformPriors = true,
                                             LoadFeatures = loadFeatures,
                                             LoadOverlaps = !redoOverlaps,
                                             OnlyKeepImagesWithFeatures = true,
                                             OnlyKeepBestImages = true,
                                             OnlyCrossSiteDriveOverlaps = onlyCrossSite,
                                             IncludeObservation = obs => filter == null || filter(obs)
                                         });
            var scene = sb.BuildTopDown(project.RootFrame);

            if (scene.Overlaps.Count == 0)
            {
                var fod = new FrustumOverlapDetector(pipeline, pipeline);
                fod.Detect(scene, onlyCrossSite);
            }
            return scene;
        }
    }
}
