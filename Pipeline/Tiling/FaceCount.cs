using JPLOPS.Geometry;

namespace JPLOPS.Pipeline
{
    public class FaceCount : NodeComponent
    {
        public int NumTris;

        public FaceCount() { }

        public FaceCount(int numTris)
        {
            NumTris = numTris;
        }
    }
}
