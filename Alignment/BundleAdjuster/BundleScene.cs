using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public class NodeImageReference : NodeComponent
    {
        public ImageRef Reference;
    }

    public class AdjustedNode : NodeComponent { }

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
