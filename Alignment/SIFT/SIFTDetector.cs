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

namespace OPS.Alignment
{
    public class SIFTDetector : IFeatureDetector
    {
        SIFT sift;
        // For details, see http://www.cs.ubc.ca/~lowe/papers/ijcv04.pdf
        public SIFTDetector(int numFeatures = 0, int octaveLayers = 3, float contrastThreshold = 0.04f, float edgeThreshold = 10f, float sigma = 1.6f)
        {
            sift = new SIFT(numFeatures, octaveLayers, contrastThreshold, edgeThreshold, sigma);
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
    }
}
