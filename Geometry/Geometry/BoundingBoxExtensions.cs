using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;

namespace OPS.Geometry
{
   public enum BoxAxis { X, Y, Z };

    public static class BoundingBoxExtensions
    {
        public static BoundingBox CreateXY(double size)
        {
            return CreateXY(size, size);
        }

        public static BoundingBox CreateXY(double width, double height)
        {
            var diag = new Vector2(width, height);
            return CreateXY(-0.5 * diag, 0.5 * diag);
        }

        public static BoundingBox CreateXY(Vector2 center, double size)
        {
            return CreateXY(center, size, size);
        }

        public static BoundingBox CreateXY(Vector2 center, double width, double height)
        {
            var diag = new Vector2(width, height);
            return CreateXY(-0.5 * diag + center, 0.5 * diag + center);
        }

        public static BoundingBox CreateXY(Vector2 min, Vector2 max)
        {
            return new BoundingBox(new Vector3(min.X, min.Y, 0), new Vector3(max.X, max.Y, 0));
        }

        public static BoundingBox CreateXY(BoundingBox box)
        {
            return new BoundingBox(new Vector3(box.Min.X, box.Min.Y, 0), new Vector3(box.Max.X, box.Max.Y, 0));
        }

        /// <summary>
        /// Returns the size of the bounding box (max-min)
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        public static Vector3 Size(this BoundingBox box)
        {
            return box.Max - box.Min;
        }

        public static double Volume(this BoundingBox box)
        {
            Vector3 size = box.Size();
            return size.X * size.Y * size.Z;
        }

