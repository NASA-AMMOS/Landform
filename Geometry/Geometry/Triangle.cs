using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;
using System.Diagnostics;

namespace OPS.Geometry
{
    /// <summary>
    /// The Triangle structure exists to simplify implementing some mesh operations.
    /// It is similar to a Face in that it represents a polygon with three vertices,
    /// however Triangles own their vertices while Faces only hold indices to vertices
    /// in an array.  This can make some mesh operations easier to implement by reducing
    /// the amount of indirection.
    ///
    /// Triangles always perform a deep copy of input vertices to avoid potential side effects
    ///
    /// A typical pattern is to convert a mesh into a list of traingles, perform an operation on the
    /// triangles, and then generate a new mesh from the array of triangles.  When generating a mesh
    /// from a list of triangles the mesh should deep copy the vertices so as to avoid side effects in
    /// the case that the triangles are later modified.
    ///
    /// Algorithms seeking to work with vertices by reference should consider operating on a list
    /// of Faces (such as the one in the Mesh object) instead.
    /// </summary>
    public class Triangle
    {
        public Vertex V0;
        public Vertex V1;
        public Vertex V2;

        public Triangle()
        {
        }

        /// <summary>
        /// Performs a deep copy of this triangle object
        /// The new triangle will NOT reference the Vertex
        /// objects of the input triangle
        /// </summary>
        /// <param name="that">A triangle to copy</param>
        public Triangle(Triangle that)
        {
            this.V0 = (Vertex)that.V0.Clone();
            this.V1 = (Vertex)that.V1.Clone();
            this.V2 = (Vertex)that.V2.Clone();
        }

        /// <summary>
        /// Creates a new triangle and performs a deep copy of the input vertices.
        /// Modification to the traingle will not affect the input vertices.
        /// </summary>
        /// <param name="v0"></param>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        public Triangle(Vertex v0, Vertex v1, Vertex v2)
        {
            this.V0 = (Vertex)v0.Clone();
            this.V1 = (Vertex)v1.Clone();
            this.V2 = (Vertex)v2.Clone();
        }

        /// <summary>
        /// Return the vertices of this triangle as a list.
        /// Note that these are NOT copies of the vertices and any changes to them
        /// will have side effects to the triangle.
        /// </summary>
        /// <returns></returns>
        public Vertex[] Vertices()
        {
            return new Vertex[] { V0, V1, V2 };
        }

        /// <summary>
        /// Returns an axis aligned bounding box for this triangle
        /// </summary>
        /// <returns></returns>
        public BoundingBox Bounds()
        {
            Vector3 min = Vector3.Min(V0.Position, Vector3.Min(V1.Position, V2.Position));
            Vector3 max = Vector3.Max(V0.Position, Vector3.Max(V1.Position, V2.Position));
            return new BoundingBox(min, max);
        }

        /// <summary>
        /// Returns a uv bounding box for this triangle
        /// </summary>
        /// <returns></returns>
        public BoundingBox UVBounds()
        {
            Vector2 min2 = Vector2.Min(V0.UV, Vector2.Min(V1.UV, V2.UV));
            Vector2 max2 = Vector2.Max(V0.UV, Vector2.Max(V1.UV, V2.UV));
            return new BoundingBox(new Vector3(min2.X, min2.Y, 0), new Vector3(max2.X, max2.Y, 0));
        }

        /// <summary>
        /// Returns the area of the triangle
        /// </summary>
        /// <returns></returns>
        public double Area()
        {
            double a = (this.V0.Position - this.V1.Position).Length();
            double b = (this.V1.Position - this.V2.Position).Length();
            double c = (this.V2.Position - this.V0.Position).Length();
            double s = (a + b + c) / 2;
            return Math.Sqrt(s * (s - a) * (s - b) * (s - c));
        }

        /// <summary>
        /// Helper method for adding vertices to an array.  Returns the index of the added vertex.
        /// If an identical vertex already exists do not add v but instead return the index of the existing vert.
        /// </summary>
        /// <param name="vertices"></param>
        /// <param name="v"></param>
        /// <returns></returns>
        static int AddVertex(List<Vertex> vertices, Vertex v)
        {
            int i;
            for (i = 0; i < vertices.Count; i++)
            {
                if ((vertices[i].AlmostEqual(v)))
                {
                    return i;
                }
            }
            int res = vertices.Count;
            vertices.Add(v);
            return res;
        }

