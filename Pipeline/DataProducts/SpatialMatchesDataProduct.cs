using OPS.Alignment;
using OPS.Cloud;

namespace OPS.Pipeline
{
    public class SpatialMatchesDataProduct : JsonDataProduct
    {
        public SpatialMatch[] Matches;

        public SpatialMatchesDataProduct() { }

        public SpatialMatchesDataProduct(SpatialMatch[] matches)
        {
            Matches = matches;
        }
    }
}
