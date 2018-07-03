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

        public void ComputeDescriptors(Image image, IEnumerable<ImageFeature> _features)
        {
            List<SIFTFeature> features = _features.Cast<SIFTFeature>().ToList();
            var emguImg = image.ToEmguGrayscale();
            VectorOfKeyPoint vokp = new VectorOfKeyPoint(features.Select(f =>
            {
                MKeyPoint kp = new MKeyPoint();
                kp.Point = new System.Drawing.PointF((float)f.Location.X, (float)f.Location.Y);
                kp.Angle = (float)f.Angle;
                kp.Octave = f.Octave;
                kp.Response = (float)f.Response;
                kp.Size = (float)f.Size;
                return kp;
            }).ToArray());

            Matrix<float> descriptors = new Matrix<float>(features.Count, 128);
            sift.Compute(emguImg, vokp, descriptors);

            float[,] descData = descriptors.Data;
            for (int i = 0; i < features.Count; i++)
            {
                byte[] data = new byte[128];
                for (int j = 0; j < 128; j++)
                {
                    data[j] = (byte)descData[i, j];
                }
                features[i].Descriptor = new SIFTDescriptor(data);
            }
        }
    }
}