        /// <summary>
        /// Clips this traingle to the provided plane.  Returns 0, 1, or 2 triangles
        /// representing the clipped geometry
        /// </summary>
        /// <param name="plane"></param>
        /// <returns></returns>
        public IEnumerable<Triangle> Clip(Plane plane)
        {
            List<Vertex> vertices = new List<Vertex>();
            Vertex[][] edges = new Vertex[][]
            {
                new Vertex[] {V0, V1},
                new Vertex[] {V1, V2},
                new Vertex[] {V2, V0}
            };
            foreach (Vertex[] edge in edges)
            {

                if (Vector3.Dot(edge[0].Position, plane.Normal) < plane.D &&
                    Vector3.Dot(edge[1].Position, plane.Normal) < plane.D)
                {
                    // Skip this edge if both points are below the plane
                    continue;
                }

                else if (Vector3.Dot(edge[0].Position, plane.Normal) >= plane.D &&
                         Vector3.Dot(edge[1].Position, plane.Normal) >= plane.D)
                {
                    // Or above the plane
                    AddVertex(vertices, edge[0]);
                    AddVertex(vertices, edge[1]);
                    continue;
                }
                // Intersection vertex
                Vertex intervert = plane.Intersect(edge[0], edge[1]);
                if (intervert == null)
                {
                    // No intersection
                    AddVertex(vertices, edge[0]);
                    AddVertex(vertices, edge[1]);
                }
                else
                {
                    if (Vector3.Dot(edge[0].Position, plane.Normal) >= plane.D)
                    {
                        // First point is above the plane
                        AddVertex(vertices, edge[0]);
                        AddVertex(vertices, intervert);
                    }
                    else
                    {
                        // Second point is above the plane
                        AddVertex(vertices, intervert);
                        AddVertex(vertices, edge[1]);
                    }
                }
            }
            if (vertices.Count < 3)
            {
                // Degenerate triangle or entirely below the plane.
                yield break;
            }
            else if (vertices.Count == 3)
            {
                yield return new Triangle(vertices[0], vertices[1], vertices[2]);
            }
            else if (vertices.Count == 4)
            {
                yield return new Triangle(vertices[0], vertices[1], vertices[3]);
                yield return new Triangle(vertices[1], vertices[2], vertices[3]);
            }
            else
            {
                Debug.Fail("Triangle.Clip produced an invalid number of points");
            }
        }
        
        /// <summary>
        /// Returns the squared distance between p and the the nearest point on this triangle
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public double SquaredDistance(Vector3 p)
        {
            BarycentricPoint closestPoint = ClosestPoint(p);
            return Vector3.DistanceSquared(p, closestPoint.Position);
        }

        /// <summary>
        /// Get the closest point on the triangle to p
        /// from https://www.geometrictools.com/Documentation/DistancePoint3Triangle3.pdf
        /// matlab implementation at http://www.mathworks.com/matlabcentral/fileexchange/22857-distance-between-a-point-and-a-triangle-in-3d
        /// </summary>
        /// <param name="P"></param>
        /// <returns></returns>
        public BarycentricPoint ClosestPoint(Vector3 P)
        {
            //Points on triangle can be parameterized in 2d as T(s, t) = B + s(E_0) + t(E_1), for s, t >= 0 and s + t <= 1
            Vector3 B = V0.Position;
            Vector3 E_0 = V1.Position - B;
            Vector3 E_1 = V2.Position - B;

            //Often used values
            Vector3 D = B - P;
            double a = E_0.Dot(E_0);
            double b = E_0.Dot(E_1);
            double c = E_1.Dot(E_1);
            double d = E_0.Dot(D);
            double e = E_1.Dot(D);
            double f = D.Dot(D);

            double det = a * c - b * b;
            double s = b * e - c * d;
            double t = b * d - a * e;

            if (s + t <= det)
            {
                if (s < 0)
                {
                    if (t < 0)
                    {
                        //region4
                        if (d < 0)
                        {
                            t = 0;
                            s = (-d >= a ? 1 : -d / a);
                        }
                        else
                        {
                            s = 0;
                            t = (e >= 0 ? 0 : (-e >= c ? 1 : -e / c));
                        }
                    }
                    else
                    {
                        //region3
                        s = 0;
                        t = (e >= 0 ? 0 : (-e >= c ? 1 : -e / c));
                    }
                }
                else if (t < 0)
                {
                    //region5
                    t = 0;
                    s = (d >= 0 ? 0 : (-d >= a ? 1 : -d / a));
                }
                else
                {
                    //region0
                    double invDet = 1 / det;
                    s *= invDet;
                    t *= invDet;
                }
            }
            else
            {
                if (s < 0)
                {
                    //region 2
                    double tmp0 = b + d;
                    double tmp1 = c + e;
                    if (tmp1 > tmp0)
                    {
                        double numer = tmp1 - tmp0;
                        double denom = a - 2 * b + c;
                        s = (numer >= denom ? 1 : numer / denom);
                        t = 1 - s;
                    }
                    else
                    {
                        s = 0;
                        t = (tmp1 <= 0 ? 1 : (e >= 0 ? 0 : -e / c));
                    }
                }
                else if (t < 0)
                {
                    //region6
                    double tmp0 = b + e;
                    double tmp1 = a + d;
                    if (tmp1 > tmp0)
                    {
                        double numer = tmp1 - tmp0;
                        double denom = a - 2 * b + c;
                        t = (numer >= denom ? 1 : numer / denom);
                        s = 1 - t;
                    }
                    else
                    {
                        t = 0;
                        s = (tmp1 <= 0 ? 1 : (d >= 0 ? 0 : -d / a));
                    }
                }
                else
                {
                    //region1
                    double numer = c + e - b - d;
                    if (numer <= 0)
                    {
                        s = 0;
                    }
                    else
                    {
                        double denom = a - 2 * b + c;
                        s = (numer >= denom ? 1 : numer / denom);
                    }
                    t = 1 - s;
                }
            }

            if (s < 0) s = 0;
            if (t < 0) t = 0;

            return new BarycentricPoint(s, t, this);
        }

