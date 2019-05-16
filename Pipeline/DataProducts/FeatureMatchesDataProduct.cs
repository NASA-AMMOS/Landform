using OPS.Alignment;
using OPS.Cloud;

namespace OPS.Pipeline
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
