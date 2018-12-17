using log4net;
using OPS.Alignment;
using OPS.Cloud;
using OPS.Pipeline.TileServer;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.AlignmentServer
{
    public class DetectFeatures
    {
        static ILog logger = LogManager.GetLogger(typeof(DetectFeatures));

        public DetectFeaturesMessage Message;
        public PipelineCore Pipeline;
        public TileServerCloud Cloud;
        public DetectFeatures(DetectFeaturesMessage message, PipelineCore pipeline, TileServerCloud cloud)
        {
            Message = message;
            Pipeline = pipeline;
            Cloud = cloud;
        }

        static ASIFTDetector detector = new ASIFTDetector();
        public void Process()
        {
            var img = Pipeline.Load(Message.Image);
            Imaging.Image mask = null;
            if (Message.MaskGuid == Guid.Empty)
            {
                logger.WarnFormat("No mask for {0}", Message.Image.DisplayName);
            }
            else
            {
                mask = Pipeline.Get<PngDataProduct>(Message.Project, Message.MaskGuid).Image;
            }

            ImageFeature[] features = FindFeatures(Message.Image.DisplayName, img, mask);
            if (features == null)
                return;
            
            var res = new DetectedFeatures
            {
                Features = features,
                ObservationName = Message.Image.DisplayName
            };
            Pipeline.Save(Message.Project, res);

            Cloud.MasterQueue.Enqueue(new FeaturesDetectedMessage()
            {
                Image = Message.Image,
                Project = Message.Project,
                MaskGuid = Message.MaskGuid,
                FeaturesGuid = res.Guid
            });
        }

        public static ImageFeature[] FindFeatures(string imgName, Imaging.Image img, Imaging.Image mask)
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
                    logger.Error("failed to detect for " + imgName, ex);
                    return null;
                }
            }

            return features.OrderByDescending(f => ((SIFTFeature)f).Response).Take(10000).ToArray();
        }
    }
}
