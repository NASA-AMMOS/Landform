using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OPS.Geometry
{
    /// <summary>
    /// A class representing a 3D mesh
    /// 
    /// Each mesh is comprised of a list of vertices and a faces
    /// 
    /// Each of the vertices must have a valid position value, but all other properties of the vertex structure
    /// are optional.  Properties are defined on a per mesh basis, either a property is defined for all of 
    /// a mesh's vertices or none of them.  The flags controlled by SetProperties determine what properties
    /// a mesh has and undefined properties are ingored by meshing operations.
    /// 
    /// </summary>
    public class Mesh
    {
        public List<Face> Faces;
        public List<Vertex> Vertices;
        
        public bool HasNormals = false;
        public bool HasUVs = false;
        public bool HasColors = false;

        /// <summary>
        /// Creates an empty mesh. 
        /// </summary>
        /// <param name="capacity"></param>
        public Mesh(int capacity=10, bool hasNormals = false, bool hasUVs = false, bool hasColors = false)
        {
            Faces = new List<Face>(capacity);
            Vertices = new List<Vertex>(capacity);
            SetProperties(hasNormals, hasUVs, HasColors);
        }

        public Mesh(Mesh other)
        {
            Faces = new List<Face>(other.Faces.Count);
            for(int i = 0; i < Faces.Count; i++)
            {
                Faces[i] = other.Faces[i];
            }
            Vertices = new List<Vertex>(other.Vertices.Count);
            for (int i = 0; i < Vertices.Count; i++)
            {
                Vertices[i] = new Vertex(other.Vertices[i]);
            }
            SetProperties(other.HasNormals, other.HasUVs, other.HasColors);
        }

        public Mesh(List<Triangle> triangles, bool hasNormals = false, bool hasUVs = false, bool hasColors = false)
        {
            Faces = new List<Face>(triangles.Count);
            Vertices = new List<Vertex>(triangles.Count * 3);
            SetProperties(hasNormals, hasUVs, HasColors);
            int idx = 0;
            foreach (Triangle t in triangles)
            {
                Faces.Add(new Face(idx, idx + 1, idx + 2));
                idx += 3;
                Vertices.Add(t.V0);
                Vertices.Add(t.V1);
                Vertices.Add(t.V2);
            }
        }

        public void SetProperties(bool hasNormals, bool hasUVs, bool hasColors)
        {
            this.HasNormals = hasNormals;
            this.HasUVs = hasUVs;
            this.HasColors = hasColors;
        }

        public List<Triangle> Triangles()
        {
            List<Triangle> triangles = new List<Triangle>(Faces.Count);
            foreach (Face f in Faces)
            {
                Triangle t = new Triangle(Vertices[f.P0],
                                          Vertices[f.P1],
                                          Vertices[f.P2]);
                triangles.Add(t);
            }
            return triangles;
        }
    }
}
