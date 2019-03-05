using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using log4net;
using Emgu.CV;
using Emgu.CV.Util;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Imaging.Emgu;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class ImageMatching
    {
        public const int DEF_MIN_MATCHES = 20;

        public static ComputedCorrespondence ComputeCorrespondence(PipelineCore pipeline, string projectName,
                                                                   string modelUrl, string dataUrl,
                                                                   string modelFrameName, string dataFrameName,
                                                                   int minMatches = DEF_MIN_MATCHES)
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
            return ComputeCorrespondence(pipeline, scene, modelUrl, dataUrl, minMatches);
        }

        public static ComputedCorrespondence ComputeCorrespondence(PipelineCore pipeline, AlignmentScene scene,
                                                                   string modelUrl, string dataUrl,
                                                                   int minMatches = DEF_MIN_MATCHES)
        {
            string rejectionReason;
            return ComputeCorrespondence(pipeline, scene, modelUrl, dataUrl, out rejectionReason, minMatches);
        }

        public static ComputedCorrespondence ComputeCorrespondence(PipelineCore pipeline, AlignmentScene scene,
                                                                   string modelUrl, string dataUrl,
                                                                   out string rejectionReason,
                                                                   int minMatches = DEF_MIN_MATCHES)
        {
            rejectionReason = null;

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
            if (matches.Count < minMatches)
            {
                pipeline.LogVerbose("{0} {1}: {2} < {3} matches, discarding", pairName, matcher.GetType().Name,
                                    matches.Count, minMatches);
                rejectionReason = string.Format("(step 0) {0} returned too few matches", matcher.GetType().Name);
                return null;
            }
            pipeline.LogVerbose("{0} {1}: {2} matches", pairName, matcher.GetType().Name, matches.Count);

            List<IMatchFilter> filters = new List<IMatchFilter>();
            filters.Add(new KnownGeometryFilter(pipeline));
            filters.Add(new MoisanStivalFilter(pipeline));
            //filters.Add(new GTMFilter());

            int step = 1;
            foreach (var filter in filters)
            {
                int oldCount = matches.Count;
                matches = filter.Filter(scene, matches);
                if (matches.Count < minMatches)
                {
                    pipeline.LogVerbose("{0} {1}: {2} < {3} matches, discarding", pairName, filter.GetType().Name,
                                        matches.Count, minMatches);
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

            var fod = new FrustumOverlapDetector(pipeline, pipeline);
            if (scene.Overlaps.Count == 0)
            {
                fod.Detect(scene, onlyCrossSite);
            }
            else
            {
                fod.MakeHulls(scene);
            }
            return scene;
        }
        
        public static Image DrawMatches(Image modelImg, Image dataImg, ImageFeature[] modelFeatures,
                                        ImageFeature[] dataFeatures, KeyValuePair<int, int>[] dataToModel)
        {
            var modelFeaturesForDataFeature = new Dictionary<int, HashSet<int>>();
            foreach (var pair in dataToModel)
            {
                int dataFeatureIndex = pair.Key;
                int modelFeatureIndex = pair.Value;
                if (!modelFeaturesForDataFeature.ContainsKey(dataFeatureIndex))
                {
                    modelFeaturesForDataFeature[dataFeatureIndex] = new HashSet<int>();
                }
                modelFeaturesForDataFeature[dataFeatureIndex].Add(modelFeatureIndex);
            }
            var matches = new VectorOfVectorOfDMatch();
            foreach (var pair in modelFeaturesForDataFeature)
            {
                int dataFeatureIndex = pair.Key;
                var matchesForDataFeature = new List<MDMatch>();
                foreach (int modelFeatureIndex in pair.Value)
                {
                    matchesForDataFeature.Add(new MDMatch() {
                            TrainIdx = modelFeatureIndex,
                            QueryIdx = dataFeatureIndex
                        });
                }
                matches.Push(new VectorOfDMatch(matchesForDataFeature.ToArray()));
            }
            var modelKeypoints = new VectorOfKeyPoint(modelFeatures.Cast<SIFTFeature>().CastToMKeyPoint().ToArray());
            var dataKeypoints = new VectorOfKeyPoint(dataFeatures.Cast<SIFTFeature>().CastToMKeyPoint().ToArray());
            var lineColor = new MCvScalar(0, 0, 255); //RGB
            var pointColor = new MCvScalar(255, 255, 0); //RGB
            var ret = new Image<Bgr, byte>(modelImg.Width + dataImg.Width, Math.Max(modelImg.Height, dataImg.Height));
            Features2DToolbox.DrawMatches(modelImg.ToEmguGrayscale(), modelKeypoints,
                                          dataImg.ToEmguGrayscale(), dataKeypoints,
                                          matches, ret, lineColor, pointColor, null,
                                          Features2DToolbox.KeypointDrawType.DrawRichKeypoints);
            return ret.ToOPSImage();
        }
    }
}
