using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;

namespace OPS.Geometry
{
    public static class BoundingBoxExtensions
    {
        /// <summary>
        /// Returns the size of the bounding box (max-min)
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        public static Vector3 Size(this BoundingBox box)
        {
            return box.Max - box.Min;
        }
        
        /// <summary>
        /// Returns the squared distance between the closest points on to bounding boxes
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static double ClosestDistanceSquared(this BoundingBox a, BoundingBox b)
        {
            double x = AxisSeparationDistance(a.Min.X, a.Max.X, b.Min.X, b.Max.X);
            double y = AxisSeparationDistance(a.Min.Y, a.Max.Y, b.Min.Y, b.Max.Y);
            double z = AxisSeparationDistance(a.Min.Z, a.Max.Z, b.Min.Z, b.Max.Z);
            return (x * x) + (y * y) + (z * z);
        }

        /// <summary>
        /// Returns a maximum possible squared difference between two bounding boxes even if they overlap
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static double FurthestDistanceSquared(this BoundingBox a, BoundingBox b)
        {
            BoundingBox union = Union(a, b);
            return Size(union).LengthSquared();
        }

        /// <summary>
        /// Finds the minimal distance between the ranges [amin,amax] and [bmin, bmax]
        /// Returns 0 if the ranges overlap
        /// </summary>
        /// <param name="aMin"></param>
        /// <param name="aMax"></param>
        /// <param name="bMin"></param>
        /// <param name="bMax"></param>
        /// <returns></returns>
        private static double AxisSeparationDistance(double aMin, double aMax, double bMin, double bMax)
        {
            if (bMin > aMax)
            {
                return bMin - aMax;
            }
            if (aMin > bMax)
            {
                return aMin - bMax;
            }
            return 0;
        }

        /// <summary>
        /// Returns the union of all inputs
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        public static BoundingBox Union(params BoundingBox[] boxes)
        {
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;

            for (int i = 0; i < boxes.Length; i++)
            {
                minX = Math.Min(minX, boxes[i].Min.X);
                minY = Math.Min(minY, boxes[i].Min.Y);
                minZ = Math.Min(minZ, boxes[i].Min.Z);
                maxX = Math.Max(maxX, boxes[i].Max.X);
                maxY = Math.Max(maxY, boxes[i].Max.Y);
                maxZ = Math.Max(maxZ, boxes[i].Max.Z);
            }

            return new BoundingBox(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ));
        }

        public static double MaxDimension(this BoundingBox box)
        {
            return MathE.Max(box.Size().ToDoubleArray());
        }

        public static Vector3 Center(this BoundingBox box)
        {
            return (box.Max + box.Min) / 2;
        }

        /// <summary>
        /// Returns true if the inner is totaly inside or equal to the outer
        /// Note that this method is similar to BoundingBox.Contains except that it allows for floating point
        /// error.
        /// </summary>
        /// <param name="inner"></param>
        /// <param name="outer"></param>
        /// <param name="epsilon"></param>
        /// <returns></returns>
        public static bool FuzzyContains(this BoundingBox outer, BoundingBox inner, double epsilon = MathE.EPSILON)
        {
            if (inner.Min.X <= outer.Min.X - epsilon ||
                inner.Max.X >= outer.Max.X + epsilon ||
                inner.Min.Y <= outer.Min.Y - epsilon ||
                inner.Max.Y >= outer.Max.Y + epsilon ||
                inner.Min.Z <= outer.Min.Z - epsilon ||
                inner.Max.Z >= outer.Max.Z + epsilon)
            {
                return false;
            }
            return true;
        }

        public static RTree.Rectangle ToRectangle(this BoundingBox box)
        {
            return new RTree.Rectangle((float)box.Min.X, (float)box.Min.Y,
                                 (float)box.Max.X, (float)box.Max.Y,
                                 (float)box.Min.Z, (float)box.Max.Z);
        }

        public static BoundingBox ToBoundingBox(this RTree.Rectangle rect)
        {
            RTree.dimension dimx = rect.get(0).Value;
            RTree.dimension dimy = rect.get(1).Value;
            RTree.dimension dimz = rect.get(2).Value;
            return new BoundingBox(new Vector3(dimx.min, dimy.min, dimz.min),
                                   new Vector3(dimx.max, dimy.max, dimz.max));
        }

        /// <summary>
        /// Returns true if the given triangle intersects with a bounding box
        /// </summary>
        /// <param name="box"></param>
        /// <param name="tri"></param>
        /// <returns></returns>
        public static bool Intersects(this BoundingBox box, Triangle tri)
        {
            return tri.Intersects(box);
        }
    }
}
