using System.Collections.Generic;
using System.Linq;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.Features2D;
using Emgu.CV.Util;
using JPLOPS.Imaging;
using JPLOPS.Imaging.Emgu;

namespace JPLOPS.ImageFeatures
{
    public abstract class FeatureDetectorBase
    {
        public abstract Feature2D MakeDetector();

        public virtual Feature2D MakeExtractor()
        {
            return MakeDetector();
        }

        public virtual IEnumerable<ImageFeature> Detect(Image image, Image mask = null)
        {
            return Detect(image.ToEmguGrayscale(), (mask != null) ? mask.ToEmguGrayscale() : null);
        }

        public IEnumerable<ImageFeature> Detect(Image<Gray, byte> image, Image<Gray, byte> mask = null)
        {
            foreach (var keypoint in DetectKeypoints(image, mask))
            {
                yield return new SIFTFeature(keypoint);
            }
        }

        public MKeyPoint[] DetectKeypoints(Image<Gray, byte> image, Image<Gray, byte> mask = null)
        {
            return MakeDetector().Detect(image, mask);
        }

        public virtual void AddDescriptors(Image image, IEnumerable<ImageFeature> features)
        {
            AddDescriptors(image.ToEmguGrayscale(), features);
        }

        public virtual void AddDescriptors(Image<Gray, byte> image, IEnumerable<ImageFeature> features)
        {
            var keypoints = features.Cast<SIFTFeature>().CastToMKeyPoint().ToArray();
            if (keypoints.Length == 0)
            {
                return;
            }
            var extractor = MakeExtractor();
            var descriptorMatrix = new Matrix<float>(keypoints.Length, extractor.DescriptorSize);
            extractor.Compute(image, new VectorOfKeyPoint(keypoints), descriptorMatrix);
            int i = 0;
            foreach (var feature in features)
            {
                byte[] data = new byte[extractor.DescriptorSize];
                for (int j = 0; j < extractor.DescriptorSize; j++)
                {
                    data[j] = (byte)descriptorMatrix.Data[i, j];
                }
                feature.Descriptor = new SIFTDescriptor(data);
                i++;
            }
        }

        /// <summary>
        /// check that feature is in bounds of image and mask and that none of the pixels in its rect are masked  
        /// </summary>
        public static bool CheckValidFeature(ImageFeature feature, Image<Gray, byte> image, Image<Gray, byte> mask,
                                             int extraRadius = 0)
        {
            int row = (int)feature.Location.Y;
            int col = (int)feature.Location.X;
            int radius = (int)(0.5*((SIFTFeature)feature).Size); //yes, round down
            radius += extraRadius;
            int minR = row - radius;
            int maxR = row + radius;
            if (minR < 0 || maxR >= image.Height || maxR >= mask.Height)
            {
                return false;
            }
            int minC = col - radius;
            int maxC = col + radius;
            if (minC < 0 || maxC >= image.Width || maxC >= mask.Width)
            {
                return false;
            }
            bool ok = true;
            for (int r = minR; ok && r <= maxR; r++)
            {
                for (int c = minC; ok && c <= maxC; c++)
                {
                    ok &= mask.Data[r, c, 0] != 0;
                }
            }
            return ok;
        }
    }
}
