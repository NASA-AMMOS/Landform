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
        /// Returns true if the this box is totaly inside or equal to the outer box
        /// Note that this method is similar to BoundingBox.Contains except that it allows for floating point
        /// error.
        /// </summary>
        /// <param name="inner"></param>
        /// <param name="outer"></param>
        /// <param name="epsilon"></param>
        /// <returns></returns>
        public static bool Inside(this BoundingBox inner, BoundingBox outer, double epsilon = MathE.EPSILON)
        {
            Vector3 size = outer.Size();
            if (inner.Min.X <= outer.Min.X - epsilon ||
                inner.Max.X >= outer.Min.X + size.X + epsilon ||
                inner.Min.Y <= outer.Min.Y - epsilon ||
                inner.Max.Y >= outer.Min.Y + size.Y + epsilon ||
                inner.Min.Z <= outer.Min.Z - epsilon ||
                inner.Max.Z >= outer.Min.Z + size.Z + epsilon)
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
    }
}
