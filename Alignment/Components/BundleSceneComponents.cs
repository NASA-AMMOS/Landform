using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace OPS.Alignment
{
    public class CameraRay : NodeComponent
    {
        public Vector3 Center;
        public Vector3 Direction;
    }
    public class PointCloudReference : NodeComponent
    {
        public string Path;
    }
}
