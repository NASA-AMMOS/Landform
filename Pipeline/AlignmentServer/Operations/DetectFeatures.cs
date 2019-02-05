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
        private static ASIFTDetector detector = new ASIFTDetector();

        private DetectFeaturesMessage message;

        public DetectFeatures(CloudPipeline pipeline, DetectFeaturesMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public void Process()
        {
            var project = Project.Find(pipeline, projectName);
            var img = pipeline.LoadImage(message.ImageUrl);

            Imaging.Image mask = null;
            if (message.MaskGuid == Guid.Empty)
            {
                pipeline.LogWarn("No mask for {0}", message.ImageUrl);
            }
            else
            {
                mask = pipeline.GetDataProduct<PngDataProduct>(project.ProductPath, message.MaskGuid, projectName).Image;
            }

            ImageFeature[] features = FindFeatures(message.ImageUrl, img, mask);
            if (features == null)
            {
                return;
            }
            
            var res = new DetectedFeatures() { ImageUrl = message.ImageUrl, Features = features };
            pipeline.SaveDataProduct(project.ProductPath, res, projectName);

            pipeline.MasterQueue.Enqueue(new FeaturesDetectedMessage()
            {
                ImageUrl = message.ImageUrl,
                MaskGuid = message.MaskGuid,
                FeaturesGuid = res.Guid
            });
        }

        public ImageFeature[] FindFeatures(string imgName, Imaging.Image img, Imaging.Image mask)
        {
            ImageFeature[] features;

            lock (detector)
            {
                try
                {
                    features = detector.Detect(img, mask).ToArray();
                }
                catch (Emgu.CV.Util.CvException ex)
                {
                    LogError("failed to detect for " + imgName, ex);
                    return null;
                }
            }

            return features.OrderByDescending(f => ((SIFTFeature)f).Response).Take(10000).ToArray();
        }
    }
}
