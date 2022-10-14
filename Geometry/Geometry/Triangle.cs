using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using JPLOPS.Util;

namespace JPLOPS.Geometry
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
        public virtual Vertex V0 { get; private set; }
        public virtual Vertex V1 { get; private set; }
        public virtual Vertex V2 { get; private set; }

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
        /// Creates a new triangle from 3 vector positions, all other vertex attributes will be set to default values
        /// </summary>
        /// <param name="v0"></param>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        public Triangle(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            this.V0 = new Vertex(v0);
            this.V1 = new Vertex(v1);
            this.V2 = new Vertex(v2);
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
        /// Randomly sample a point on the triangle
        /// </summary>
        /// <returns></returns>
        public BarycentricPoint Sample(Random rand)
        {
            double s = rand.NextDouble();
            double t = rand.NextDouble();
            if(s + t > 1)
            {
                s = 1 - s;
                t = 1 - t;
            }
            return new BarycentricPoint(s, t, this);

            
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
        /// Returns the area of the triangle or 0 if the triangle is not well formed 
        /// See this reference for numerically stable triangle area calculation
        /// https://people.eecs.berkeley.edu/~wkahan/Triangle.pdf
        /// </summary>
        /// <returns></returns>
        public double Area()
        {
            return Area(V0.Position, V1.Position, V2.Position);
        }

        public static double Area(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            // Compute the length of all 3 sides of the triangle
            double a = (v0 - v1).Length();
            double b = (v1 - v2).Length();
            double c = (v2 - v0).Length();

            void swap(ref double x, ref double y)
            {
                double tmp = x;
                x = y;
                y = tmp;
            }

            // Sort such that a >= b >= c
            if (a < b)
            {
                swap(ref a, ref b);
            }
            if (b < c)
            {
                swap(ref b, ref c);
            }
            if (a < b)
            {
                swap(ref a, ref b);
            }

            if (c - (a - b) < 0)
            {
                return 0; // Not a real triangle
            }

            double v = ((a + (b + c)) * (c - (a - b)) * (c + (a - b)) * (a + (b - c)));
            v = Math.Sqrt(v) / 4;
            return v;
        }

        /// <summary>
        /// Clips this traingle to the provided plane.
        /// Returns 0, 1, or 2 triangles representing the clipped geometry on or above the plane.
        /// </summary>
        public IEnumerable<Triangle> Clip(Plane plane)
        {
            List<Vertex> vertices = new List<Vertex>();

            // Returns the index of the added vertex.
            // If an identical vertex already exists do not add v but instead return the index of the existing vert.
            int addVertex(Vertex v)
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

            Vertex[][] edges = new Vertex[][]
            {
                new Vertex[] {V0, V1},
                new Vertex[] {V1, V2},
                new Vertex[] {V2, V0}
            };

            foreach (Vertex[] edge in edges)
            {
                // Plane.D is the negative of the distance from the origin to the plane in the direction of the normal
                double dist = -plane.D;

                if (Vector3.Dot(edge[0].Position, plane.Normal) < dist &&
                    Vector3.Dot(edge[1].Position, plane.Normal) < dist)
                {
                    // Skip this edge if both points are below the plane
                    continue;
                }

                else if (Vector3.Dot(edge[0].Position, plane.Normal) >= dist &&
                         Vector3.Dot(edge[1].Position, plane.Normal) >= dist)
                {
                    // Or above the plane
                    addVertex(edge[0]);
                    addVertex(edge[1]);
                    continue;
                }
                // Intersection vertex
                Vertex intervert = plane.Intersect(edge[0], edge[1]);
                if (intervert == null)
                {
                    // No intersection
                    addVertex(edge[0]);
                    addVertex(edge[1]);
                }
                else
                {
                    if (Vector3.Dot(edge[0].Position, plane.Normal) >= dist)
                    {
                        // First point is above the plane
                        addVertex(edge[0]);
                        addVertex(intervert);
                    }
                    else
                    {
                        // Second point is above the plane
                        addVertex(intervert);
                        addVertex(edge[1]);
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
        /// Picks a uniformly random point on the given triangle in barycentric coordinates and returns its cartesian
        /// coordinates
        /// </summary>
        /// <returns>Point in cartesian space of the random point on the triangle's surface</returns>
        public Vector3 RandomPoint(Random rng = null)
        {
            rng = rng ?? NumberHelper.MakeRandomGenerator();

            // Pick random coordinates across a square that gets squished onto the triangle
            double a = rng.NextDouble();
            double b = rng.NextDouble();

            // If the random coordinates cross the boundary of the triangle into the extents of the imaginary square,
            // flip back onto the triangle
            if (a + b >= 1)
            {
                a = 1 - a;
                b = 1 - b;
            }

            // Map the square coordinates onto the triangle
            Vector3 v1 = V0.Position;
            Vector3 v2 = V1.Position;
            Vector3 v3 = V2.Position;
            return v1 + a * (v2 - v1) + b * (v3 - v1);
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

            double u0 = uv0.X;
            double u1 = uv1.X;
            double u2 = uv2.X;

            double v0 = uv0.Y;
            double v1 = uv1.Y;
            double v2 = uv2.Y;

            double u = uv.X;
            double v = uv.Y;
            
            double b0 = (((v1 - v2) * (u  - u2) + (u2 - u1) * (v  - v2)) /
                         ((v1 - v2) * (u0 - u2) + (u2 - u1) * (v0 - v2)));

            double b1 = (((v2 - v0) * (u  - u2) + (u0 - u2) * (v  - v2)) /
                         ((v1 - v2) * (u0 - u2) + (u2 - u1) * (v0 - v2)));

            double b2 = 1.0f - b0 - b1;

            if (b0 >= 0 && b0 <= 1 && b1 >= 0 && b1 <= 1 && b2 >= 0 && b2 <= 1)
            {
                return new BarycentricPoint(b0, b1, b2, this);
            }

            return null;
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

        public bool TryComputeNormal(out Vector3 norm)
        {
            return ComputeNormal(V0.Position, V1.Position, V2.Position, out norm);
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
                throw new Exception("cannot compute normal, degenerate triangle");
            }
        }

        public static bool ComputeNormal(Vector3 v0, Vector3 v1, Vector3 v2, out Vector3 norm)
        {
            Vector3 v1v0 = v1 - v0;
            Vector3 v2v0 = v2 - v0;
            norm = Vector3.Cross(v1v0, v2v0);
            double eps = 1e-9;
            if (norm.Length() < eps)
            {
                return false;
            }
            norm.Normalize();
            return true;
        }

        public IEnumerable<Triangle> Clip(BoundingBox box)
        {
            if (box.Contains(this))
            {
                yield return this;
                yield break;
            }
            if (!Bounds().Intersects(box))
            {
                yield break;
            }
            // Note Plane.D is the negative of the distance from the origin to the plane in the direction of the normal
            var clipped = new Triangle[] { this }
                .SelectMany(tri => tri.Clip(new Plane(new Vector3(1, 0, 0), -box.Min.X)))
                .SelectMany(tri => tri.Clip(new Plane(new Vector3(-1, 0, 0), box.Max.X)))
                .SelectMany(tri => tri.Clip(new Plane(new Vector3(0, 1, 0), -box.Min.Y)))
                .SelectMany(tri => tri.Clip(new Plane(new Vector3(0, -1, 0), box.Max.Y)))
                .SelectMany(tri => tri.Clip(new Plane(new Vector3(0, 0, 1), -box.Min.Z)))
                .SelectMany(tri => tri.Clip(new Plane(new Vector3(0, 0, -1), box.Max.Z)));
            foreach (var tri in clipped)
            {
                yield return tri;
            }
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

        /// <summary>
        /// Subdivides this triangle along the plane and returns a list of the subsequent triangles
        /// This is accomplished by clipping the triangle once to get the portion above the plane, and then
        /// a second time to get the portion below the plane.  These two sets of triangles are unioned and returned.
        /// </summary>
        /// <param name="plane"></param>
        /// <returns></returns>
        public IEnumerable<Triangle> SplitAlongPlane(Plane plane)
        {
            // Clip in first direction
            foreach (var t in this.Clip(plane))
            {
                yield return t;
            }
            // Clip in other direction
            plane.D = -plane.D;
            plane.Normal = -plane.Normal;
            foreach (var t in this.Clip(plane))
            {
                yield return t;
            }
        }

        /// <summary>
        /// Cut this triangle to just the portion that is outside the given bounding box.  Return a list of new triangles
        /// that make up the resulting geometry.
        /// </summary>
        /// <param name="box"></param>
        /// <returns></returns>
        public IEnumerable<Triangle> Cut(BoundingBox box)
        {
            // This triangle does not intersect the box.  Clip nothing by returning the triangle
            if (!this.Bounds().Intersects(box))
            {
                return new Triangle[] { this };
            }
            IEnumerable<Triangle> clipped = new Triangle[] { this };
            // Clip each triangle against each plane of the box, against both directions
            // This will garantee we generate triangles on both sides of the box boundary with edges along the box
            // Note Plane.D is the negative of the distance from the origin to the plane in the direction of the normal
            clipped = clipped
                .SelectMany(tri => tri.SplitAlongPlane(new Plane(new Vector3(1, 0, 0), -box.Min.X)))
                .SelectMany(tri => tri.SplitAlongPlane(new Plane(new Vector3(-1, 0, 0), box.Max.X)))
                .SelectMany(tri => tri.SplitAlongPlane(new Plane(new Vector3(0, 1, 0), -box.Min.Y)))
                .SelectMany(tri => tri.SplitAlongPlane(new Plane(new Vector3(0, -1, 0), box.Max.Y)))
                .SelectMany(tri => tri.SplitAlongPlane(new Plane(new Vector3(0, 0, 1), -box.Min.Z)))
                .SelectMany(tri => tri.SplitAlongPlane(new Plane(new Vector3(0, 0, -1), box.Max.Z)));
            return clipped.Where(tri => !box.FuzzyContains(tri.Bounds()));
        }

        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 23 + V0.GetHashCode();
            hash = hash * 23 + V1.GetHashCode();
            hash = hash * 23 + V2.GetHashCode();
            return hash;
        }        

        public override bool Equals(System.Object obj)
        {
            return Equals(obj as Triangle);
        }

        public bool Equals(Triangle  t)
        {
            // For Equals implementation see https://msdn.microsoft.com/en-us/library/dd183755.aspx 
            // If parameter is null, return false.
            if (Object.ReferenceEquals(t, null))
            {
                return false;
            }

            // Optimization for a common success case.
            if (Object.ReferenceEquals(this, t))
            {
                return true;
            }

            // If run-time types are not exactly the same, return false.
            if (this.GetType() != t.GetType())
            {
                return false;
            }

            // Return true if the fields match.
            // Note that the base class is not invoked because it is
            // System.Object, which defines Equals as reference equality.
            return (V0 == t.V0) && (V1 == t.V1) && (V2 == t.V2);
        }
    }

    public class IndirectTriangle : Triangle
    {
        public override Vertex V0 { get { return mesh.Vertices[i0]; } }
        public override Vertex V1 { get { return mesh.Vertices[i1]; } }
        public override Vertex V2 { get { return mesh.Vertices[i2]; } }

        public readonly Mesh mesh;
        public readonly int i0, i1, i2;

        public IndirectTriangle(Mesh mesh, int i0, int i1, int i2)
        {
            this.mesh = mesh;
            this.i0 = i0;
            this.i1 = i1;
            this.i2 = i2;
        } 

        public IndirectTriangle(Mesh mesh, Face f) : this(mesh, f.P0, f.P1, f.P2)
        {
        } 

        public IndirectTriangle(Mesh mesh, int faceIndex) : this(mesh, mesh.Faces[faceIndex])
        {
        }
    }
}
