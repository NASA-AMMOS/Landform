using JPLOPS.ImageFeatures;

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
