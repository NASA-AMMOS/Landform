using OPS.Alignment;
using OPS.Cloud;

namespace OPS.Pipeline
{
    public class DetectedFeatures : JsonDataProduct
    {
        public string ObservationName;
        public ImageFeature[] Features;
    }
}
