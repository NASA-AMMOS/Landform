using System.Collections.Generic;
using System.Linq;
using Emgu.CV;
using Emgu.CV.Structure;
using JPLOPS.Imaging;
using JPLOPS.Imaging.Emgu;

namespace JPLOPS.ImageFeatures
{
    /// <summary>
    /// http://www.cs.ubc.ca/~lowe/papers/ijcv04.pdf
    /// </summary>
    public class PCASIFTDetector : SIFTDetector
    {
        private static readonly PCAKeypointProjector projector;

        static PCASIFTDetector()
        {
            projector = new PCAKeypointProjector();
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
