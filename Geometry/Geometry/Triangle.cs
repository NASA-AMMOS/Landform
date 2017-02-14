using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
            this.V0 = (Vertex) that.V0.Clone();
            this.V1 = (Vertex) that.V1.Clone();
            this.V2 = (Vertex) that.V2.Clone();
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
            this.V0 = (Vertex) v0.Clone();
            this.V1 = (Vertex) v1.Clone();
            this.V2 = (Vertex) v2.Clone();
        }
    }
}