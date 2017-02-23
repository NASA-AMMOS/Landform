using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

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
        public bool HasFaces { get { return Faces.Count > 0; } }

        /// <summary>
        /// Creates an empty mesh. 
        /// </summary>
        /// <param name="capacity"></param>
        public Mesh(bool hasNormals = false, bool hasUVs = false, bool hasColors = false, int capacity = 10)
        {
            Faces = new List<Face>(capacity);
            Vertices = new List<Vertex>(capacity);
            SetProperties(hasNormals, hasUVs, hasColors);
        }

        /// <summary>
        /// Creates a deep copy of another mesh
        /// Uses the Vertex.Clone() method so that the new mesh has its own copy of vertices and 
        /// extended vertex types can persist addtional properties
        /// </summary>
        /// <param name="other"></param>
        public Mesh(Mesh other)
        {
            this.Faces = new List<Face>(other.Faces.Count);
            for(int i = 0; i < other.Faces.Count; i++)
            {
                this.Faces.Add(other.Faces[i]);
            }
            this.Vertices = new List<Vertex>(other.Vertices.Count);
            for (int i = 0; i < other.Vertices.Count; i++)
            {
                this.Vertices.Add((Vertex)other.Vertices[i].Clone());
            }
            SetProperties(other.HasNormals, other.HasUVs, other.HasColors);
        }

        /// <summary>
        /// Creates a mesh using a list of triangles.  Performs a clone on triangle vertices to avoid side effects
        /// in the case that triangles are modified later
        /// </summary>
        /// <param name="triangles"></param>
        /// <param name="hasNormals"></param>
        /// <param name="hasUVs"></param>
        /// <param name="hasColors"></param>
        public Mesh(List<Triangle> triangles, bool hasNormals = false, bool hasUVs = false, bool hasColors = false)
        {
            Faces = new List<Face>(triangles.Count);
            Vertices = new List<Vertex>(triangles.Count * 3);
            SetProperties(hasNormals, hasUVs, hasColors);
            int idx = 0;
            foreach (Triangle t in triangles)
            {
                Faces.Add(new Face(idx, idx + 1, idx + 2));
                idx += 3;
                Vertices.Add((Vertex)t.V0.Clone());
                Vertices.Add((Vertex)t.V1.Clone());
                Vertices.Add((Vertex)t.V2.Clone());
            }
        }

        /// <summary>
        /// Determines what values in the vertex structure are considered to have valid data
        /// </summary>
        /// <param name="hasNormals"></param>
        /// <param name="hasUVs"></param>
        /// <param name="hasColors"></param>
        public void SetProperties(bool hasNormals, bool hasUVs, bool hasColors)
        {
            this.HasNormals = hasNormals;
            this.HasUVs = hasUVs;
            this.HasColors = hasColors;
        }

        /// <summary>
        /// Returns true if any face in the mesh has 2 or more of its vertices in the same position (zero area face)
        /// </summary>
        /// <returns></returns>
        public bool HasInvalidFaces()
        {
            foreach(var f in Faces)
            {
                // Are any two of the vertices referenced by this face the same index
                if(!f.IsValid())
                {
                    return true;
                }
                // Are any of the faces vertices at the same location
                if(Vertices[f.P0].Position == Vertices[f.P1].Position ||                   
                   Vertices[f.P1].Position == Vertices[f.P2].Position ||
                   Vertices[f.P2].Position == Vertices[f.P0].Position)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes any identical faces
        /// Note that this method only removes faces that are strictly identical
        /// Faces that have the same indicies and in the same order (winding) but with different
        /// offsets will not be removed.  Simillarly, faces that have different vertices but are 
        /// logically identifcal because their vertices have identical properties will not be removed
        /// </summary>
        public void RemoveIdenticalFaces()
        {
            HashSet<Face> fs = new HashSet<Face>();
            List<Face> uniqueFaces = new List<Face>();
            for (int i = 0; i < this.Faces.Count; i++)
            {
                if (!fs.Contains(this.Faces[i]))
                {
                    uniqueFaces.Add(this.Faces[i]);
                    fs.Add(this.Faces[i]);
                }
            }
            this.Faces = uniqueFaces;
        }

        /// <summary>
        /// Remove any vertices that are identical
        /// Also checks for and removes any identical faces
        /// </summary>
        public void RemoveDuplicateVertices()
        {
            // Make a list of unique vertices and compute a mapping between old and new indices
            Dictionary<Vertex, int> vertexToIndex = new Dictionary<Vertex, int>();
            Dictionary<int, int> oldToNewIndex = new Dictionary<int, int>();
            List<Vertex> uniqueVertices = new List<Vertex>();
            for(int i = 0; i < this.Vertices.Count; i++)
            {
                Vertex v = this.Vertices[i];
                if (!vertexToIndex.ContainsKey(v))
                {
                    vertexToIndex.Add(v, vertexToIndex.Count);
                    uniqueVertices.Add(v);
                }
                oldToNewIndex.Add(i, vertexToIndex[v]);                             
            }
            // Update the vertex list
            this.Vertices = uniqueVertices;
            // Update the face indices
            for(int i = 0; i < this.Faces.Count; i++)
            {
                Face f = this.Faces[i];
                f.P0 = oldToNewIndex[f.P0];
                f.P1 = oldToNewIndex[f.P1];
                f.P2 = oldToNewIndex[f.P2];
                this.Faces[i] = f;
            }
            RemoveIdenticalFaces();
        }
        
        /// <summary>
        /// Returns a list of triangles for this mesh.  Triangles each contain thier own
        /// clone of vertices so modifications to the triangles or mesh will not have side effects on the other
        /// </summary>
        /// <returns></returns>
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

        public void Save(string filename, string textureFilename = null)
        {
            string ext = Path.GetExtension(filename).ToLower();
            if (ext.Equals(".obj"))
            {
                OBJSerializer.Write(this, filename, textureFilename);
            }
            else if(ext.Equals(".ply"))
            {
                PLYSerializer.Write(this, filename, textureFilename);
            }
            else
            {
                throw new MeshSerializerException("Mesh format not supported");
            }
        }

        public static Mesh Load(string filename)
        {
            string ext = Path.GetExtension(filename).ToLower();
            if (ext.Equals(".obj"))
            {
                return OBJSerializer.Read(filename);
            }
            else if (ext.Equals(".ply"))
            {
                return PLYSerializer.Read(filename);
            }
            else
            {
                throw new MeshSerializerException("Mesh format not supported");
            }
        }
    }
}
