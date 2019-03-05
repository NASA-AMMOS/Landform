using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV.Features2D;
using Emgu.CV.XFeatures2D;
using Emgu.CV;
using OPS.Imaging;
using OPS.Imaging.Emgu;
using Emgu.CV.Structure;
using Microsoft.Xna.Framework;
using Emgu.CV.Util;

namespace OPS.Alignment
{
    public class SIFT : IFeatureDetector, IDescriptorCalculator
    {
        Emgu.CV.XFeatures2D.SIFT sift;
        // For details, see http://www.cs.ubc.ca/~lowe/papers/ijcv04.pdf
        public SIFT(int numFeatures = 0, int octaveLayers = 3, float contrastThreshold = 0.04f, float edgeThreshold = 10f, float sigma = 1.6f)
        {
            sift = new Emgu.CV.XFeatures2D.SIFT(numFeatures, octaveLayers, contrastThreshold, edgeThreshold, sigma);
        }

        public IEnumerable<ImageFeature> Detect(Image image, Image mask = null)
        {
            var emguImg = image.ToEmguGrayscale();
            Image<Gray, byte> emguMask = (mask != null) ? (mask.ToEmguGrayscale()) : null;

            foreach (var kp in sift.Detect(emguImg, emguMask))
            { 
                yield return new SIFTFeature(
                    new Vector2(kp.Point.X, kp.Point.Y),
                    kp.Size,
                    kp.Angle,
                    kp.Octave,
                    kp.Response);
            }
        }

        public void ComputeDescriptors(Image image, IEnumerable<ImageFeature> features)
        {
            var emguImg = image.ToEmguGrayscale();
            var keypoints = features.Cast<SIFTFeature>().CastToMKeyPoint().ToArray();
            var descriptors = new Matrix<float>(keypoints.Length, 128);
            sift.Compute(emguImg, new VectorOfKeyPoint(keypoints), descriptors);
            float[,] descData = descriptors.Data;
            int i = 0;
            foreach (var feature in features)
            {
                byte[] data = new byte[128];
                for (int j = 0; j < 128; j++)
                {
                    data[j] = (byte)descData[i, j];
                }
                feature.Descriptor = new SIFTDescriptor(data);
                i++;
            }
        }
    }
}
