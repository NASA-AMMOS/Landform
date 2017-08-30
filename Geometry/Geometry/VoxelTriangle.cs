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

            //return Triangle.Bounds().Intersects(other);

            //bool intersects = TriangleBoxCollision.IsTriangleInBox(Triangle, other);
            //if (intersects)
            //{
            //    ; // Intersects
            //}
            //else
            //{
            //    ; // Doesn't intersect
            //}
            //return intersects;
        }

        public double SquaredDistance(Vector3 xyz)
        {
            return Triangle.SquaredDistance(xyz);
        }
    }
}
