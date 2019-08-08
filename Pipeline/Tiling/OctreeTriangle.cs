using Microsoft.Xna.Framework;
using OPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public class OctreeTriangleNode : OctreeNodeContents
    {
        public VertexNode v0;
        public VertexNode v1;
        public VertexNode v2;
        Triangle tri;

        public OctreeTriangleNode(VertexNode v0, VertexNode v1, VertexNode v2)
        {
            this.v0 = v0;
            this.v1 = v1;
            this.v2 = v2;
            tri = new Triangle(v0.Vert.Position, v1.Vert.Position, v2.Vert.Position);
        }

        public BoundingBox Bounds()
        {
            return tri.Bounds();
        }

        public bool Intersects(BoundingBox other)
        {
            return this.tri.Intersects(other);
        }

        public double SquaredDistance(Vector3 xyz)
        {
            return tri.SquaredDistance(xyz);
        }
    }
}
