using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Alignment;

namespace OPS.Pipeline.AlignmentServer
{
    public class DetectFeaturesMessage : PipelineMessage
    {
        public string ImageUrl;
        public string MaskUrl;
        public DetectFeaturesMessage() {}
        public DetectFeaturesMessage(string projectName) : base(projectName) {}

        public override string Info()
        {
            return string.Format("[{0}] DetectFeatures image {1}", ProjectName, ImageUrl);
        }
    }

    public class FeaturesDetectedMessage : PipelineMessage
    {
        public string ImageUrl;
        public Guid FeaturesGuid;
        public FeaturesDetectedMessage() {}
        public FeaturesDetectedMessage(string projectName) : base(projectName) {}
    }

    public class DetectFeatures : PipelineOperation
    {
        private DetectFeaturesMessage message;

        public DetectFeatures(PipelineCore pipeline, DetectFeaturesMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public void Process()
        {
            var project = Project.Find(pipeline, projectName);
            var detector = new FeatureDetector(pipeline, MissionSpecific.GetInstance(project.Mission).GetMasker());
            var shortUrl = StringHelper.GetLastUrlPathSegment(message.ImageUrl);
            LogLess("detecting features for image {0} in project {1}", shortUrl, project.Name);
            var res = detector.Detect(message.ImageUrl, message.MaskUrl, project);
            if (res != null)
            {
                pipeline.SaveDataProduct(project.ProductPath, res, projectName);
                LogLess("detected features for image {0} in project {1}", shortUrl, project.Name);
                pipeline.EnqueueToMaster(new FeaturesDetectedMessage()
                                         { ImageUrl = message.ImageUrl, FeaturesGuid = res.Guid });
            }
            else
            {
                LogWarn("failed to detect features for image {0} in project {1}", shortUrl, project.Name);
                //not fatal to fail on one image
            }
        }
    }
}
