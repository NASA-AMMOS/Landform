using Microsoft.Xna.Framework;

namespace JPLOPS.Pipeline
{
    public class SpatialMatch
    {
        public Vector3 ModelPoint;
        public Vector3 DataPoint;

        public SpatialMatch(Vector3 modelPoint, Vector3 dataPoint)
        {
            ModelPoint = modelPoint;
            DataPoint = dataPoint;
        }
    }

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
