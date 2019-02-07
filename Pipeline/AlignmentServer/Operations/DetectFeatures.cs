using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using OPS.Imaging;
using OPS.Cloud;
using OPS.Pipeline;
using OPS.Alignment;

namespace OPS.Pipeline.AlignmentServer
{
    public class DetectFeaturesMessage : QueueMessage
    {
        public string ImageUrl;
        public Guid MaskGuid;
        public DetectFeaturesMessage() {}
        public DetectFeaturesMessage(string projectName) : base(projectName) {}
    }

    public class FeaturesDetectedMessage : QueueMessage
    {
        public string ImageUrl;
        public Guid MaskGuid;
        public Guid FeaturesGuid;
        public FeaturesDetectedMessage() {}
        public FeaturesDetectedMessage(string projectName) : base(projectName) {}
    }

    public class DetectFeatures : CloudPipelineOperation
    {
        private DetectFeaturesMessage message;
        private FeatureDetector detector = new FeatureDetector(FeatureDetector.DetectorType.ASIFT);

        public DetectFeatures(CloudPipeline pipeline, DetectFeaturesMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public void Process()
        {
            var project = Project.Find(pipeline, projectName);
            var res = detector.Detect(pipeline, message.ImageUrl, message.MaskGuid, projectName, project.ProductPath);
            if (res != null)
            {
                pipeline.SaveDataProduct(project.ProductPath, res, projectName);
                pipeline.MasterQueue.Enqueue(new FeaturesDetectedMessage()
                                             {
                                                 ImageUrl = message.ImageUrl,
                                                 MaskGuid = message.MaskGuid,
                                                 FeaturesGuid = res.Guid
                                             });
            }
        }
    }
}
