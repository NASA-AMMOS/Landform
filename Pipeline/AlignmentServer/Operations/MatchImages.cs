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

namespace OPS.Pipeline.AlignmentServer
{
    public class MatchImagesMessage : QueueMessage
    {
        public string ModelImageUrl;
        public string ModelFrameName;
        public Guid ModelFeaturesGuid;
        public string DataImageUrl;
        public string DataFrameName;
        public Guid DataFeaturesGuid;
        public int OverlapIndex;
        public MatchImagesMessage() { }
        public MatchImagesMessage(string projectName) : base(projectName) { }
    }

    public class ImagesMatchedMessage : QueueMessage
    {
        public string ModelImageUrl;
        public string DataImageUrl;
        public int OverlapIndex;
        public Guid CorrespondenceGuid;
        public ImagesMatchedMessage() { }
        public ImagesMatchedMessage(string projectName) : base(projectName) { }
    }

    public class MatchImages : CloudPipelineOperation
    {
        static readonly int MIN_MATCHES = 20;

        private readonly MatchImagesMessage message;

        public MatchImages(CloudPipeline pipeline, MatchImagesMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        private ComputedCorrespondence DoCorrespondence()
        {
            var project = Project.Find(pipeline, projectName);
            var productPath = project.ProductPath;

            var modelUrl = message.ModelImageUrl;
            var dataUrl = message.DataImageUrl;

            var modelFeatures =
                pipeline.GetDataProduct<DetectedFeatures>(productPath, message.ModelFeaturesGuid, projectName).Features;
            var dataFeatures =
                pipeline.GetDataProduct<DetectedFeatures>(productPath, message.DataFeaturesGuid, projectName).Features;

            var modelFrame = Frame.Find(pipeline, projectName, message.ModelFrameName);
            var dataFrame = Frame.Find(pipeline, projectName, message.DataFrameName);
            BuildSceneGraph builder = new BuildSceneGraph(pipeline, projectName,
                                                          new BuildSceneGraph.Options() { UseTransformPriors = true });
            AlignmentScene scene = builder.BuildBottomUp(new Frame[] { modelFrame, dataFrame });
            scene.DetectedFeatures[modelUrl] = modelFeatures;
            scene.DetectedFeatures[dataUrl] = dataFeatures;
            Debug.Assert(scene.ObservationUrlToNode.ContainsKey(modelUrl));
            Debug.Assert(scene.ObservationUrlToNode.ContainsKey(dataUrl));

            //IFeatureMatcher matcher = new EmguSIFTMatcher();
            //IFeatureMatcher matcher = new KnownGeometryMatcher(url => scene.ObservationUrlToNode[url]);
            //IFeatureMatcher matcher = new BruteForceMatcher();
            IFeatureMatcher matcher = new CascadeHashingMatcher();
            var matches = matcher.Match(modelFeatures, dataFeatures, modelUrl, dataUrl);
            if (matches.Count < MIN_MATCHES)
            {
                return null;
            }

            List<IMatchFilter> filters = new List<IMatchFilter>();
            filters.Add(new KnownGeometryFilter(url => scene.ObservationUrlToNode[url], pipeline.Logger));
            filters.Add(new MoisanStivalFilter(url => scene.ObservationUrlToNode[url], pipeline.Logger));
            //filters.Add(new GTMFilter());

            foreach (var filter in filters)
            {
                int oldCount = matches.Count;
                matches = filter.Filter(modelFeatures, dataFeatures, matches);
                pipeline.LogVerbose("{0}: {1} -> {2}", filter.GetType().Name, oldCount, matches.Count);
                if (matches.Count < MIN_MATCHES)
                {
                    return null;
                }
            }

            if (modelUrl.ToLower() == dataUrl.ToLower())
            {
                //TODO ???
                pipeline.LogWarn("match images model URL same as data URL: {0}", modelUrl);
                var tmp = message.ModelFeaturesGuid;
                message.ModelFeaturesGuid = message.DataFeaturesGuid;
                message.DataFeaturesGuid = tmp;
            }

            var res = new ComputedCorrespondence
            {
                ModelFeaturesGuid = message.ModelFeaturesGuid,
                DataFeaturesGuid = message.DataFeaturesGuid,
                Correspondence = matches
            };

            pipeline.SaveDataProduct(project.ProductPath, res, projectName);

            return res;
        }

        public void Process()
        {
            var modelUrl = message.ModelImageUrl;
            var dataUrl = message.DataImageUrl;

            var pair = string.Format("({0}, {1})", 
                                     StringHelper.GetLastUrlPathSegment(modelUrl),
                                     StringHelper.GetLastUrlPathSegment(dataUrl));

            pipeline.LogInfo("matching features for image pair {0} in project {1}", pair, projectName);

            var result = DoCorrespondence();

            Guid guid = Guid.Empty;

            if (result != null && result.Correspondence != null)
            {
                pipeline.LogInfo("matched features for image pair {0} in project {1}", pair, projectName);

                //some matchers may swap model and data images, but why?
                modelUrl = result.Correspondence.ModelImageUrl;
                dataUrl = result.Correspondence.DataImageUrl;
                guid = result.Guid;
            }
            else
            {
                pipeline.LogError("failed to match features for image pair {0} in project {1}", pair, projectName);
            }

            pipeline.MasterQueue.Enqueue(new ImagesMatchedMessage(projectName)
            {
                ModelImageUrl = modelUrl,
                DataImageUrl = dataUrl,
                OverlapIndex = message.OverlapIndex,
                CorrespondenceGuid = guid
            });
        }
    }
}