        /// <summary>
        /// Finds the center of the triangle in barycentric coordinates and returns its position
        /// </summary>
        /// <returns></returns>
        public Vector3 Barycenter()
        {
            double oneThird = 1.0 / 3.0;
            return new BarycentricPoint(oneThird, oneThird, oneThird, this).Position;
        }
        
        /// <summary>
        /// Given a uv coordinate, returns the the barycentric position if is within
        /// the bounds of the triangle.  Null otherwise.
        /// </summary>
        /// <param name="uv"></param>
        /// <returns></returns>
        public BarycentricPoint UVToBarycentric(Vector2 uv)
        {
            //Pat Sweeney port:

            Vector2 uv0 = V0.UV;
            Vector2 uv1 = V1.UV;
            Vector2 uv2 = V2.UV;

            Vector3 v0 = V0.Position;
            Vector3 v1 = V1.Position;
            Vector3 v2 = V2.Position;

            double lowLimit = 0;
            double highLimit = 1;

            double x1 = uv0.X;
            double x2 = uv1.X;
            double x3 = uv2.X;
            double y1 = uv0.Y;
            double y2 = uv1.Y;
            double y3 = uv2.Y;
            double xf = uv.X;
            double yf = uv.Y;

            double b0 = (((y2 - y3) * (xf - x3) + (x3 - x2) * (yf - y3)) /
                ((y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3)));
            double b1 = (((y3 - y1) * (xf - x3) + (x1 - x3) * (yf - y3)) /
                ((y2 - y3) * (x1 - x3) + (x3 - x2) * (y1 - y3)));
            double b2 = 1.0f - b0 - b1;

            BarycentricPoint r = null;
            if (b0 >= lowLimit && b0 <= highLimit &&
                b1 >= lowLimit && b1 <= highLimit &&
                b2 >= lowLimit && b2 <= highLimit)
            {
                r = new BarycentricPoint(b0, b1, b2, this);
            }
            return r;
        }

        /// <summary>
        /// Returns a normal for this face.  Normal is determined by position and winding order of vertices
        /// </summary>
        public Vector3 Normal
        {
            get
            {
                return ComputeNormal(V0.Position, V1.Position, V2.Position);
            }
        }

        public static Vector3 ComputeNormal(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            Vector3 norm;
            if (ComputeNormal(v0, v1, v2, out norm))
            {
                return norm;
            }
            else
            {
                throw new Exception("Normal error, Zero length face");
            }
        }

        public static bool ComputeNormal(Vector3 v0, Vector3 v1, Vector3 v2, out Vector3 norm)
        {
            Vector3 v1v0 = v1 - v0;
            Vector3 v2v0 = v2 - v0;
            norm = Vector3.Cross(v1v0, v2v0);
            // Normalize
            if (norm.Length() > 0)
            {
                norm.Normalize();
            }
            else
            {
                return false;
            }
            return true;
        }

        public IEnumerable<Triangle> Clip(BoundingBox box)
        {
            // This triangle does not intersect the box.  Clip everything by returning an empty list
            if(!this.Bounds().Intersects(box))
            {
                return new Triangle[] { };
            }
            Vector3 size = box.Size();
            IEnumerable<Triangle> clipped = new Triangle[] { this };
            clipped = clipped.SelectMany(tri => tri.Clip(new Plane(new Vector3(1, 0, 0), box.Min.X)))
                                .SelectMany(tri => tri.Clip(new Plane(new Vector3(-1, 0, 0), -(box.Min.X + size.X))))
                                .SelectMany(tri => tri.Clip(new Plane(new Vector3(0, 1, 0), box.Min.Y)))
                                .SelectMany(tri => tri.Clip(new Plane(new Vector3(0, -1, 0), -(box.Min.Y + size.Y))))
                                .SelectMany(tri => tri.Clip(new Plane(new Vector3(0, 0, 1), box.Min.Z)))
                                .SelectMany(tri => tri.Clip(new Plane(new Vector3(0, 0, -1), -(box.Min.Z + size.Z))));
            return clipped;
        }

        /// <summary>
        /// Returns true if this triangle intersects the given bounding box
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        public bool Intersects(BoundingBox box)
        {
            return Clip(box).Count() > 0;
        }
    }
}
