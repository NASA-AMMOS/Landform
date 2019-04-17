using OPS.Alignment;
using OPS.Cloud;

namespace OPS.Pipeline
{
    public class FeaturesDataProduct : JsonDataProduct
    {
        public ImageFeature[] Features;

        public FeaturesDataProduct() { }

        public FeaturesDataProduct(ImageFeature[] features)
        {
            Features = features;
        }
    }
}
