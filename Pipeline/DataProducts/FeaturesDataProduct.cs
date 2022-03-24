using JPLOPS.ImageFeatures;
using JPLOPS.Cloud;

namespace JPLOPS.Pipeline
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
