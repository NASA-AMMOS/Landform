using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    // from http://programyourfaceoff.blogspot.com/2012/01/gjk-algorithm.html
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
                d.Normalize();
                Vector3 a = Support(one, two, d);
                if (a.Dot(d) < 0) return false;

                simplex.Add(a);
                if (ProcessSimplex(ref simplex, ref d)) return true;
            }
            return true; // eh, probably intersects?
        }

        private static bool ProcessSimplex(ref List<Vector3> simplex, ref Vector3 d)
        {
            if (simplex.Count == 1)
            {
                d = -simplex[0];
            }
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
                    // a0 and ab colinear
                    d = new Vector3(-d.X, 0, 0);
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
            Vector3 acNormal = Vector3.Cross(abc, ac);
            Vector3 abNormal = Vector3.Cross(ab, abc);

            if (acNormal.Dot(a0) > 0)
            {
                if (ac.Dot(a0) > 0)
                {
                    simplex.Remove(b);
                    d = Vector3.Cross(Vector3.Cross(ac, a0), ac);
                }
                else
                {
                    if (ab.Dot(a0) > 0)
                    {
                        simplex.Remove(c);
                        d = Vector3.Cross(Vector3.Cross(ab, a0), ab);
                    }
                    else
                    {
                        simplex.Remove(b);
                        simplex.Remove(c);
                        d = a0;
                    }
                }
            }
            else
            {
                if (abNormal.Dot(a0) > 0)
                {
                    if (ab.Dot(a0) > 0)
                    {
                        simplex.Remove(c);
                        d = Vector3.Cross(Vector3.Cross(ab, a0), ab);
                    }
                    else
                    {
                        simplex.Remove(b);
                        simplex.Remove(c);
                        d = a0;
                    }
                }
                else
                {
                    if (abc.Dot(a0) > 0)
                    {
                        d = Vector3.Cross(Vector3.Cross(abc, a0), abc);
                    }
                    else
                    {
                        d = Vector3.Cross(Vector3.Cross(-abc, a0), -abc);
                    }
                }
            }
            return false;

            /*if (abNormal.Dot(a0) > 0)
            {
                simplex = new List<Vector3>() { a, b };
                d = ab.Cross(a0).Cross(ab);
                return false;
            }

            if (acNormal.Dot(a0) > 0)
            {
                simplex = new List<Vector3>() { a, c };
                d = ac.Cross(a0).Cross(ac);
                return false;
            }

            if (abc.Dot(a0) > 0)
            {
                d = abc;
            }
            else
            {
                simplex = new List<Vector3>() { a, c, b };
                d = -abc;
            }
            return false;*/
        }

        /*private static bool _innerTetrahedron(ref List<Vector3> simplex, Vector3 a, Vector3 b, Vector3 c, Vector3 d, ref Vector3 direction)
        {
            Vector3 abNormal = null; 
        }*/
        private static bool ProcessTetrahedron(ref List<Vector3> simplex, ref Vector3 direction)
        {
            Vector3 a = simplex[3];
            Vector3 b = simplex[2];
            Vector3 c = simplex[1];
            Vector3 d = simplex[0];
            Vector3 ac = c - a;
            Vector3 ad = d - a;
            Vector3 ab = b - a;
            Vector3 bc = c - b;
            Vector3 bd = d - b;

            Vector3 acd = Vector3.Cross(ad, ac);
            Vector3 abd = Vector3.Cross(ab, ad);
            Vector3 abc = Vector3.Cross(ac, ab);

            Vector3 a0 = -a;

            if (abc.Dot(a0) > 0)
            {
                if (Vector3.Cross(abc, ac).Dot(a0) > 0)
                {
                    simplex.Remove(b);
                    simplex.Remove(d);
                    direction = Vector3.Cross(Vector3.Cross(ac, a0), ac);
                }
                else if (Vector3.Cross(ab, abc).Dot(a0) > 0)
                {
                    simplex.Remove(c);
                    simplex.Remove(d);
                    direction = Vector3.Cross(Vector3.Cross(ab, a0), ab);
                }
                else
                {
                    simplex.Remove(d);
                    direction = abc;
                }
            }
            else if (acd.Dot(a0) > 0)
            {
                if (Vector3.Cross(acd, ad).Dot(a0) > 0)
                {
                    simplex.Remove(b);
                    simplex.Remove(c);
                    direction = Vector3.Cross(Vector3.Cross(ad, a0), ad);
                }
                else if (Vector3.Cross(ac, acd).Dot(a0) > 0)
                {
                    simplex.Remove(b);
                    simplex.Remove(d);
                    direction = Vector3.Cross(Vector3.Cross(ac, a0), ac);
                }
                else
                {
                    simplex.Remove(b);
                    direction = acd;
                }
            }
            else if (abd.Dot(a0) > 0)
            {
                if (Vector3.Cross(abd, ab).Dot(a0) > 0)
                {
                    simplex.Remove(c);
                    simplex.Remove(d);
                    direction = Vector3.Cross(Vector3.Cross(ab, a0), ab);
                }
                else if (Vector3.Cross(ad, abd).Dot(a0) > 0)
                {
                    simplex.Remove(b);
                    simplex.Remove(c);
                    direction = Vector3.Cross(Vector3.Cross(ad, a0), ad);
                }
                else
                {
                    simplex.Remove(c);
                    direction = abd;
                }
            }
            else
            {
                return true;
            }

            return false;
        }
    }
}
