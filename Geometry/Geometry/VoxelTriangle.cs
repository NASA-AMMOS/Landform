using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace OPS.Geometry
{
    class VoxelTriangle : OctreeNodeContents
    {
        public Triangle Triangle { get; internal set; }
        public BasePoint[] BasePoints = null;
        public List<int> TraversalPath;

        public VoxelTriangle(Triangle tri)
        {
            Triangle = tri;
        }

        public BoundingBox Bounds()
        {
            return Triangle.Bounds();
        }

        public bool Intersects(BoundingBox other)
        {
            return Triangle.Clip(other).Count() > 0;
        }

        public double SquaredDistance(Vector3 xyz)
        {
            return Triangle.SquaredDistance(xyz);
        }
    }
}