        public static Vector2 GetFaceSizePerpendicularToAxis(this BoundingBox box, BoxAxis axis)
        {
            var sz = box.Size();
            switch (axis)
            {
                case BoxAxis.X: return new Vector2(sz.Y, sz.Z);
                case BoxAxis.Y: return new Vector2(sz.X, sz.Z);
                case BoxAxis.Z: return new Vector2(sz.X, sz.Y);
                default: throw new ArgumentException("unknown axis: " + axis);
            }
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
        /// Keeps the same center but enlarges or shrinks the extents by ratio
        /// </summary>
        public static BoundingBox CreateScaled(this BoundingBox box, Vector3 ratio)
        {
            var center = box.Center();
            var size = box.Size() * ratio;
            return new BoundingBox(center - (0.5 * size), center + (0.5 * size));
        }

        public static BoundingBox CreateScaled(this BoundingBox box, double ratio)
        {
            return box.CreateScaled(Vector3.One * ratio);
        }

        /// <summary>
        /// return 4 planes coincident to the box faces parallel to axis
        /// the "top" side of the planes corresponds to the outside of the box
        /// </summary>
        public static Plane[] GetFacePlanesAroundAxis(this BoundingBox box, BoxAxis axis)
        {
            Vector3 up = new Vector3(0, 0, 1); //top (+z)
            Vector3 dn = new Vector3(0, 0, -1); //bottom (-z)
            Vector3 fw = new Vector3(0, 1, 0); //front (+y)
            Vector3 bk = new Vector3(0, -1, 0); //back (-y)
            Vector3 rt = new Vector3(1, 0, 0); //right (+x)
            Vector3 lf = new Vector3(-1, 0, 0); //left (-x)

            Vector3[] normals = null;
            double w = 0, h = 0;

            switch (axis)
            {
                case BoxAxis.X:
                {
                    normals = new Vector3[] { up, fw, dn, bk };
                    w = box.Max.Z - box.Min.Z;
                    h = box.Max.Y - box.Min.Y;
                    break;
                }
                case BoxAxis.Y:
                {
                    normals = new Vector3[] { up, rt, dn, lf };
                    w = box.Max.Z - box.Min.Z;
                    h = box.Max.X - box.Min.X;
                    break;
                }
                case BoxAxis.Z:
                {
                    normals = new Vector3[] { lf, fw, rt, bk };
                    w = box.Max.X - box.Min.X;
                    h = box.Max.Y - box.Min.Y;
                    break;
                }
                default: throw new ArgumentException("unknown box axis: " + axis);
            }

            var ctr = box.Center();

            return new Plane[] { PlaneExtensions.FromPointAndNormal(ctr + 0.5 * w * normals[0], normals[0]),
                                 PlaneExtensions.FromPointAndNormal(ctr + 0.5 * h * normals[1], normals[1]),
                                 PlaneExtensions.FromPointAndNormal(ctr + 0.5 * w * normals[2], normals[2]),
                                 PlaneExtensions.FromPointAndNormal(ctr + 0.5 * h * normals[3], normals[3]) };
        }

        /// <summary>
        /// Returns true if the inner is totally inside or equal to the outer
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

        public static Mesh ToMesh(this BoundingBox box, Vector4? color = null)
        {
            List<Triangle> tt = new List<Triangle>();

            //there is an XNA API to get the corners but I don't like its doc
            Vector3 min = box.Min;
            Vector3 max = box.Max;
            Vector3[] c = new Vector3[] //box corners
                {
                    //bottom
                    //
                    //    c[3]---c[2]
                    // y    |      |
                    // ^    |      |
                    // |  c[0]---c[1]
                    // |  
                    // +----> x
                    new Vector3(min.X, min.Y, min.Z),
                    new Vector3(max.X, min.Y, min.Z),
                    new Vector3(max.X, max.Y, min.Z),
                    new Vector3(min.X, max.Y, min.Z),

                    //top
                    //
                    //    c[7]---c[6]
                    // y    |      |
                    // ^    |      |
                    // |  c[4]---c[5]
                    // |  
                    // +----> x
                    new Vector3(min.X, min.Y, max.Z),
                    new Vector3(max.X, min.Y, max.Z),
                    new Vector3(max.X, max.Y, max.Z),
                    new Vector3(min.X, max.Y, max.Z),
                };

            //top (+z)
            Vector3 up = new Vector3(0, 0, 1);
            tt.Add(new Triangle(new Vertex(c[4], up, color), new Vertex(c[5], up, color), new Vertex(c[6], up, color)));
            tt.Add(new Triangle(new Vertex(c[6], up, color), new Vertex(c[7], up, color), new Vertex(c[4], up, color)));

            //bottom (-z)
            Vector3 dn = new Vector3(0, 0, -1);
            tt.Add(new Triangle(new Vertex(c[0], dn, color), new Vertex(c[3], dn, color), new Vertex(c[2], dn, color)));
            tt.Add(new Triangle(new Vertex(c[2], dn, color), new Vertex(c[1], dn, color), new Vertex(c[0], dn, color)));

            //front (+y)
            Vector3 fw = new Vector3(0, 1, 0);
            tt.Add(new Triangle(new Vertex(c[0], fw, color), new Vertex(c[1], fw, color), new Vertex(c[5], fw, color)));
            tt.Add(new Triangle(new Vertex(c[5], fw, color), new Vertex(c[4], fw, color), new Vertex(c[0], fw, color)));

            //back (-y)
            Vector3 bk = new Vector3(0, -1, 0);
            tt.Add(new Triangle(new Vertex(c[3], bk, color), new Vertex(c[7], bk, color), new Vertex(c[6], bk, color)));
            tt.Add(new Triangle(new Vertex(c[6], bk, color), new Vertex(c[2], bk, color), new Vertex(c[3], bk, color)));

            //right (+x)
            Vector3 rt = new Vector3(1, 0, 0);
            tt.Add(new Triangle(new Vertex(c[1], rt, color), new Vertex(c[2], rt, color), new Vertex(c[6], rt, color)));
            tt.Add(new Triangle(new Vertex(c[6], rt, color), new Vertex(c[5], rt, color), new Vertex(c[1], rt, color)));

            //left (-x)
            Vector3 lf = new Vector3(-1, 0, 0);
            tt.Add(new Triangle(new Vertex(c[0], lf, color), new Vertex(c[4], lf, color), new Vertex(c[7], lf, color)));
            tt.Add(new Triangle(new Vertex(c[7], lf, color), new Vertex(c[3], lf, color), new Vertex(c[0], lf, color)));

            return new Mesh(tt, hasNormals: true, hasColors: color.HasValue);
        }

        public static Matrix StretchCubeAlongLineSegment(Vector3 a, Vector3 b, double size = 1)
        {
            Vector3 c = 0.5 * (a + b);
            Vector3 d = (1 / size) * (b - a);
            Vector3 dn = Vector3.Normalize(d);
            Vector3 h = Vector3.Cross(dn, Vector3.UnitX);
            if (h.LengthSquared() < 0.001)
            {
                h = Vector3.Cross(dn, Vector3.UnitY);
            }
            h = Vector3.Normalize(h);
            Vector3 v = Vector3.Cross(dn, h);
            return new Matrix(h.X, h.Y, h.Z, 0,
                              v.X, v.Y, v.Z, 0,
                              d.X, d.Y, d.Z, 0,
                              c.X, c.Y, c.Z, 1);
        }

        public static BoundingBox MakeCube(double size = 1)
        {
            double h = 0.5 * size;
            return new BoundingBox(new Vector3(-h, -h, -h), new Vector3(h, h, h));
        }

        public static string SizeToString(this BoundingBox box, int decimalPlaces = 3)
        {
            Vector3 sz = box.Size();
            string fmt = string.Format("{{0:f{0}}}x{{1:f{0}}}x{{2:f{0}}}", decimalPlaces);
            return string.Format(fmt, sz.X, sz.Y, sz.Z);
        }
    }
}
