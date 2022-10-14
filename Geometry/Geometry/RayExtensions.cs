using Microsoft.Xna.Framework;
using System;

namespace JPLOPS.Geometry
{
    public static class RayExtensions
    {
        public static bool ClosestIntersection(Ray r0, Ray r1, out double t0, out double t1)
        {
            Vector3 p0 = r0.Position,
                    p1 = r1.Position;
            Vector3 u0 = r0.Direction,
                    u1 = r1.Direction;
            Vector3 w0 = p0 - p1;
            double u0_u0 = u0.Dot(u0),
                   u0_u1 = u0.Dot(u1),
                   u1_u0 = u1.Dot(u1),
                   u0_w0 = u0.Dot(w0),
                   u1_w0 = u1.Dot(w0);

            if (Math.Abs(u0_u0 * u1_u0 - u0_u1 * u0_u1) < 1e-5)
            {
                t0 = 0;
                t1 = 0;
                return false;
            }
            t0 = (u0_u1 * u1_w0 - u1_u0 * u0_w0) / (u0_u0 * u1_u0 - u0_u1 * u0_u1);
            t1 = (u0_u0 * u1_w0 - u0_u1 * u0_w0) / (u0_u0 * u1_u0 - u0_u1 * u0_u1);
            return true;
        }

        public static void Transform(ref Ray ray, ref Matrix matrix, out Ray result)
        {
            Vector3 newPosition, newDirection;
            Vector3.Transform(ref ray.Position, ref matrix, out newPosition);
            Vector3.TransformNormal(ref ray.Direction, ref matrix, out newDirection);
            result = new Ray(newPosition, newDirection);
        }

        public static Ray Transform(Ray ray, Matrix matrix)
        {
            Ray result;
            Transform(ref ray, ref matrix, out result);
            return result;
        }
    }
}
