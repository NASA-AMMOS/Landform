using System;
using Microsoft.Xna.Framework;
using JPLOPS.MathExtensions;

namespace JPLOPS.Geometry
{
    public static class PlaneExtensions
    {
        const double PERPENDICULAR_EPS = 1e-6;
        const double INTERSECTION_CHECK_EPS = 1e-7; 

        /// <summary>
        /// Check if the line between points a and b intesects the plane
        /// If so return the normalized distance 0-1 along the line a->b
        /// Where the intersection occurs
        /// Otherwise return null
        /// </summary>
        public static double? IntersectT(this Plane plane, Vector3 a, Vector3 b)
        {
            // Plane.D is the negative of the distance from the origin to the plane in the direction of the normal
            // because it's the coefficient in the equation distanceToPlane = dot(point, normal) + D

            //(1) parametric point on segment as a function of t:          p = a + t * (b - a)
            //(2) implicit point on plane with normal N and coefficient D: 0 = dot(p, N) + D

            //substitute (1) into (2) and solve for t:
            //0 = dot(a + t * (b - a), N) + D
            //0 = dot(a, N) + t * dot(b - a, N) + D
            //t = -(dot(a, N) + D) / dot(b - a, N)

            double denominator = Vector3.Dot(plane.Normal, b - a);

            if (Math.Abs(denominator) < PERPENDICULAR_EPS)
            {
                // Line is perpendicular to plane normal - no intersection
                return null;
            }

            double t = - (plane.D + Vector3.Dot(plane.Normal, a)) / denominator;

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
        public static Vertex Intersect(this Plane plane, Vertex a, Vertex b)
        {
            double? t = IntersectT(plane, a.Position, b.Position);
            if (!t.HasValue)
            {
                return null;
            }
            return Vertex.Lerp(a, b, t.Value);
        }

        public static Plane FromPointAndNormal(Vector3 point, Vector3 normal)
        {
            // Plane.D is the negative of the distance from the origin to the plane in the direction of the normal
            // because it's the coefficient in the equation distanceToPlane = dot(point, normal) + D
            return new Plane(normal, -Vector3.Dot(normal, point));
        }
    }
}
