using System.Collections.Generic;
//using Emgu.CV.XFeatures2D;
//using Emgu.CV;
using OPS.Imaging;
//using OPS.Imaging.Emgu;
//using Emgu.CV.Structure;
using Microsoft.Xna.Framework;
using System;

namespace OPS.Alignment
{
    public class PCASIFTDetector : IFeatureDetector
    {
        Emgu.CV.XFeatures2D.SIFT sift;
        // For details, see http://www.cs.ubc.ca/~lowe/papers/ijcv04.pdf
        public PCASIFTDetector(int numFeatures = 0, int octaveLayers = 3, float contrastThreshold = 0.04f, float edgeThreshold = 10f, float sigma = 1.6f)
        {
            sift = new Emgu.CV.XFeatures2D.SIFT(numFeatures, octaveLayers, contrastThreshold, edgeThreshold, sigma);
        }

        public IEnumerable<ImageFeature> Detect(Image image, Image mask = null)
        {
            var emguImg = image;
            Image emguMask = (mask != null) ? (mask) : null;

            foreach (var kp in sift.Detect(emguImg, emguMask))
            {
                yield return new PCASIFTFeature(
                    new Vector2(kp.Point.X, kp.Point.Y),
                    kp.Size,
                    kp.Angle / 180f * Math.PI,
                    kp.Octave,
                    kp.Response);
            }
        }
    }
}
