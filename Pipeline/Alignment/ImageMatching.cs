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
            var modelFrame = Frame.Find(pipeline, projectName, modelFrameName);
            var dataFrame = Frame.Find(pipeline, projectName, dataFrameName);
            var opts = new BuildSceneGraph.Options() { UseTransformPriors = true, LoadFeatures = true };
            BuildSceneGraph builder = new BuildSceneGraph(pipeline, projectName, opts);
            AlignmentScene scene = builder.BuildBottomUp(new Frame[] { modelFrame, dataFrame });
            return ComputeCorrespondence(pipeline, scene, modelUrl, dataUrl);
        }

        public static ComputedCorrespondence ComputeCorrespondence(PipelineCore pipeline, AlignmentScene scene,
                                                                   string modelUrl, string dataUrl)
        {
            Debug.Assert(scene.ObservationUrlToNode.ContainsKey(modelUrl));
            Debug.Assert(scene.ObservationUrlToNode.ContainsKey(dataUrl));

            //IFeatureMatcher matcher = new EmguSIFTMatcher();
            //IFeatureMatcher matcher = new KnownGeometryMatcher();
            //IFeatureMatcher matcher = new BruteForceMatcher();
            IFeatureMatcher matcher = new CascadeHashingMatcher();
            var matches = matcher.Match(scene, modelUrl, dataUrl);
            if (matches.Count < MIN_MATCHES)
            {
                return null;
            }

            List<IMatchFilter> filters = new List<IMatchFilter>();
            filters.Add(new KnownGeometryFilter(pipeline.Logger));
            filters.Add(new MoisanStivalFilter(pipeline.Logger));
            //filters.Add(new GTMFilter());

            foreach (var filter in filters)
            {
                int oldCount = matches.Count;
                matches = filter.Filter(scene, matches);
                pipeline.LogVerbose("{0}: {1} -> {2}", filter.GetType().Name, oldCount, matches.Count);
                if (matches.Count < MIN_MATCHES)
                {
                    return null;
                }
            }

            var modelNode = scene.ObservationUrlToNode[modelUrl];
            var dataNode = scene.ObservationUrlToNode[dataUrl];

            var modelObs = modelNode.GetComponent<NodeObservation>().Observation;
            var dataObs = dataNode.GetComponent<NodeObservation>().Observation;

            return new ComputedCorrespondence()
            {
                ModelFeaturesGuid = modelObs.FeaturesGuid,
                DataFeaturesGuid = dataObs.FeaturesGuid,
                Correspondence = matches
            };
        }

        public static bool SaveOverlap(PipelineCore pipeline, string projectName, AlignmentScene scene,
                                       ComputedCorrespondence correspondence)
        {
            var modelUrl = correspondence.Correspondence.ModelImageUrl;
            var dataUrl = correspondence.Correspondence.DataImageUrl;

            var modelNode = scene.ObservationUrlToNode[modelUrl];
            var dataNode = scene.ObservationUrlToNode[dataUrl];

            var modelObs = modelNode.GetComponent<NodeObservation>().Observation;
            var dataObs = dataNode.GetComponent<NodeObservation>().Observation;

            return SaveOverlap(pipeline, projectName, correspondence != null ? correspondence.Guid : Guid.Empty,
                               modelObs.Name, dataObs.Name);
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
    }
}
