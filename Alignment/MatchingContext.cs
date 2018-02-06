using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public class MatchingContext
    {
        public Dictionary<ImageRef, ImageFeature[]> DetectedFeatures;
        public Dictionary<UnorderedImagePair, ImagePairCorrespondence> Correspondences;
        public HashSet<UnorderedImagePair> Overlaps;

        public MatchingContext()
        {
            DetectedFeatures = new Dictionary<ImageRef, ImageFeature[]>();
            Correspondences = new Dictionary<UnorderedImagePair, ImagePairCorrespondence>();
            Overlaps = new HashSet<UnorderedImagePair>();
        }
    }
}
