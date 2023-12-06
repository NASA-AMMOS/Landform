using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using JPLOPS.MathExtensions;

namespace JPLOPS.Geometry
{
    public enum BoxAxis { X, Y, Z };

    /// Also see Microsoft.Xna.Framework.Extensions and OPS.MathExtensions.XNAExtensions
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
        public static Vector3 Extent(this BoundingBox box)
        {
            return box.Max - box.Min;
        }

        public static double Diameter(this BoundingBox box)
        {
            return box.Extent().Length();
        }

        public static Vector3 Center(this BoundingBox box)
        {
            return 0.5 * (box.Max + box.Min);
        }

        /// <summary>
        /// Negative if empty.
        /// </summary>
        public static double Volume(this BoundingBox box)
        {
            Vector3 size = box.Extent();
            return size.X * size.Y * size.Z;
        }

        public static Vector3 GetBoxAxisDirection(BoxAxis axis)
        {
            switch (axis)
            {
                case BoxAxis.X: return Vector3.UnitX;
                case BoxAxis.Y: return Vector3.UnitY;
                case BoxAxis.Z: return Vector3.UnitZ;
                default: throw new ArgumentException("unknown axis: " + axis);
            }
        }

        public static BoxAxis GetBoxAxis(Vector3 dir)
        {
            if (dir == Vector3.UnitX)
            {
                return BoxAxis.X;
            }
            if (dir == Vector3.UnitY)
            {
                return BoxAxis.Y;
            }
            if (dir == Vector3.UnitZ)
            {
                return BoxAxis.Z;
            }
            throw new Exception("no box axis for direction " + dir);
        }

        public static double GetExtentInAxis(this BoundingBox box, BoxAxis axis)
        {
            var sz = box.Extent();
            switch (axis)
            {
                case BoxAxis.X: return sz.X;
                case BoxAxis.Y: return sz.Y;
                case BoxAxis.Z: return sz.Z;
                default: throw new ArgumentException("unknown axis: " + axis);
            }
        }

        public static Vector2 GetFaceSizePerpendicularToAxis(this BoundingBox box, BoxAxis axis)
        {
            var sz = box.Extent();
            switch (axis)
            {
                case BoxAxis.X: return new Vector2(sz.Y, sz.Z);
                case BoxAxis.Y: return new Vector2(sz.X, sz.Z);
                case BoxAxis.Z: return new Vector2(sz.X, sz.Y);
                default: throw new ArgumentException("unknown axis: " + axis);
            }
        }

        public static double GetFaceAreaPerpendicularToAxis(this BoundingBox box, BoxAxis axis)
        {
            var sz = box.GetFaceSizePerpendicularToAxis(axis);
            return sz.X * sz.Y;
        }

        public static double MaxDimension(this BoundingBox box)
        {
            Vector3 size = box.Extent();
            return Math.Max(size.X, Math.Max(size.Y, size.Z));
        }

        public static BoxAxis MaxAxis(this BoundingBox box, out double maxDim)
        {
            Vector3 size = box.Extent();
            maxDim = box.MaxDimension();
            if (maxDim == size.X)
            {
                return BoxAxis.X;
            }
            if (maxDim == size.Y)
            {
                return BoxAxis.Y;
            }
            if (maxDim == size.Z)
            {
                return BoxAxis.Z;
            }
            throw new Exception("possible NaN in BoundingBox");
        }

        public static BoxAxis MaxAxis(this BoundingBox box)
        {
            return box.MaxAxis(out double maxDim);
        }

        public static double MinDimension(this BoundingBox box)
        {
            Vector3 size = box.Extent();
            return Math.Min(size.X, Math.Min(size.Y, size.Z));
        }

        public static BoxAxis MinAxis(this BoundingBox box, out double minDim)
        {
            minDim = box.MinDimension();
            Vector3 size = box.Extent();
            if (minDim == size.X)
            {
                return BoxAxis.X;
            }
            if (minDim == size.Y)
            {
                return BoxAxis.Y;
            }
            if (minDim == size.Z)
            {
                return BoxAxis.Z;
            }
            throw new Exception("possible NaN in BoundingBox");
        }

        public static BoxAxis MinAxis(this BoundingBox box)
        {
            return box.MinAxis(out double minDim);
        }

