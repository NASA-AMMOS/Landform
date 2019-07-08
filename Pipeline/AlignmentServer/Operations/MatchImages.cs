using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;

//TODO: refactor so that local codepath does not have cloud dependencies
//https://github.jpl.nasa.gov/OnSight/Landform/issues/596
using QueueMessage = OPS.Cloud.QueueMessage;

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
        public MatchImagesMessage() { }
        public MatchImagesMessage(string projectName) : base(projectName) { }
    }

    public class ImagesMatchedMessage : QueueMessage
    {
        public string ModelImageUrl;
        public string DataImageUrl;
        public Guid CorrespondenceGuid;
        public ImagesMatchedMessage() { }
        public ImagesMatchedMessage(string projectName) : base(projectName) { }
    }

    public class MatchImages : PipelineOperation
    {
        private readonly MatchImagesMessage message;

        public MatchImages(PipelineCore pipeline, MatchImagesMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public void Process()
        {
            var modelUrl = message.ModelImageUrl;
            var dataUrl = message.DataImageUrl;
            var pairName = (new URLPair(modelUrl, dataUrl)).ToStringShort();

            pipeline.LogInfo("matching features for {0} in project {1}", pairName, projectName);

            var result = ImageMatching.ComputeCorrespondence(pipeline, projectName, modelUrl, dataUrl,
                                                             message.ModelFrameName, message.DataFrameName);

            Guid guid = Guid.Empty;
            if (result != null && result.Correspondence != null)
            {
                pipeline.LogInfo("matched features for {0} in project {1}", pairName, projectName);

                var project = Project.Find(pipeline, projectName);
                pipeline.SaveDataProduct(project.ProductPath, result, projectName);

                //matcher could have swapped model and data images
                modelUrl = result.Correspondence.ModelImageUrl;
                dataUrl = result.Correspondence.DataImageUrl;
                guid = result.Guid;
            }
            else
            {
                pipeline.LogInfo("insufficient feature match for {0} in project {1}", pairName, projectName);
            }

            pipeline.EnqueueToMaster(new ImagesMatchedMessage(projectName)
                                     { ModelImageUrl = modelUrl, DataImageUrl = dataUrl, CorrespondenceGuid = guid });
        }
    }
}
