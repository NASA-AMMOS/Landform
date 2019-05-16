using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Alignment;

namespace OPS.Pipeline.AlignmentServer
{
    public class DetectFeaturesMessage : QueueMessage
    {
        public string ImageUrl;
        public string MaskUrl;
        public DetectFeaturesMessage() {}
        public DetectFeaturesMessage(string projectName) : base(projectName) {}
    }

    public class FeaturesDetectedMessage : QueueMessage
    {
        public string ImageUrl;
        public Guid FeaturesGuid;
        public FeaturesDetectedMessage() {}
        public FeaturesDetectedMessage(string projectName) : base(projectName) {}
    }

    public class DetectFeatures : CloudPipelineOperation
    {
        private DetectFeaturesMessage message;

        public DetectFeatures(CloudPipeline pipeline, DetectFeaturesMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public void Process()
        {
            var project = Project.Find(pipeline, projectName);
            var detector = new FeatureDetector(pipeline, MissionSpecific.GetInstance(project.Mission).GetMasker());
            var shortUrl = StringHelper.GetLastUrlPathSegment(message.ImageUrl);
            pipeline.LogInfo("detecting features for image {0} in project {1}", shortUrl, project.Name);
            var res = detector.Detect(message.ImageUrl, message.MaskUrl, projectName, project.ProductPath);
            if (res != null)
            {
                pipeline.SaveDataProduct(project.ProductPath, res, projectName);
                pipeline.LogInfo("detected features for image {0} in project {1}", shortUrl, project.Name);
                pipeline.MasterQueue.Enqueue(new FeaturesDetectedMessage()
                                             {
                                                 ImageUrl = message.ImageUrl,
                                                 FeaturesGuid = res.Guid
                                             });
            }
            else
            {
                pipeline.LogError("failed to detect features for image {0} in project {1}", shortUrl, project.Name);
            }
        }
    }
}
