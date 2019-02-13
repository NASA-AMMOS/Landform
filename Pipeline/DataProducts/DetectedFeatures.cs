using OPS.Alignment;
using OPS.Cloud;

namespace OPS.Pipeline
{
    public class DetectedFeatures : JsonDataProduct
    {
        public string ImageUrl;
        public ImageFeature[] Features;
    }
}