        /// <summary>
        /// split a box into two halves along the given axis at the given breakpoint
        /// </summary>
        public static List<BoundingBox> Halves(this BoundingBox box, BoxAxis axis, double breakpoint = 0.5)
        {
            var ret = new List<BoundingBox>();
            Vector3 min = box.Min;
            Vector3 max = box.Max;
            Vector3 ctr = Vector3.Lerp(min, max, breakpoint);
            switch (axis)
            {
                case BoxAxis.X:
                {
                    ret.Add(new BoundingBox(new Vector3(min.X, min.Y, min.Z), new Vector3(ctr.X, max.Y, max.Z)));
                    ret.Add(new BoundingBox(new Vector3(ctr.X, min.Y, min.Z), new Vector3(max.X, max.Y, max.Z)));
                    break;
                }
                case BoxAxis.Y:
                {
                    ret.Add(new BoundingBox(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, ctr.Y, max.Z)));
                    ret.Add(new BoundingBox(new Vector3(min.X, ctr.Y, min.Z), new Vector3(max.X, max.Y, max.Z)));
                    break;
                }
                case BoxAxis.Z:
                {
                    ret.Add(new BoundingBox(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, max.Y, ctr.Z)));
                    ret.Add(new BoundingBox(new Vector3(min.X, min.Y, ctr.Z), new Vector3(max.X, max.Y, max.Z)));
                    break;
                }
            }
            return ret;
        }

        /// <summary>
        /// split a box into four quarters in the plane perpendicular to the given axis at the given breakpoint
        /// if no breakpoint is given the box center is used
        /// </summary>
        public static List<BoundingBox> Quarters(this BoundingBox box, BoxAxis axis, Vector3? breakpoint = null)
        {
            var ret = new List<BoundingBox>();
            Vector3 min = box.Min;
            Vector3 max = box.Max;
            Vector3 ctr = breakpoint.HasValue ? breakpoint.Value : box.Center();
            switch (axis)
            {
                case BoxAxis.X: // split in YZ plane
                {
                    ret.Add(new BoundingBox(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, ctr.Y, ctr.Z)));
                    ret.Add(new BoundingBox(new Vector3(min.X, min.Y, ctr.Z), new Vector3(max.X, ctr.Y, max.Z)));
                    ret.Add(new BoundingBox(new Vector3(min.X, ctr.Y, min.Z), new Vector3(max.X, max.Y, ctr.Z)));
                    ret.Add(new BoundingBox(new Vector3(min.X, ctr.Y, ctr.Z), new Vector3(max.X, max.Y, max.Z)));
                    break;
                }
                case BoxAxis.Y: // split in XZ plane
                {
                    ret.Add(new BoundingBox(new Vector3(min.X, min.Y, min.Z), new Vector3(ctr.X, max.Y, ctr.Z)));
                    ret.Add(new BoundingBox(new Vector3(ctr.X, min.Y, min.Z), new Vector3(max.X, max.Y, ctr.Z)));
                    ret.Add(new BoundingBox(new Vector3(min.X, min.Y, ctr.Z), new Vector3(ctr.X, max.Y, max.Z)));
                    ret.Add(new BoundingBox(new Vector3(ctr.X, min.Y, ctr.Z), new Vector3(max.X, max.Y, max.Z)));
                    break;
                }
                case BoxAxis.Z: // split in XY plane
                {
                    ret.Add(new BoundingBox(new Vector3(min.X, min.Y, min.Z), new Vector3(ctr.X, ctr.Y, max.Z)));
                    ret.Add(new BoundingBox(new Vector3(ctr.X, min.Y, min.Z), new Vector3(max.X, ctr.Y, max.Z)));
                    ret.Add(new BoundingBox(new Vector3(min.X, ctr.Y, min.Z), new Vector3(ctr.X, max.Y, max.Z)));
                    ret.Add(new BoundingBox(new Vector3(ctr.X, ctr.Y, min.Z), new Vector3(max.X, max.Y, max.Z)));
                    break;
                }
            }
            return ret;
        }

        /// <summary>
        /// split a box into octants at the given breakpoint
        /// if no breakpoint is given the box center is used
        /// </summary>
        public static List<BoundingBox> Octants(this BoundingBox box, Vector3? breakpoint = null)
        {
            var ret = new List<BoundingBox>();
            Vector3 min = box.Min;
            Vector3 max = box.Max;
            Vector3 ctr = breakpoint.HasValue ? breakpoint.Value : box.Center();

            ret.Add(new BoundingBox(new Vector3(min.X, min.Y, min.Z), new Vector3(ctr.X, ctr.Y, ctr.Z)));
            ret.Add(new BoundingBox(new Vector3(ctr.X, min.Y, min.Z), new Vector3(max.X, ctr.Y, ctr.Z)));
            ret.Add(new BoundingBox(new Vector3(min.X, ctr.Y, min.Z), new Vector3(ctr.X, max.Y, ctr.Z)));
            ret.Add(new BoundingBox(new Vector3(ctr.X, ctr.Y, min.Z), new Vector3(max.X, max.Y, ctr.Z)));

            ret.Add(new BoundingBox(new Vector3(min.X, min.Y, ctr.Z), new Vector3(ctr.X, ctr.Y, max.Z)));
            ret.Add(new BoundingBox(new Vector3(ctr.X, min.Y, ctr.Z), new Vector3(max.X, ctr.Y, max.Z)));
            ret.Add(new BoundingBox(new Vector3(min.X, ctr.Y, ctr.Z), new Vector3(ctr.X, max.Y, max.Z)));
            ret.Add(new BoundingBox(new Vector3(ctr.X, ctr.Y, ctr.Z), new Vector3(max.X, max.Y, max.Z)));

            return ret;
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
            return Union(a, b).Extent().LengthSquared();
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

        /// <summary>
        /// Keeps the same center but enlarges or shrinks the extents by ratio
        /// </summary>
        public static BoundingBox CreateScaled(this BoundingBox box, Vector3 ratio)
        {
            var center = box.Center();
            var size = box.Extent() * ratio;
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
        /// A vertical AA trapezoid has vertical left and right sides (llc, ulc) and (lrc, urc):
        ///
        /// ulc
        /// |  \
        /// |   \
        /// |    urc
        /// |    |
        /// |    |
        /// |    lrc
        /// |   /
        /// |  /
        /// llc
        ///
        /// A horizontal AA trapezoid has horizontal top and bottom sides (llc, lrc) and (ulc, urc):
        ///
        ///  
        ///    ulc--------urc
        ///   /              \
        /// llc---------------lrc
        /// </summary>
        private class AxisAlignedTrapezoid
        {
            public readonly bool vertical;
            public readonly double span;

            private Vector2 llc, lrc, urc, ulc;
            private double invSpan;
            private const double eps = 1e-12;

            public AxisAlignedTrapezoid(Vector2 llc, Vector2 lrc, Vector2 urc, Vector2 ulc)
            {
                vertical = llc.X == ulc.X && lrc.X == urc.X;
                bool horizontal = llc.Y == lrc.Y && urc.Y == ulc.Y;
                if (!vertical && !horizontal)
                {
                    throw new ArgumentException("not axis aligned");
                }
                this.llc = llc;
                this.lrc = lrc;
                this.urc = urc;
                this.ulc = ulc;
                span = vertical ? (lrc.X - llc.X) : (ulc.Y - llc.Y);
                invSpan = 1.0 / span;
            }

            public Vector2 AbsoluteToRelative(Vector2 p)
            {
                if (vertical)
                {
                    double x = p.X - llc.X;
                    //if x ~= 0 then let leave it even if trapezoid is degenerate (span ~= 0)
                    //if !(x ~= 0) then let x go nuts if trapezoid is degenerate, because p is outside it
                    if (Math.Abs(x) > eps)
                    { 
                        x *= invSpan;
                    }
                    double y0 = (1.0 - x) * llc.Y + x * lrc.Y;
                    double y1 = (1.0 - x) * ulc.Y + x * urc.Y;
                    double dy = y1 - y0;
                    double y = p.Y - y0;
                    if (Math.Abs(y) > eps) // similar logic as above
                    {
                        y /= dy;
                    }
                    return new Vector2(x, y);
                }
                else
                {
                    double y = p.Y - llc.Y;
                    if (Math.Abs(y) > eps)
                    {
                        y *= invSpan;
                    }
                    double x0 = (1.0 - y) * llc.X + y * ulc.X;
                    double x1 = (1.0 - y) * lrc.X + y * urc.X;
                    double dx = x1 - x0;
                    double x = p.X - x0;
                    if (Math.Abs(x) > eps)
                    {
                        x /= dx;
                    }
                    return new Vector2(x, y);
                }
            }

            public Vector2 RelativeToAbsolute(Vector2 p)
            {
                if (vertical)
                {
                    double y0 = (1.0 - p.X) * llc.Y + p.X * lrc.Y;
                    double y1 = (1.0 - p.X) * ulc.Y + p.X * urc.Y;
                    return new Vector2(llc.X + p.X * span, y0 + p.Y * (y1 - y0));
                }
                else
                {
                    double x0 = (1.0 - p.Y) * llc.X + p.Y * ulc.X;
                    double x1 = (1.0 - p.Y) * lrc.X + p.Y * urc.X;
                    return new Vector2(x0 + p.X * (x1 - x0), llc.Y + p.Y * span);
                }
            }
        }

        /// <summary>
        /// Create a function to warp src to dst relative to box, ignoring Z coordinates.
        ///
        /// The box is split into 5 regions, a central rectangle surrounded by four trapezoids, for src and dst:
        ///
        /// \|||||||||||||||||/      \|||||||||||||||||/
        /// -\|||||||||||||||/-      -\|||||||A|||||||/-
        /// --\||||||a||||||/--      --\|||||||||||||/--
        /// ---\|||||||||||/---      ---xxxxxxxxxxxxx---
        /// ----\|||||||||/----      ---xxxxxxxxxxxxx---
        /// -----xxxxxxxxx-----      ---xxxxxxxxxxxxx---
        /// -----xxxxxxxxx-----      ---xxxxxxxxxxxxx---
        /// -----xxxxxxxxx-----      ---xxxxxxxxxxxxx---
        /// --b--xxxSRCxxx--c-- ---> -B-xxxxxDSTxxxxx-C-
        /// -----xxxxxxxxx-----      ---xxxxxxxxxxxxx---
        /// -----xxxxxxxxx-----      ---xxxxxxxxxxxxx---
        /// -----xxxxxxxxx-----      ---xxxxxxxxxxxxx---
        /// ----/|||||||||\----      ---xxxxxxxxxxxxx---
        /// ---/|||||||||||\---      ---xxxxxxxxxxxxx---
        /// --/||||||d||||||\--      --/|||||||||||||\--
        /// -/|||||||||||||||\-      -/|||||||D|||||||\-
        /// /|||||||||||||||||\      /|||||||||||||||||\
        ///
        /// Points are mapped from SRC to DST and from {a-d} to {A-D}.
        ///
        /// If ease <= 0 or ease >= 1 then the mapping is piecewise bilinear.
        /// Otherwise an approximate cubic spline easing function is used
        /// to avoid the tangent discontinuity at the boundaries of the inner region.
        /// </summary>
        public static Func<Vector2, Vector2> Create2DWarpFunction(this BoundingBox box,
                                                                  BoundingBox src, BoundingBox dst,
                                                                  double ease = 0)
        {
            Func<BoundingBox, Vector2> llc = b => b.Min.XY();
            Func<BoundingBox, Vector2> lrc = b => new Vector2(b.Max.X, b.Min.Y);
            Func<BoundingBox, Vector2> urc = b => b.Max.XY();
            Func<BoundingBox, Vector2> ulc = b => new Vector2(b.Min.X, b.Max.Y);

            var srcs = new List<AxisAlignedTrapezoid>();
            var dsts = new List<AxisAlignedTrapezoid>();

            void addPair(Vector2 srcLLC, Vector2 srcLRC, Vector2 srcURC, Vector2 srcULC,
                         Vector2 dstLLC, Vector2 dstLRC, Vector2 dstURC, Vector2 dstULC)
            {
                srcs.Add(new AxisAlignedTrapezoid(srcLLC, srcLRC, srcURC, srcULC));
                dsts.Add(new AxisAlignedTrapezoid(dstLLC, dstLRC, dstURC, dstULC));
            }

            addPair(llc(src), lrc(src), urc(src), ulc(src), llc(dst), lrc(dst), urc(dst), ulc(dst)); //src -> dst
            addPair(ulc(src), urc(src), urc(box), ulc(box), ulc(dst), urc(dst), urc(box), ulc(box)); //a -> A
            addPair(llc(box), llc(src), ulc(src), ulc(box), llc(box), llc(dst), ulc(dst), ulc(box)); //b -> B
            addPair(lrc(src), lrc(box), urc(box), urc(src), lrc(dst), lrc(box), urc(box), urc(dst)); //c -> C
            addPair(llc(box), lrc(box), lrc(src), llc(src), llc(box), lrc(box), lrc(dst), llc(dst)); //d -> D

            //de Casteljau algorithm: point on cubic bezier curve given control polygon p0-p3 and parameter t in [0, 1]
            Vector2 cubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, double t)
            {
                double w = 1 - t;
                var q0 = w * p0 + t * p1;
                var q1 = w * p1 + t * p2;
                var q2 = w * p2 + t * p3;
                var r0 = w * q0 + t * q1;
                var r1 = w * q1 + t * q2;
                return w * r0 + t * r1;
            }

            //bake an easing function
            //without easing the remap is a linear function taking x in [0,1] to y in [0,1] with unit slope
            //with ease-in the initial slope is as given, otherwise the final slope is as given
            //the easing function is a piecewise linear approximation to a cubic bezier
            //we use cubic bezier because
            //(a) the curve is always contained in the convex hull of the control polygon
            //(b) any line intersects the curve no more times than it intersects the control polygon
            //(c) the curve interpolates its end points
            //(d) the start and end tangents are controlled by the second and second-from-last control points
            //property (a) lets us ensure that the curve stays inside [0,1]x[0,1] and not too far from the diagonal
            //property (b) lets us construct a curve that is a function of x
            //property (c) lets us ensure that the curve starts at (0, 0) and ends at (1, 1)
            //property (d) lets us control the easing slope
            Func<double, double> remap(double slope, bool easeIn)
            {
                var p0 = Vector2.Zero;
                var p3 = Vector2.One;

                //we keep the inner two control points coincident to construct a singly curved monotonic function
                double angle = Math.Atan2(slope, 1);
                var p12 = new Vector2(Math.Cos(angle), Math.Sin(angle)) * ease;
                if (!easeIn)
                {
                    p12 = Vector2.One - p12;
                }

                //it's not trivial to compute y as a function of x for the cubic bezier
                //rather, we bake a piecewise linear approximation of it
                //and then binary search that
                int segs = 32;
                var pts = new Vector2[segs + 1];
                pts[0] = p0;
                pts[segs] = p3;
                for (int i = 1; i < segs; i++)
                {
                    pts[i] = cubicBezier(p0, p12, p12, p3, ((double)i) / segs);
                }

                return x =>
                {
                    int l = 0, u = segs;
                    while (u - l > 1)
                    {
                        int m = (l + u) / 2;
                        if (x < pts[m].X)
                        {
                            u = m;
                        }
                        else
                        {
                            l = m;
                        }
                    }
                    double t = (x - pts[l].X) / (pts[u].X - pts[l].X);
                    return pts[l].Y + t * (pts[u].Y - pts[l].Y);
                };
            }

            var remaps = new Func<double, double>[5]; //entries initialized to null
            if (ease > 0 && ease < 1)
            {
                remaps[1] = remap(srcs[1].span / dsts[1].span, easeIn: true);  //a -> A
                remaps[2] = remap(srcs[2].span / dsts[2].span, easeIn: false); //b -> B
                remaps[3] = remap(srcs[3].span / dsts[3].span, easeIn: true);  //c -> C
                remaps[4] = remap(srcs[4].span / dsts[4].span, easeIn: false); //d -> D
            }

            return v =>
            {
                for (int i = 0; i < srcs.Count; i++)
                {
                    var r = srcs[i].AbsoluteToRelative(v);
                    if (r.X >= 0 && r.X <= 1 && r.Y >= 0 && r.Y <= 1)
                    {
                        if (remaps[i] != null)
                        {
                            var was = r;
                            if (srcs[i].vertical)
                            {
                                r.X = remaps[i](r.X);
                            }
                            else
                            {
                                r.Y = remaps[i](r.Y);
                            }
                        }
                        return dsts[i].RelativeToAbsolute(r);
                    }
                }
                return v;
            };
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
                                       (float)box.Min.Z, (float)box.Max.Z); //yes, z last
        }

        public static BoundingBox ToBoundingBox(this RTree.Rectangle rect)
        {
            RTree.dimension dimx = rect.get(0).Value;
            RTree.dimension dimy = rect.get(1).Value;
            RTree.dimension dimz = rect.get(2).Value;
            return new BoundingBox(new Vector3(dimx.min, dimy.min, dimz.min),
                                   new Vector3(dimx.max, dimy.max, dimz.max));
        }

        public static BoundingBox CreateEmpty()
        {
            double pinf = double.PositiveInfinity, ninf = double.NegativeInfinity;
            return new BoundingBox(min: new Vector3(pinf, pinf, pinf), max: new Vector3(ninf, ninf, ninf));
        }

        public static bool IsEmpty(this BoundingBox box)
        {
            return box.Min.X > box.Max.X || box.Min.Y > box.Max.Y || box.Min.Z > box.Max.Z;
        }

        public static BoundingBox CreateFromPoint(Vector3 pt, double minSize = 0)
        {
            Vector3 offset = Vector3.One * 0.5 * minSize;
            return new BoundingBox(pt - offset, pt + offset);
        }

        public static BoundingBox CreateFromPoints(IEnumerable<Vector3> pts, double minSize = 0)
        {
            if (pts.Count() == 0)
            {
                return CreateEmpty();
            }
            var box = CreateFromPoint(pts.First(), minSize);
            foreach (var pt in pts.Skip(1))
            {
                Extend(ref box, pt);
            }
            return box;
        }

        public static BoundingBox CreateFromTriangle(Triangle tri)
        {
            var ret = CreateFromPoint(tri.V0.Position);
            Extend(ref ret, tri.V1.Position);
            Extend(ref ret, tri.V2.Position);
            return ret;
        }

        //because it's annoying to have to say bounds.Contains(pt) != ContainmentType.Disjoint
        //and also there were subtle bugs where ppl were instead saying
        //bounds.Contains(pt) == ContainmentType.Contains
        //which is not quite the same thing (misses points that are on the surface of the box)
        public static bool ContainsPoint(this BoundingBox box, Vector3 pt)
        {
            return
                pt.X >= box.Min.X && pt.X <= box.Max.X &&
                pt.Y >= box.Min.Y && pt.Y <= box.Max.Y &&
                pt.Z >= box.Min.Z && pt.Z <= box.Max.Z;
        }

        public static bool ContainsPoint(this BoundingBox box, Vector3 pt,
                                         bool includeMaxX, bool includeMaxY, bool includeMaxZ)
        {
            if (pt.X < box.Min.X || pt.Y < box.Min.Y || pt.Z < box.Min.Z)
            {
                return false;
            }
            if (pt.X > box.Max.X || (pt.X == box.Max.X && !includeMaxX))
            {
                return false;
            }
            if (pt.Y > box.Max.Y || (pt.Y == box.Max.Y && !includeMaxY))
            {
                return false;
            }
            if (pt.Z > box.Max.Z || (pt.Z == box.Max.Z && !includeMaxZ))
            {
                return false;
            }
            return true;
        }

        public static bool FuzzyContainsPoint(this BoundingBox box, Vector3 pt, double epsilon = MathE.EPSILON)
        {
            return
                pt.X >= box.Min.X - epsilon && pt.X <= box.Max.X + epsilon &&
                pt.Y >= box.Min.Y - epsilon && pt.Y <= box.Max.Y + epsilon &&
                pt.Z >= box.Min.Z - epsilon && pt.Z <= box.Max.Z + epsilon ;
        }

        public static bool Contains(this BoundingBox box, Triangle tri)
        {
            return
                tri.V0.Position.X >= box.Min.X && tri.V0.Position.X <= box.Max.X &&
                tri.V0.Position.Y >= box.Min.Y && tri.V0.Position.Y <= box.Max.Y &&
                tri.V0.Position.Z >= box.Min.Z && tri.V0.Position.Z <= box.Max.Z &&
                tri.V1.Position.X >= box.Min.X && tri.V1.Position.X <= box.Max.X &&
                tri.V1.Position.Y >= box.Min.Y && tri.V1.Position.Y <= box.Max.Y &&
                tri.V1.Position.Z >= box.Min.Z && tri.V1.Position.Z <= box.Max.Z &&
                tri.V2.Position.X >= box.Min.X && tri.V2.Position.X <= box.Max.X &&
                tri.V2.Position.Y >= box.Min.Y && tri.V2.Position.Y <= box.Max.Y &&
                tri.V2.Position.Z >= box.Min.Z && tri.V2.Position.Z <= box.Max.Z;
        }

        public static BoundingBox Extend(ref BoundingBox box, Vector3 pt)
        {
            box.Min.X = Math.Min(box.Min.X, pt.X);
            box.Min.Y = Math.Min(box.Min.Y, pt.Y);
            box.Min.Z = Math.Min(box.Min.Z, pt.Z);
            box.Max.X = Math.Max(box.Max.X, pt.X);
            box.Max.Y = Math.Max(box.Max.Y, pt.Y);
            box.Max.Z = Math.Max(box.Max.Z, pt.Z);
            return box;
        }

        public static BoundingBox Extend(ref BoundingBox box, params Vector3[] pts)
        {
            foreach (var pt in pts)
            {
                Extend(ref box, pt);
            }
            return box;
        }

        public static BoundingBox Extend(ref BoundingBox box, Triangle tri)
        {
            Extend(ref box, tri.V0.Position);
            Extend(ref box, tri.V1.Position);
            Extend(ref box, tri.V2.Position);
            return box;
        }

        public static BoundingBox Extend(ref BoundingBox box, BoundingBox other)
        {
            box.Min.X = Math.Min(box.Min.X, other.Min.X);
            box.Min.Y = Math.Min(box.Min.Y, other.Min.Y);
            box.Min.Z = Math.Min(box.Min.Z, other.Min.Z);
            box.Max.X = Math.Max(box.Max.X, other.Max.X);
            box.Max.Y = Math.Max(box.Max.Y, other.Max.Y);
            box.Max.Z = Math.Max(box.Max.Z, other.Max.Z);
            return box;
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
            tt.Add(new Triangle(new Vertex(c[3], fw, color), new Vertex(c[7], fw, color), new Vertex(c[6], fw, color)));
            tt.Add(new Triangle(new Vertex(c[6], fw, color), new Vertex(c[2], fw, color), new Vertex(c[3], fw, color)));

            //back (-y)
            Vector3 bk = new Vector3(0, -1, 0);
            tt.Add(new Triangle(new Vertex(c[0], bk, color), new Vertex(c[1], bk, color), new Vertex(c[5], bk, color)));
            tt.Add(new Triangle(new Vertex(c[5], bk, color), new Vertex(c[4], bk, color), new Vertex(c[0], bk, color)));

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

        public static List<Plane> FacePlanes(this BoundingBox box)
        {
            var ret = new List<Plane>();
            ret.Add(PlaneExtensions.FromPointAndNormal(box.Min, new Vector3(-1, 0, 0)));
            ret.Add(PlaneExtensions.FromPointAndNormal(box.Min, new Vector3(0, -1, 0)));
            ret.Add(PlaneExtensions.FromPointAndNormal(box.Min, new Vector3(0, 0, -1)));
            ret.Add(PlaneExtensions.FromPointAndNormal(box.Max, new Vector3(1, 0, 0)));
            ret.Add(PlaneExtensions.FromPointAndNormal(box.Max, new Vector3(0, 1, 0)));
            ret.Add(PlaneExtensions.FromPointAndNormal(box.Max, new Vector3(0, 0, 1)));
            return ret;
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

        public static string Fmt(this BoundingBox box, int decimalPlaces = 3)
        {
            string fmt = string.Format("({{0:f{0}}}, {{1:f{0}}}, {{2:f{0}}})-({{3:f{0}}}, {{4:f{0}}}, {{5:f{0}}})",
                                       decimalPlaces);
            return string.Format(fmt, box.Min.X, box.Min.Y, box.Min.Z, box.Max.X, box.Max.Y, box.Max.Z);
        }

        public static string FmtExtent(this BoundingBox box, int decimalPlaces = 3)
        {
            if (box.IsEmpty()) {
                return "(empty)";
            }
            Vector3 sz = box.Extent();
            string fmt = string.Format("{{0:f{0}}}x{{1:f{0}}}x{{2:f{0}}}", decimalPlaces);
            return string.Format(fmt, sz.X, sz.Y, sz.Z);
        }
    }
}
