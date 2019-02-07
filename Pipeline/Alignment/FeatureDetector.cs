using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using OPS.Alignment;

namespace OPS.Pipeline
{
    public class FeatureDetector
    {
        public enum DetectorType
        {
            SIFT,
            ASIFT,
            PCASIFT
        }

        public readonly DetectorType Detector;

        private readonly int maxFeatures;

        public FeatureDetector(DetectorType detector, int maxFeatures = 10000)
        {
            this.Detector = detector;
            this.maxFeatures = maxFeatures;
        }

        private PCAKeypointProjector projector;
        public ImageFeature[] DetectPCASIFT(Imaging.Image img, Imaging.Image mask)
        {
            if (projector == null)
            {
                string gpcafile = PCAKeypointProjector.DefaultTrainingSpace;
                projector = new PCAKeypointProjector(gpcafile, false);
            }
            List<PCASIFTFeature> features = new PCASIFTDetector().Detect(img, mask).Cast<PCASIFTFeature>().ToList();
            projector.Project(img, features, 1);
            return features.ToArray();
        }

        public ImageFeature[] Detect(Image img, Image mask)
        {
            ImageFeature[] features = null;
            switch (Detector)
            {
                case DetectorType.PCASIFT: features = DetectPCASIFT(img, mask); break;
                case DetectorType.ASIFT: features = (new ASIFTDetector()).Detect(img, mask).ToArray(); break;
                case DetectorType.SIFT: features = (new SIFT()).Detect(img, mask).ToArray(); break;
                default: throw new NotImplementedException("unhandled feature detector " + Detector);
            }
            return features.OrderByDescending(f => ((SIFTFeature)f).Response).Take(maxFeatures).ToArray();
        }

        public DetectedFeatures Detect(PipelineCore pipeline, string imageUrl, Guid maskGuid, string projectName,
                                       string productPath)
        {
            var img = pipeline.LoadImage(imageUrl);

            Image mask = null;
            if (maskGuid == Guid.Empty)
            {
                pipeline.LogWarn("no mask for {0}", imageUrl);
            }
            else
            {
                mask = pipeline.GetDataProduct<PngDataProduct>(productPath, maskGuid, projectName).Image;
            }

            try
            {
                return new DetectedFeatures() { ImageUrl = imageUrl, Features = Detect(img, mask) };
            }
            catch (Emgu.CV.Util.CvException ex)
            {
                pipeline.LogError("failed to detect {0} features for {1}", Detector, imageUrl, ex);
                return null;
            }
        }
    }
}
