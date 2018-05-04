using OPS.Alignment;
using OPS.Cloud;
using OPS.Plumbing;

namespace OPS.Pipeline
{
    public class DetectedFeatures : JsonDataProduct
    {
        public string ObservationName;
        public ImageFeature[] Features;
    }
}
