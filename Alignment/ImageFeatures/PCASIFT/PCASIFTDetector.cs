using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.XFeatures2D;
using OPS.Imaging;
using OPS.Imaging.Emgu;

namespace OPS.Alignment
{
    /// <summary>
    /// http://www.cs.ubc.ca/~lowe/papers/ijcv04.pdf
    /// </summary>
    public class PCASIFTDetector : SIFTDetector
    {
        private static readonly PCAKeypointProjector projector;

        static PCASIFTDetector()
        {
            projector = new PCAKeypointProjector(PCAKeypointProjector.DefaultTrainingSpace);
        }

        public override IEnumerable<ImageFeature> Detect(Image image, Image mask = null)
        {
            var keypoints = DetectKeypoints(image.ToEmguGrayscale(), (mask != null) ? mask.ToEmguGrayscale() : null);
            foreach (var keypoint in keypoints)
            {
                yield return (new PCASIFTFeature(keypoint));
            }
        }

        public override void AddDescriptors(Image image, IEnumerable<ImageFeature> features)
        {
            AddDescriptors(image.ToEmguGrayscale(), features);
        }

        public override void AddDescriptors(Image<Gray, byte> image, IEnumerable<ImageFeature> features)
        {
            projector.Project(image, features.Cast<PCASIFTFeature>());
        }
    }
}
