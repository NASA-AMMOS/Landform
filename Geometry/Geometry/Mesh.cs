using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.Xna.Framework;
using System.Diagnostics;

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
            Clean();
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

        bool FaceIsValid(Face f)
        {
            // Are any two of the vertices referenced by this face the same index
            if (!f.IsValid())
            {
                return false;
            }
            // Are any of the faces vertices at the same location
            if (Vertices[f.P0].Position == Vertices[f.P1].Position ||
                Vertices[f.P1].Position == Vertices[f.P2].Position ||
                Vertices[f.P2].Position == Vertices[f.P0].Position)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Returns true if any face in the mesh has 2 or more of its vertices in the same position (zero area face)
        /// </summary>
        /// <returns></returns>
        public bool HasInvalidFaces()
        {
            foreach(var f in Faces)
            {
                if(!FaceIsValid(f))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Removes any invalid faces
        /// An invalid face is one which has two or more vertices at the same location
        /// </summary>
        public void RemoveInvalidFaces()
        {
            List<Face> validFaces = new List<Face>();
            foreach (var f in Faces)
            {
                if (FaceIsValid(f))
                {
                    validFaces.Add(f);
                }
            }
            this.Faces = validFaces;
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
        /// Removes logically identical faces.  Two faces are logically identical
        /// if they have the same winding and identical vertices.  Note that we
        /// compare vertex equivalence and not just indices.
        /// </summary>
        public void RemoveDuplicateFaces()
        {
            // Create a mapping from each vertex to a list of face indices that contain that vertex
            Dictionary<Vertex, HashSet<int>> vertexToFaceIndex = new Dictionary<Vertex, HashSet<int>>();
            for (int i = 0; i < this.Faces.Count; i++)
            {
                Vertex[] vs = FaceToVertexArray(Faces[i]);
                for(int k = 0; k < vs.Length; k++)
                {
                    if(!vertexToFaceIndex.ContainsKey(vs[k]))
                    {
                        vertexToFaceIndex.Add(vs[k], new HashSet<int>());
                    }
                    vertexToFaceIndex[vs[k]].Add(i);
                }
            }

            // Make a list of unique faces by taking the first occurence of each face
            List<Face> uniqueFaces = new List<Face>();
            // For each face i
            for (int i = 0; i < this.Faces.Count; i++)
            {                
                // If there is another face like this one, then all of the other faces vertices must be identical
                // Thus we can just look up the hashset for one of this faces vertices
                HashSet<int> potentiallyIdenticalFaces = vertexToFaceIndex[this.Vertices[this.Faces[i].P0]];

                // Check to see if there are any faces are identical to this one AND has a smaller index (occures earlier in the face list)
                // if so we are not the first occurence of this face
                bool isFirstInstance = true;
                foreach (int j in potentiallyIdenticalFaces)
                {
                    if(j < i)
                    {
                        // Check the three possible offsets the vertices could have
                        Vertex[] a = FaceToVertexArray(Faces[i]);
                        Vertex[] b = FaceToVertexArray(Faces[j]);                        
                        for (int offset = 0; offset < 3; offset++)
                        {
                            // For each offset, check to see if the vertices are identical between the faces
                            if (a[0].Equals(b[(0 + offset) % 3]) && a[1].Equals(b[(1 + offset) % 3]) && a[2].Equals(b[(2 + offset) % 3]))
                            {
                                isFirstInstance = false;
                                break;
                            }
                        }
                    }                    
                }
                if(isFirstInstance)
                {
                    uniqueFaces.Add(this.Faces[i]);
                }
            }
            this.Faces = uniqueFaces;
        }

        /// <summary>
        /// Removes any vertices that are not referenced by a face.  
        /// </summary>
        public void RemoveUnreferencedVertices()
        {
            // Mark which vertices are referenced by faces
            HashSet<int> referencedIndices = new HashSet<int>();
            for(int i = 0; i < this.Faces.Count; i++)
            {
                referencedIndices.Add(this.Faces[i].P0);
                referencedIndices.Add(this.Faces[i].P1);
                referencedIndices.Add(this.Faces[i].P2);
            }
            // Remove unused vertices
            List<Vertex> referencedVertices = new List<Vertex>();
            Dictionary<int, int> oldToNewIndex = new Dictionary<int, int>();
            for(int i = 0; i < this.Vertices.Count; i++)
            {
                // Is this vertex referenced by a face?
                if(referencedIndices.Contains(i))
                {
                    oldToNewIndex.Add(i, referencedVertices.Count);
                    referencedVertices.Add(this.Vertices[i]);                    
                }
            }
            this.Vertices = referencedVertices;
            // Update face indices
            for(int i = 0; i < this.Faces.Count; i++)
            {
                Face f = this.Faces[i];
                f.P0 = oldToNewIndex[f.P0];
                f.P1 = oldToNewIndex[f.P1];
                f.P2 = oldToNewIndex[f.P2];
                this.Faces[i] = f;
            }
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
        /// Removes duplicate and degenerate faces
        /// Removes duplicate vertices
        /// If any faces are defined this will also remove any vertices that are not referenced
        /// by a face
        /// </summary>
        public void Clean()
        {
            RemoveDuplicateVertices();
            if (HasFaces)
            {
                RemoveInvalidFaces();
                RemoveUnreferencedVertices();
                RemoveDuplicateFaces();
            }
        }

        public void Transform(Matrix m)
        {
            foreach(Vertex v in this.Vertices)
            {
                v.Position = Vector3.Transform(v.Position, m);
                if(this.HasNormals)
                {
                    v.Normal = Vector3.TransformNormal(v.Normal, m);
                }
            }
        }

        public void Translate(Vector3 offset)
        {
            for (int i = 0; i < this.Vertices.Count; i++)
            {
                this.Vertices[i].Position += offset;
            }
        }

        Vertex[] FaceToVertexArray(Face f)
        {
            return new Vertex[] { this.Vertices[f.P0], this.Vertices[f.P1], this.Vertices[f.P2] };
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

        /// <summary>
        /// Checks to see if this mesh has the same attributes as the other mesh (normal, uv, and texture)
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool AttributesEqual(Mesh other)
        {
            return this.HasNormals == other.HasNormals && this.HasUVs == other.HasUVs && this.HasColors == other.HasColors;
        }


        /// <summary>
        /// Return true if all attributes that are true of this mesh are also true of other
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool AttributesAlsoExistIn(Mesh other)
        {
            if ((this.HasNormals && !other.HasNormals) || (this.HasUVs && !other.HasUVs) || (this.HasColors && !other.HasColors))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Combines one or more meshes with this one
        /// The proprties of the input meshes must match this one
        /// Vertex objects are cloned to avoid side effects in case the meshes are modifed in the future
        /// </summary>
        /// <param name="otherMeshes"></param>
        public void MergeWith(params Mesh[] otherMeshes)
        {
            for (int i = 0; i < otherMeshes.Length; i++)
            {
                Mesh m = otherMeshes[i];
                if(!AttributesAlsoExistIn(m))
                {
                    throw new Exception("Mesh to merge missing one or more attributes required by aggregate mesh");
                }
                int vertexBaseCount = this.Vertices.Count;
                for (int j = 0; j < m.Vertices.Count; j++)
                {
                    this.Vertices.Add((Vertex)m.Vertices[j].Clone());
                }
                for (int j = 0; j < m.Faces.Count; j++)
                {
                    Face f = new Face(m.Faces[j]);
                    f.P0 += vertexBaseCount;
                    f.P1 += vertexBaseCount;
                    f.P2 += vertexBaseCount;
                    this.Faces.Add(f);
                }
            }
            Clean();
        }

        /// <summary>
        /// Combines and returns one or more meshes
        /// The proprties of the input meshes must match this one
        /// Vertex objects are cloned to avoid side effects in case the meshes are modifed in the future
        /// </summary>
        /// <param name="meshesToCombine"></param>
        /// <returns></returns>
        public static Mesh Merge(params Mesh[] meshesToCombine)
        {
            Mesh first = meshesToCombine[0];
            return Merge(first.HasNormals, first.HasUVs, first.HasColors, meshesToCombine);
        }

        /// <summary>
        /// Combines several meshes and returnes a new mesh with the specified attributes
        /// </summary>
        /// <param name="hasNormals"></param>
        /// <param name="hasUvs"></param>
        /// <param name="hasColors"></param>
        /// <param name="meshesToCombine"></param>
        /// <returns></returns>
        public static Mesh Merge(bool hasNormals, bool hasUvs, bool hasColors, params Mesh[] meshesToCombine)
        {
            Mesh result = new Mesh(hasNormals, hasUvs, hasColors);
            result.MergeWith(meshesToCombine);
            return result;
        }

        public static Mesh Clip(Mesh m, BoundingBox box)
        {
            Mesh result;
            if (m.Faces.Count > 0)
            {
                List<Triangle> resTriangles = new List<Triangle>();
                foreach (Face f in m.Faces)
                {
                    Vertex v0 = m.Vertices[f.P0];
                    Vertex v1 = m.Vertices[f.P1];
                    Vertex v2 = m.Vertices[f.P2];
                    Triangle t = new Triangle(v0, v1, v2);
                    resTriangles.AddRange(t.Clip(box));
                }
                result = new Mesh(resTriangles, m.HasNormals, m.HasUVs, m.HasColors);
            }
            else
            {
                result = new Mesh(m.HasNormals, m.HasUVs, m.HasColors);
                // this is a point cloud
                foreach (var v in m.Vertices)
                {
                    if(box.Contains(v.Position) != ContainmentType.Disjoint)
                    {
                        result.Vertices.Add(v);
                    }
                }
            }
            Debug.Assert(box.FuzzyContains(result.Bounds()), "Clipped mesh exceeds bounding box");
            return result;
        }

        /// <summary>
        /// Removes specified vertices from this mesh
        /// Also removes any faces that reference a removed vertex 
        /// </summary>
        /// <param name="vertices"></param>
        public void RemoveVertices(List<Vertex> vertices)
        {
            Dictionary<int, Vertex> originalIndexToVert = new Dictionary<int,Vertex>();
            Dictionary<int, int> originalToClippedIndex = new Dictionary<int, int>();
            HashSet<Vertex> vertsToRemove = new HashSet<Vertex>(vertices);
            List<Vertex> clippedVerts = new List<Vertex>();
            // Loop through all existing vertices and determine which ones to keep
            // Record original and new indices
            for(int i = 0; i <  this.Vertices.Count; i++)
            {
                Vertex v = this.Vertices[i];
                originalIndexToVert.Add(i, v);
                if (!vertsToRemove.Contains(v))
                {
                    originalToClippedIndex.Add(i, clippedVerts.Count);
                    clippedVerts.Add(v);
                }
            }
            // Remove faces that reference removed vertices
            // Remap face indices to new vertex list
            List<Face> clippedFaces = new List<Face>();
            for(int i = 0; i < this.Faces.Count; i++)
            {
                Face face = this.Faces[i];
                // Keep this face only if none of it's vertices have been clipped
                bool keep = face.ToArray().All(j => originalToClippedIndex.ContainsKey(j));
                if (keep)
                {
                    clippedFaces.Add(new Face(face.ToArray().Select(j => originalToClippedIndex[j]).ToArray()));
                }
            }
            this.Vertices = clippedVerts;
            this.Faces = clippedFaces;
        }

        /// <summary>
        /// Reverse the winding of faces - i.e. make them face the other direction
        /// </summary>
        public void ReverseWinding()
        {
            for(int i = 0; i < this.Faces.Count; i++)
            {
                Face f = this.Faces[i];
                this.Faces[i] = new Face(f.P0, f.P2, f.P1);
            }
        }

        /// <summary>
        /// Returns a box thats bounds encompass the vertex positions in 3D space
        /// </summary>
        /// <returns></returns>
        public BoundingBox Bounds()
        {
            BoundingBox b = new BoundingBox(Vector3.Largest, Vector3.Smallest);
            foreach (Vertex v in this.Vertices)
            {
                b.Min = Vector3.Min(b.Min, v.Position);
                b.Max = Vector3.Max(b.Max, v.Position);
            }
            return b;
        }

        /// <summary>
        /// Returns a bounding box whose min/max represent the component wise minimum and maximum across all vertex normals
        /// </summary>
        /// <returns></returns>
        public BoundingBox NormalBounds()
        {
            BoundingBox b = new BoundingBox(Vector3.Largest, Vector3.Smallest);
            foreach (Vertex v in this.Vertices)
            {
                b.Min = Vector3.Min(b.Min, v.Normal);
                b.Max = Vector3.Max(b.Max, v.Normal);
            }
            return b;
        }

        /// <summary>
        /// Returns a bounding box whose min/max represent the component wise minimum and maximum across all vertex uvs
        /// Since min and max are 3D vectors the z components are set to 0
        /// </summary>
        /// <returns></returns>
        public BoundingBox UVBounds()
        {
            BoundingBox b = new BoundingBox(Vector3.Largest, Vector3.Smallest);
            foreach (Vertex v in this.Vertices)
            {
                b.Min = Vector3.Min(b.Min, new Vector3(v.UV, 0));
                b.Max = Vector3.Max(b.Max, new Vector3(v.UV, 0));
            }
            return b;
        }

        /// <summary>
        /// Save a mesh to disk with an optional filename
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="textureFilename"></param>
        public void Save(string filename, string textureFilename = null)
        {
            string ext = Path.GetExtension(filename).ToLower();
            MeshSerializer s = MeshSerializers.GetSerializer(ext);
            if(s == null)
            {
                throw new MeshSerializerException("Mesh format not supported");
            }
            s.Save(this, filename, textureFilename);
        }

        /// <summary>
        /// Read a mesh to disk
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static Mesh Load(string filename)
        {
            string ext = Path.GetExtension(filename).ToLower();
            MeshSerializer s = MeshSerializers.GetSerializer(ext);
            if (s == null)
            {
                throw new MeshSerializerException("Mesh format not supported");
            }
            return s.Load(filename);
        }
    }
}
