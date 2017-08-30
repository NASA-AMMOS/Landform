using Microsoft.Xna.Framework;
using OPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    class TriangleBoxCollision
    {
        public static bool IsTriangleInBox(Triangle tri, BoundingBox bb)
        {
            // Move everything so that the box center is in (0, 0, 0)
            Vector3 center = bb.Center();
            BoundingBox box = new BoundingBox(bb.Min - center, bb.Max - center);
            tri.V0.Position -= center;
            tri.V1.Position -= center;
            tri.V2.Position -= center;

            // Compute triangle edges
            Vector3 e0 = tri.V1.Position - tri.V0.Position; // tri edge 0
            Vector3 e1 = tri.V2.Position - tri.V1.Position; // tri edge 1
            Vector3 e2 = tri.V0.Position - tri.V2.Position; // tri edge 2

            // Bullet 3: Test the 9 tests first (this was faster)
            Vector3 fe = new Vector3(Math.Abs(e0.X), Math.Abs(e0.Y), Math.Abs(e0.Z));
            if (AxisTestX01(tri, box, e0.Z, e0.Y, fe.Z, fe.Y)) return false;
            if (AxisTestY02(tri, box, e0.Z, e0.X, fe.Z, fe.X)) return false;
            if (AxisTestZ12(tri, box, e0.Y, e0.X, fe.Y, fe.X)) return false;

            fe = new Vector3(Math.Abs(e1.X), Math.Abs(e1.Y), Math.Abs(e1.Z));
            if (AxisTestX01(tri, box, e1.Z, e1.Y, fe.Z, fe.Y)) return false;
            if (AxisTestY02(tri, box, e1.Z, e1.X, fe.Z, fe.X)) return false;
            if (AxisTestZ0(tri, box, e1.Y, e1.X, fe.Y, fe.X)) return false;

            fe = new Vector3(Math.Abs(e2.X), Math.Abs(e2.Y), Math.Abs(e2.Z));
            if (AxisTestX2(tri, box, e2.Z, e2.Y, fe.Z, fe.Y)) return false;
            if (AxisTestY1(tri, box, e2.Z, e2.X, fe.Z, fe.X)) return false;
            if (AxisTestZ12(tri, box, e2.Y, e2.X, fe.Y, fe.X)) return false;

            // Bullet 1: First test overlap in the x/y/z directions
            // find min, max of the triangle each direction, and test for overlap in
            // that direction -- this is equivalent to testing a minimal AABB around
            // the triangle against the AABB

            // Test in X-direction
            double min = Math.Min(Math.Min(tri.V0.Position.X, tri.V1.Position.X), tri.V2.Position.X);
            double max = Math.Max(Math.Max(tri.V0.Position.X, tri.V1.Position.X), tri.V2.Position.X);
            if (min > (box.Size().X / 2) || max < (box.Size().X / -2))
            {
                return false;
            }

            // Test in Y-direction
            min = Math.Min(Math.Min(tri.V0.Position.Y, tri.V1.Position.Y), tri.V2.Position.Z);
            max = Math.Max(Math.Max(tri.V0.Position.Y, tri.V1.Position.Y), tri.V2.Position.Z);
            if (min > (box.Size().Y / 2) || max < (box.Size().Y / -2))
            {
                return false;
            }

            // Test in Z-direction
            min = Math.Min(Math.Min(tri.V0.Position.Y, tri.V1.Position.Y), tri.V2.Position.Z);
            max = Math.Max(Math.Max(tri.V0.Position.Y, tri.V1.Position.Y), tri.V2.Position.Z);
            if (min > (box.Size().Z / 2) || max < (box.Size().Z / -2))
            {
                return false;
            }

            // Bullet 2: Test if the box intersects the plane of the triangle compute plane equation of triangle: normal * x + d = 0
            Vector3 normal = e0.Cross(e1);
            double d = -normal.Dot(tri.V0.Position);  /* plane eq: normal.x+d=0 */
            if (!PlaneBoxOverlap(normal, d, box.Size() / 2))
            {
                return false;
            }

            return true;
        }

        private static bool PlaneBoxOverlap(Vector3 normal, double d, Vector3 maxbox)
        {
            Vector3 vmin = new Vector3();
            Vector3 vmax = new Vector3();

            // X
            if (normal.X > 0)
            {
                vmin.X = -maxbox.X;
                vmax.X = maxbox.X;
            }
            else
            {
                vmin.X = maxbox.X;
                vmax.X = -maxbox.X;
            }

            // Y
            if (normal.Y > 0)
            {
                vmin.Y = -maxbox.Y;
                vmax.Y = maxbox.Y;
            }
            else
            {
                vmin.Y = maxbox.Y;
                vmax.Y = -maxbox.Y;
            }

            // Z
            if (normal.Z > 0)
            {
                vmin.Z = -maxbox.Z;
                vmax.Z = maxbox.Z;
            }
            else
            {
                vmin.Z = maxbox.Z;
                vmax.Z = -maxbox.Z;
            }

            // Evaluate result
            if (normal.Dot(vmin) + d > 0)
            {
                return false;
            }
            if (normal.Dot(vmax) + d >= 0)
            {
                return true;
            }

            return false;
        }


        // X tests
        private static bool AxisTestX01(Triangle tri, BoundingBox box, double a, double b, double fa, double fb)
        {
            double min, max;
            double p0 = a * tri.V0.Position.Y - b * tri.V0.Position.Z;
            double p2 = a * tri.V2.Position.Y - b * tri.V2.Position.Z;

            if (p0 < p2)
            {
                min = p0; max = p2;
            }
            else {
                min = p2; max = p0;
            }

            double rad = (fa * box.Size().Y / 2) + (fb * box.Size().Z / 2);

            if (min > rad || max < -rad)
            {
                return true;
            }

            return false;
        }

        private static bool AxisTestX2(Triangle tri, BoundingBox box, double a, double b, double fa, double fb)
        {
            double min, max;
            double p0 = a * tri.V0.Position.Y - b * tri.V0.Position.Z;
            double p1 = a * tri.V1.Position.Y - b * tri.V1.Position.Z;

            if (p0 < p1)
            {
                min = p0; max = p1;
            }
            else
            {
                min = p1; max = p0;
            }

            double rad = (fa * box.Size().Y / 2) + (fb * box.Size().Z / 2);

            if (min > rad || max < -rad)
            {
                return true;
            }

            return false;
        }

        // Y tests
        private static bool AxisTestY02(Triangle tri, BoundingBox box, double a, double b, double fa, double fb)
        {
            double min, max;
            double p0 = -a * tri.V0.Position.X + b * tri.V0.Position.Z;
            double p2 = -a * tri.V2.Position.X + b * tri.V2.Position.Z;

            if (p0 < p2)
            {
                min = p0;
                max = p2;
            }
            else
            {
                min = p2;
                max = p0;
            }

            double rad = (fa * box.Size().X / 2) + (fb * box.Size().Z / 2);

            if (min > rad || max < -rad)
            {
                return true;
            }

            return false;
        }

        private static bool AxisTestY1(Triangle tri, BoundingBox box, double a, double b, double fa, double fb)
        {
            double min, max;
            double p0 = -a * tri.V0.Position.X + b * tri.V0.Position.Z;
            double p1 = -a * tri.V1.Position.X + b * tri.V1.Position.Z;

            if (p0 < p1)
            {
                min = p0;
                max = p1;
            }
            else
            {
                min = p1;
                max = p0;

            }

            double rad = (fa * box.Size().X / 2) + (fb * box.Size().Z / 2);

            if (min > rad || max < -rad)
            {
                return true;
            }

            return false;
        }

        // Z tests
        private static bool AxisTestZ12(Triangle tri, BoundingBox box, double a, double b, double fa, double fb)
        {
            double min, max;
            double p1 = a * tri.V1.Position.X - b * tri.V1.Position.Y;
            double p2 = a * tri.V2.Position.X - b * tri.V2.Position.Y;

            if (p2 < p1)
            {
                min = p2;
                max = p1;
            }
            else
            {
                min = p1;
                max = p2;
            }

            double rad = (fa * box.Size().X / 2) + (fb * box.Size().Y / 2);

            if (min > rad || max < -rad)
            {
                return true;
            }

            return false;
        }

        private static bool AxisTestZ0(Triangle tri, BoundingBox box, double a, double b, double fa, double fb)
        {
            double min, max;
            double p0 = a * tri.V0.Position.X - b * tri.V0.Position.Y;
            double p1 = a * tri.V1.Position.X - b * tri.V1.Position.Y;

            if (p0 < p1)
            {
                min = p0;
                max = p1;
            }
            else
            {
                min = p1;
                max = p0;
            }

            double rad = (fa * box.Size().X / 2) + (fb * box.Size().Y / 2);

            if (min > rad || max < -rad)
            {
                return true;
            }

            return false;
        }
    }
}
