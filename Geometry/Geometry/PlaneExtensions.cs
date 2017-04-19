using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;

namespace OPS.Geometry
{
    public static class PlaneExtensions
    {
        const double PERPENDICULAR_EPS = 1e-6;
        const double INTERSECTION_CHECK_EPS = 1e-7; 

        /// <summary>
        /// Check if the line between points a and b intesects the plane
        /// If so return the normalized distance 0-1 along the line a->b
        /// Where the intersection occures
        /// Otherwise return null
        /// </summary>
        /// <param name="plane"></param>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static double? IntersectT(this Plane plane, Vector3 a, Vector3 b)
        {
            double denominator = Vector3.Dot(plane.Normal, b - a);
            if (Math.Abs(denominator) < PERPENDICULAR_EPS)
            {
                // Line is perpendicular to plane normal - no intersection
                return null;
            }
            double t = (plane.D - Vector3.Dot(plane.Normal, a)) / denominator;
            if (t < -INTERSECTION_CHECK_EPS || t > 1 + INTERSECTION_CHECK_EPS)
            {
                return null;
            }
            return MathE.Clamp(t,0,1);
        }

        /// <summary>
        /// Checks to see if the line between vertices a->b intersects with the plane
        /// If so returns the interpolated vertex where the intersection occures
        /// Otherwise returns null
        /// </summary>
        /// <param name="plane"></param>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static Vertex Intersect(this Plane plane, Vertex a, Vertex b)
        {
            double? t = IntersectT(plane, a.Position, b.Position);
            if(!t.HasValue)
            {
                return null;
            }
            return Vertex.Lerp(a, b, t.Value);
        }
    }
}
