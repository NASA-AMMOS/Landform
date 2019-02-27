using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
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
        public ImageFeature[] DetectPCASIFT(Image img, Image mask)
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

        public DetectedFeatures Detect(PipelineCore pipeline, string imageUrl, string maskUrl, string projectName,
                                       string productPath, int borderWidth = 10)
        {
            var img = pipeline.LoadImage(imageUrl);

            //do not mutate existing mask image
            Image mask = !string.IsNullOrEmpty(maskUrl) ? ((Image)pipeline.LoadImage(imageUrl).Clone()) : null;

            if (mask == null)
            {
                pipeline.LogVerbose("generating synthetic rover mask for {0}",
                                    StringHelper.GetLastUrlPathSegment(imageUrl));
                mask = RoverMask.Build(img);
            }
            
            //propagate invalid pixels from img to mask
            if (img.Metadata is PDSMetadata)
            {
                var parser = new PDSParser((PDSMetadata)img.Metadata);
                if (parser.HasMissingConstant)
                {
                    float[] missingVal = parser.MissingConstant.Select(x => (float)x).ToArray();
                    for (int idxRow = 0; idxRow < img.Height; idxRow++)
                    {
                        for (int idxCol = 0; idxCol < img.Width; idxCol++)
                        {
                            if (img.BandValuesEqual(idxRow, idxCol, missingVal))
                            {
                                mask[0, idxRow, idxCol] = 0;
                            }
                        }
                    }
                }
            }

            //add borders to mask
            borderWidth = Math.Min(mask.Height / 2, Math.Min(mask.Width / 2, borderWidth));
            for (int border = 0; border < borderWidth; border++)
            {
                //whole row
                for(int idxCol=0; idxCol < mask.Width; idxCol++)
                {
                    mask[0, border, idxCol] = 0;
                    mask[0, mask.Height - 1 - border, idxCol] = 0;
                }

                //whole column
                for (int idxRow = 0; idxRow < mask.Height; idxRow++)
                {
                    mask[0, idxRow, border] = 0;
                    mask[0, idxRow, mask.Width - 1 - border] = 0;
                }
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
