using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class ImageMatching
    {
        public enum MatcherType
        {
            EmguSIFT,
            KnownGeometry,
            BruteForce,
            CascadeHashing
        }
        public const MatcherType DEF_MATCHER_TYPE = MatcherType.CascadeHashing;
        public const int DEF_MIN_MATCHES = 20;
        public const double DEF_MAX_DESCRIPTOR_DISTANCE_RATIO = 0.9;
        public const double DEF_MAX_DESCRIPTOR_DISTANCE = 0;
        public const bool DEF_USE_KNOWN_GEOMETRY_FILTER = true;
        public const bool DEF_USE_MOISAN_STIVAL_FILTER = true;
        public const bool DEF_USE_GTM_FILTER = false;

        public class Options
        {
            public MatcherType MatcherType = DEF_MATCHER_TYPE;
            public int MinMatches = DEF_MIN_MATCHES;
            public double MaxDescriptorDistanceRatio = DEF_MAX_DESCRIPTOR_DISTANCE_RATIO;
            public double MaxDescriptorDistance = DEF_MAX_DESCRIPTOR_DISTANCE;
            public bool UseKnownGeometryFilter = DEF_USE_KNOWN_GEOMETRY_FILTER;
            public bool UseMoisanStivalFilter = DEF_USE_MOISAN_STIVAL_FILTER;
            public bool UseGTMFilter = DEF_USE_GTM_FILTER;
            public double KGFMahalanobisThreshold = KnownGeometryFilter.DEF_MAHALANOBIS_THRESHOLD;
            public double KGFMajorAxisThreshold = KnownGeometryFilter.DEF_MAJOR_AXIS_THRESHOLD;
        }

        public static ComputedCorrespondence ComputeCorrespondence(PipelineCore pipeline, string projectName,
                                                                   string modelUrl, string dataUrl,
                                                                   string modelFrameName, string dataFrameName,
                                                                   Options options = null)
        {
            //build a minimal scene graph containing these two frames and their ancestors
            var bsoOpts = new BuildSceneGraph.Options() {
                PreloadCaches = false, //in this situation it will in general be a loss to preload caches
                UseTransformPriors = true, //we build the scene graph bottom-up so the frame cache doesn't scan
                LoadFeatures = true //observation cache doesn't scan
            };
            BuildSceneGraph builder = new BuildSceneGraph(pipeline, projectName, bsoOpts);
            AlignmentScene scene = builder.BuildBottomUp(new[] { modelFrameName, dataFrameName });
            (new FrustumOverlapDetector(pipeline, pipeline)).MakeHulls(scene);
            return ComputeCorrespondence(pipeline, scene, modelUrl, dataUrl, options);
        }

        public static ComputedCorrespondence ComputeCorrespondence(PipelineCore pipeline, AlignmentScene scene,
                                                                   string modelUrl, string dataUrl,
                                                                   Options options = null)
        {
            string rejectionReason;
            return ComputeCorrespondence(pipeline, scene, modelUrl, dataUrl, out rejectionReason, options);
        }


        public static ComputedCorrespondence ComputeCorrespondence(PipelineCore pipeline, AlignmentScene scene,
                                                                   string modelUrl, string dataUrl,
                                                                   out string rejectionReason, Options options = null)
        {
            if (options == null)
            {
                options = new Options();
            }

            rejectionReason = null;

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

            IFeatureMatcher matcher = null;
            switch (options.MatcherType)
            {
                case MatcherType.EmguSIFT:
                {
                    matcher = new EmguSIFTMatcher() { MaxDistanceRatio = options.MaxDescriptorDistanceRatio };
                    break;
                }
                case MatcherType.KnownGeometry:
                {
                    matcher = new KnownGeometryMatcher() { MaxDistanceRatio = options.MaxDescriptorDistanceRatio };
                    break;
                }
                case MatcherType.BruteForce:
                {
                    matcher = new BruteForceMatcher() { MaxDistanceRatio = options.MaxDescriptorDistanceRatio };
                    break;
                }
                case MatcherType.CascadeHashing:
                {
                    matcher = new CascadeHashingMatcher() { MaxDistanceRatio = options.MaxDescriptorDistanceRatio };
                    break;
                }
            }

            pipeline.LogVerbose("{0} {1}: maxDescriptorDistanceRatio = {2}, minMatches = {3}" +
                                "useKnownGeometryFilter={4}, useMoisanStivalFilter={5}, useGTMFilter={6}",
                                pairName, matcher.GetType().Name, options.MaxDescriptorDistanceRatio,
                                options.MinMatches, options.UseKnownGeometryFilter, options.UseMoisanStivalFilter,
                                options.UseGTMFilter);

            var matches = matcher.Match(scene, modelUrl, dataUrl);

            if (matches.Count < options.MinMatches)
            {
                pipeline.LogVerbose("{0} {1}: {2} < {3} matches, discarding", pairName, matcher.GetType().Name,
                                    matches.Count, options.MinMatches);
                rejectionReason = string.Format("(step 0) {0} returned too few matches", matcher.GetType().Name);
                return null;
            }
            pipeline.LogVerbose("{0} {1}: {2} matches", pairName, matcher.GetType().Name, matches.Count);

            List<IMatchFilter> filters = new List<IMatchFilter>();
            if (options.MaxDescriptorDistance > 0)
            {
                filters.Add(new DescriptorDistanceFilter(options.MaxDescriptorDistance));
            }
            if (options.UseKnownGeometryFilter)
            {
                filters.Add(new KnownGeometryFilter(pipeline)
                            {
                                MahalanobisThreshold = options.KGFMahalanobisThreshold,
                                MajorAxisThreshold = options.KGFMajorAxisThreshold
                            });
            }
            if (options.UseMoisanStivalFilter)
            {
                filters.Add(new MoisanStivalFilter(pipeline));
            }
            if (options.UseGTMFilter)
            { 
                filters.Add(new GTMFilter());
            }
            int step = 1;
            foreach (var filter in filters)
            {
                int oldCount = matches.Count;
                matches = filter.Filter(scene, matches);
                if (matches.Count < options.MinMatches)
                {
                    pipeline.LogVerbose("{0} {1}: {2} < {3} matches, discarding", pairName, filter.GetType().Name,
                                        matches.Count, options.MinMatches);
                    rejectionReason = string.Format("(step {0}) {1} returned too few matches",
                                                    step, filter.GetType().Name);
                    return null;
                }
                pipeline.LogVerbose("{0} {1}: {2} -> {3}", pairName, filter.GetType().Name, oldCount, matches.Count);
                step++;
            }

            pipeline.LogVerbose("{0} {1}: {2} -> {3} feature matches, keeping", pairName, matcher.GetType().Name,
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
                                                                 Func<Observation, bool> obsFilter = null,
                                                                 Func<string, string, bool> overlapFilter = null)
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
                                             IncludeObservation = obs => obsFilter == null || obsFilter(obs),
                                             IncludeOverlap = (n1, n2) => overlapFilter == null || overlapFilter(n1, n2)
                                         });
            string rootName = MissionSpecific.GetInstance(project.Mission).RootFrameName();
            var scene = sb.BuildTopDown(rootName);

            var fod = new FrustumOverlapDetector(pipeline, pipeline);

            //scene should have no overlaps if redoOverlaps is set because they shouldn't have been loaded
            //but it's more clear and doesn't hurt to also or with redoOverlaps here
            if (redoOverlaps || scene.Overlaps.Count == 0)
            {
                fod.Detect(scene, onlyCrossSite);
            }
            else
            {
                fod.MakeHulls(scene);
            }
            return scene;
        }
    }
}
