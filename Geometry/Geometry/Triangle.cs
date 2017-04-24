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
    }
}