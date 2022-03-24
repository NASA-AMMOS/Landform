using JPLOPS.ImageFeatures;
using JPLOPS.Cloud;

namespace JPLOPS.Pipeline
{
    public class FeatureMatchesDataProduct : JsonDataProduct
    {
        public FeatureMatch[] Matches;

        public FeatureMatchesDataProduct() { }

        public FeatureMatchesDataProduct(FeatureMatch[] matches)
        {
            Matches = matches;
        }
    }
}
