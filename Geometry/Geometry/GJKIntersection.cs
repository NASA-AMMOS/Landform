using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace JPLOPS.Geometry
{
    // Written with much help from this page:
    // http://programyourfaceoff.blogspot.com/2012/01/gjk-algorithm.html
    /// <summary>
    /// Intersection algorithm for convex 3D shapes.
    /// </summary>
    public static class GJKIntersection
    {
        static Vector3 FurthestPoint(Mesh m, Vector3 dir)
        {
            double maxDot = double.NegativeInfinity;
            Vector3 res = dir * double.NegativeInfinity;
            foreach (var vtx in m.Vertices)
            {
                var v = vtx.Position;
                if (v.Dot(dir) > maxDot)
                {
                    maxDot = v.Dot(dir);
                    res = v;
                }
            }
            return res;
        }
        static Vector3 Support(Mesh one, Mesh two, Vector3 dir)
        {
            return FurthestPoint(one, dir) - FurthestPoint(two, -dir);
        }

        public static bool Intersects(Mesh one, Mesh two)
        {
            Vector3 d = Vector3.One / Math.Sqrt(3);
            Vector3 s = Support(one, two, d);
            List<Vector3> simplex = new List<Vector3>() { s };
            d = -s;

            int maxIters = 100;
            for (int i = 0; i < maxIters; i++)
            {
                Vector3 a = Support(one, two, d);
                if (a.Dot(d) < 0) return false;

                simplex.Add(a);
                if (ProcessSimplex(ref simplex, ref d)) return true;
                d.Normalize();
            }
            return true; // eh, probably intersects?
        }

        private static bool ProcessSimplex(ref List<Vector3> simplex, ref Vector3 d)
        {
            if (simplex.Count == 2) return ProcessLine(ref simplex, ref d);
            else if (simplex.Count == 3) return ProcessTriangle(ref simplex, ref d);
            else return ProcessTetrahedron(ref simplex, ref d);
        }

        private static bool ProcessLine(ref List<Vector3> simplex, ref Vector3 d)
        {
            Vector3 a = simplex[1];
            Vector3 b = simplex[0];
            Vector3 ab = b - a;
            Vector3 a0 = -a;
            if (ab.Dot(a0) > 0)
            {
                d = Vector3.Cross(Vector3.Cross(ab, a0), ab);
                if (d.LengthSquared() < 1e-7)
                {
                    // line intersects origin
                    return true;
                }
            }
            else
            {
                simplex.Remove(b);
                d = a0;
            }
            return false;
        }

        private static bool ProcessTriangle(ref List<Vector3> simplex, ref Vector3 d)
        {
            Vector3 a = simplex[2];
            Vector3 b = simplex[1];
            Vector3 c = simplex[0];
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 abc = Vector3.Cross(ab, ac);
            Vector3 a0 = -a;

            if (Vector3.Cross(ab, abc).Dot(a0) > 0)
            {
                // closest to edge ab
                simplex.Remove(c);
                d = Vector3.Cross(Vector3.Cross(ab, a0), ab);
            }
            else if (Vector3.Cross(abc, ac).Dot(a0) > 0)
            {
                // closest to edge ac
                simplex.Remove(b);
                d = Vector3.Cross(Vector3.Cross(ac, a0), ac);
            }
            else
            {
                // must be inside triangle
                // is it above or below?
                if (abc.Dot(a0) > 0)
                {
                    // above
                    d = abc;
                }
                else if (abc.Dot(a0) < 0)
                {
                    // below, reverse winding order
                    simplex = new List<Vector3> { b, c, a };
                    d = -abc;
                }
                else
                {
                    // nope, triangle contains origin
                    return true;
                }
            }
            return false;
        }
        
        private static bool ProcessTetrahedron(ref List<Vector3> simplex, ref Vector3 direction)
        {
            Vector3 ap, bp, cp;
            {
                Vector3 a = simplex[3];
                Vector3 b = simplex[2];
                Vector3 c = simplex[1];
                Vector3 d = simplex[0];

                Vector3 a0 = -a;

                Vector3 ab = b - a;
                Vector3 ac = c - a;
                Vector3 ad = d - a;


                if (ab.Cross(ac).Dot(a0) > 0)
                {
                    ap = a;
                    bp = b;
                    cp = c;
                }
                else if (ac.Cross(ad).Dot(a0) > 0)
                {
                    ap = a;
                    bp = c;
                    cp = d;
                }
                else if (ad.Cross(b).Dot(a0) > 0)
                {
                    ap = a;
                    bp = d;
                    cp = b;
                }
                else
                {
                    return true;
                }
            }

            Vector3 a0p = -ap;

            Vector3 abp = bp - ap;
            Vector3 acp = cp - ap;
            Vector3 abcp = Vector3.Cross(abp, acp);

            if (abp.Cross(abcp).Dot(a0p) > 0)
            {
                simplex = new List<Vector3> { bp, ap };
                direction = Vector3.Cross(Vector3.Cross(abp, a0p), abp);
            }
            else if (abcp.Cross(acp).Dot(a0p) > 0)
            {
                simplex = new List<Vector3> { cp, ap };
                direction = Vector3.Cross(Vector3.Cross(acp, a0p), acp);
            }
            else
            {
                simplex = new List<Vector3> { cp, bp, ap };
                direction = abcp;
            }
            return false;
        }
    }
}
