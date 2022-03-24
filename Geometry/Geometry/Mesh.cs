using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
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
    /// </summary>
    public class Mesh
    {
        public List<Face> Faces;
        public List<Vertex> Vertices;

        public bool HasNormals = false;
        public bool HasUVs = false;
        public bool HasColors = false;
        public bool HasFaces { get { return Faces.Count > 0; } }
        public bool HasVertices { get { return Vertices.Count > 0; } }

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
            Faces = new List<Face>(other.Faces.Count);
            for (int i = 0; i < other.Faces.Count; i++)
            {
                Faces.Add(other.Faces[i]);
            }
            Vertices = new List<Vertex>(other.Vertices.Count);
            for (int i = 0; i < other.Vertices.Count; i++)
            {
                Vertices.Add((Vertex)other.Vertices[i].Clone());
            }
            SetProperties(other);
        }

        /// <summary>
        /// Creates a mesh using a list of triangles.  Performs a clone on triangle vertices to avoid side effects
        /// in the case that triangles are modified later
        /// </summary>
        public Mesh(IEnumerable<Triangle> triangles,
                    bool hasNormals = false, bool hasUVs = false, bool hasColors = false, Action<string> warn = null)
        {
            SetProperties(hasNormals, hasUVs, hasColors);
            SetTriangles(triangles);
        }

        /// <summary>
        /// Copies the data from the triangles.  Cleans the mesh, including removing invalid and duplicate faces, and
        /// importantly, merging spatially duplicate vertices, as every input triangle is initially independent.
        /// </summary>
        public void SetTriangles(IEnumerable<Triangle> triangles, bool normalize = true, Action<string> warn = null)
        {
            Faces = new List<Face>(triangles.Count());
            Vertices = new List<Vertex>(triangles.Count() * 3);
            int idx = 0;
            foreach (Triangle t in triangles)
            {
                Faces.Add(new Face(idx, idx + 1, idx + 2));
                idx += 3;
                Vertices.Add((Vertex)t.V0.Clone());
                Vertices.Add((Vertex)t.V1.Clone());
                Vertices.Add((Vertex)t.V2.Clone());
            }
            this.Clean(normalize: normalize, removeDuplicateVerts: true, warn: warn);
        }

        /// <summary>
        /// Determines what values in the vertex structure are considered to have valid data
        /// </summary>
        public void SetProperties(bool hasNormals, bool hasUVs, bool hasColors)
        {
            HasNormals = hasNormals;
            HasUVs = hasUVs;
            HasColors = hasColors;
        }

        public void SetProperties(Mesh other)
        {
            SetProperties(other.HasNormals, other.HasUVs, other.HasColors);
        }

        public struct FaceStats
        {
            public double MinArea;
            public double MaxArea;
            public double AvgArea;

            public override string ToString()
            {
                return string.Format("min tri area {0}, max {1}, avg {2}", MinArea, MaxArea, AvgArea);
            }
        }

        public FaceStats CollectFaceStats()
        {
            var stats = new FaceStats();
            stats.MinArea = float.PositiveInfinity;
            stats.MaxArea = float.NegativeInfinity;
            stats.AvgArea = 0;
            foreach (Face face in Faces)
            {
                double area = new IndirectTriangle(this, face).Area();
                stats.MinArea = Math.Min(area, stats.MinArea);
                stats.MaxArea = Math.Max(area, stats.MaxArea);
                stats.AvgArea += area;
            }
            if (Faces.Count > 1)
            {
                stats.AvgArea /= Faces.Count;
            }
            return stats;
        }

        /// <summary>
        /// Returns a list of triangles for this mesh.
        /// Writes to the triangle vertex data will write through to the vertices of this mesh.
        /// </summary>
        public IEnumerable<Triangle> Triangles()
        {
            foreach (Face face in Faces)
            {
                yield return new IndirectTriangle(this, face);
            }
        }

        /// <summary>
        /// Returns a triangl corresponding to a face of this mesh.
        /// Writes to the triangle vertex data will write through to the vertices of this mesh.
        /// </summary>
        public Triangle FaceToTriangle(Face face)
        {
            return new IndirectTriangle(this, face);
        }

        public Triangle FaceToTriangle(int faceIndex)
        {
            return FaceToTriangle(Faces[faceIndex]);
        }

        /// <summary>
        /// Returns an array of the three vertices held by the given face
        /// </summary>
        /// <param name="f"></param>
        /// <returns></returns>
        public Vertex[] FaceToVertexArray(Face f)
        {
            return new Vertex[] { Vertices[f.P0], Vertices[f.P1], Vertices[f.P2] };
        }

        public HashSet<int> VertexIndicesReferencedByFaces()
        {
            // Mark which vertices are referenced by faces
            HashSet<int> referencedIndices = new HashSet<int>();
            for (int i = 0; i < Faces.Count; i++)
            {
                referencedIndices.Add(Faces[i].P0);
                referencedIndices.Add(Faces[i].P1);
                referencedIndices.Add(Faces[i].P2);
            }

            return referencedIndices;
        }

        /// <summary>
        /// Returns a box thats bounds encompass the vertex positions in 3D space
        /// </summary>
        /// <returns></returns>
        public BoundingBox Bounds()
        {
            BoundingBox b = new BoundingBox(Vector3.Largest, Vector3.Smallest);
            if (HasFaces)
            {
                foreach (var idxVert in VertexIndicesReferencedByFaces())
                {
                    var v = Vertices[idxVert];
                    b.Min = Vector3.Min(b.Min, v.Position);
                    b.Max = Vector3.Max(b.Max, v.Position);
                }
            }
            else
            {
                foreach (Vertex v in Vertices)
                {
                    b.Min = Vector3.Min(b.Min, v.Position);
                    b.Max = Vector3.Max(b.Max, v.Position);
                }
            }
            return b;
        }

        /// <summary>
        /// Translate this mesh to be centered on its bounds
        /// </summary>
        public void Center()
        {
            Translate(-Bounds().Center());
        }

        /// <summary>
        /// Attempt to estimate the average distance between vertices in this mesh
        /// </summary>
        /// <param name="samples"></param>
        /// <returns></returns>
        public double AverageDensity(int samples = 0)
        {
            VertexKDTree tree = new VertexKDTree(this);
            return tree.AverageDensity(samples: samples);
        }

        /// <summary>
        /// Returns total mesh surface area by summing area of each triangle
        /// </summary>
        /// <returns></returns>
        public double SurfaceArea()
        {
            return Faces.Sum(f => Triangle.Area(Vertices[f.P0].Position, Vertices[f.P1].Position,
                                                Vertices[f.P2].Position));
        }

        public void Scale(double s)
        {
            foreach(Vertex v in Vertices)
            {
                v.Position *= s;
            }
        }

        /// <summary>
        /// Applies a transformation matrix to each vertex in the mesh
        /// </summary>
        /// <param name="m"></param>
        public void Transform(Matrix mat)
        {
            foreach (Vertex v in Vertices)
            {
                v.Position = Vector3.Transform(v.Position, mat);
                if (HasNormals)
                {
                    v.Normal = Vector3.TransformNormal(v.Normal, mat);
                }
            }
        }

        public Mesh Transformed(Matrix mat)
        {
            Mesh ret = new Mesh(this);
            ret.Transform(mat);
            return ret;
        }

        /// Applies an offset to all vertices in the mesh
        /// </summary>
        /// <param name="offset"></param>
        public void Translate(Vector3 offset)
        {
            for (int i = 0; i < Vertices.Count; i++)
            {
                Vertices[i].Position += offset;
            }
        }

        /// <summary>
        /// Removes specified vertices from this mesh
        /// Also removes any faces that reference a removed vertex 
        /// </summary>
        /// <param name="vertices"></param>
        public void RemoveVertices(IEnumerable<Vertex> vertices)
        {
            Dictionary<int, Vertex> originalIndexToVert = new Dictionary<int, Vertex>();
            Dictionary<int, int> originalToClippedIndex = new Dictionary<int, int>();
            HashSet<Vertex> vertsToRemove = new HashSet<Vertex>(vertices);
            List<Vertex> clippedVerts = new List<Vertex>();
            // Loop through all existing vertices and determine which ones to keep
            // Record original and new indices
            for (int i = 0; i < Vertices.Count; i++)
            {
                Vertex v = Vertices[i];
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
            for (int i = 0; i < Faces.Count; i++)
            {
                Face face = Faces[i];
                // Keep this face only if none of it's vertices have been clipped
                bool keep = face.ToArray().All(j => originalToClippedIndex.ContainsKey(j));
                if (keep)
                {
                    clippedFaces.Add(new Face(face.ToArray().Select(j => originalToClippedIndex[j]).ToArray()));
                }
            }
            Vertices = clippedVerts;
            Faces = clippedFaces;
        }

        /// <summary>
        /// Reverse the winding of faces - i.e. make them face the other direction
        /// </summary>
        public void ReverseWinding()
        {
            for (int i = 0; i < Faces.Count; i++)
            {
                Face f = Faces[i];
                Faces[i] = new Face(f.P0, f.P2, f.P1);
            }
        }

        /// <summary>
        /// Checks to see if this mesh has the same attributes as the other mesh (normal, uv, and texture)
        /// </summary>
        public bool AttributesEqual(Mesh other)
        {
            return HasNormals == other.HasNormals && HasUVs == other.HasUVs && HasColors == other.HasColors;
        }

        /// <summary>
        /// Return true if all attributes that are true of this mesh are also true of other
        /// </summary>
        public bool AttributesSubsetOf(Mesh other, bool checkColors = true)
        {
            if ((HasNormals && !other.HasNormals) || (HasUVs && !other.HasUVs) ||
                (checkColors && HasColors && !other.HasColors))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Copy one or more vertex attributes from another mesh to matching vertices of this mesh.
        /// By default matching includes all non-copied attributes which are present on both meshes.
        /// Where multiple src vertices match a destionation vertex the attributes of the source vertices are averaged.
        /// </summary>
        public void CopyVertexAttributes(Mesh src, bool matchPositions = true, bool matchNormals = true,
                                         bool matchUVs = true, bool matchColors = true, bool copyPositions = false,
                                         bool copyNormals = false, bool copyUVs = false, bool copyColors = false)
        {
            copyNormals &= src.HasNormals;
            copyUVs &= src.HasUVs;
            copyColors &= src.HasColors;

            matchPositions &= !copyPositions;
            matchNormals &= HasNormals && src.HasNormals && !copyNormals;
            matchUVs &= HasUVs && src.HasUVs && !copyUVs;
            matchColors &= HasColors && src.HasColors && !copyColors;

            var comparer = new Vertex.Comparer(matchPositions, matchNormals, matchUVs, matchColors);

            //unique vertex of this mesh -> list of equivalent vertices of src
            var map = new Dictionary<Vertex, List<Vertex>>(comparer);

            foreach (var v in Vertices)
            {
                map[v] = null;
            }

            foreach (var v in src.Vertices)
            {
                if (map.ContainsKey(v))
                {
                    var srcs = map[v];
                    if (srcs == null)
                    {
                        map[v] = srcs = new List<Vertex>();
                    }
                    srcs.Add(v);
                }
            }

            foreach (var dst in Vertices)
            {
                var srcs = map[dst];
                if (srcs != null)
                {
                    if (copyPositions)
                    {
                        dst.Position = srcs.Aggregate(Vector3.Zero, (s, v) => (s + v.Position)) / srcs.Count;
                    }
                    if (copyNormals)
                    {
                        dst.Normal = srcs.Aggregate(Vector3.Zero, (s, v) => (s + v.Normal)) / srcs.Count;
                    }
                    if (copyUVs)
                    {
                        dst.UV = srcs.Aggregate(Vector2.Zero, (s, v) => (s + v.UV)) / srcs.Count;
                    }
                    if (copyColors)
                    {
                        dst.Color = srcs.Aggregate(Vector4.Zero, (s, v) => (s + v.Color)) / srcs.Count;
                    }
                }
            }
        }

        /// <summary>
        /// Compute Hausdorff difference between this mesh and 1 or more other meshes.
        /// If symmetric = true then computes the bidirectional Hausdorff distance.
        /// Otherwise computes the unidirectional Hausdorff distance from this mesh to the merged others.
        /// </summary>
        public double HausdorffDistance(double maxErrorEpsilon, bool symmetric, params Mesh[] others)
        {
            if (!HasFaces)
            {
                throw new ArgumentException("Hausdorff distance requires mesh with faces");
            }

            var srcs = (others ?? new Mesh[0]).Where(m => m != null && m.HasVertices).ToList();

            if (srcs.Count < 1)
            {
                throw new ArgumentException("Hausdorff distance requires at least one other non-empty mesh");
            }

            if (srcs.Count(m => !m.HasFaces) > 0)
            {
                throw new ArgumentException("Hausdorff distance requires all meshes to have faces");
            }

            Mesh merged = srcs.Count > 1 ? MeshMerge.MergeWithCommonAttributes(srcs.ToArray()) : srcs[0];

            //this isn't right
            //just because the two bounds don't intersect
            //doesn't mean the Hausdorff distance is related to the size of either bounds
            //for example consider two parallel planar meshes, so by construction their bounds never intersect
            //but their Hausdorff distance is the distance between them which can be arbitrarily small
            //if (!Bounds().Intersects(merged.Bounds()))
            //{
            //    return merged.Bounds().MaxDimension();
            //}

            return JPLOPS.Geometry.HausdorffDistance.Calculate(this, merged, maxErrorEpsilon, symmetric);
        }

        /// <summary>
        /// Assumes mesh with axis-aligned rectangular convex hull when projected onto the plane defined by upAxis.
        /// Returns the vertex posisitions of the 4 corners.
        /// </summary>
        /// <param name="upAxis">"up" axis of mesh (given as vector3 with single non-zero component) </param>
        /// <returns></returns>
        public List<Vertex> Corners(Vector3 upAxis)
        {
            List<int> axes = new List<int>();
            for (int i = 0; i < 3; i++)
            {
                if (upAxis.ToDoubleArray()[i] == 0)
                {
                    axes.Add(i);
                }
            }
            if (axes.Count != 2)
            {
                throw new MeshException("Axis must have exactly one non-zero component");
            }

            int a1 = axes[0];
            int a2 = axes[1];

            Vertex lowerLeft = Vertices[0];
            Vertex lowerRight = Vertices[0];
            Vertex upperLeft = Vertices[0];
            Vertex upperRight = Vertices[0];
            foreach (Vertex v in Vertices)
            {
                double[] pos = v.Position.ToDoubleArray();
                double[] ll = lowerLeft.Position.ToDoubleArray();
                double[] lr = lowerRight.Position.ToDoubleArray();
                double[] ul = upperLeft.Position.ToDoubleArray();
                double[] ur = upperRight.Position.ToDoubleArray();

                //pos.dot(-1, -1) > ll.dot(-1, -1)
                if (-pos[a1] -pos[a2] > -ll[a1] -ll[a2])
                {
                    lowerLeft = v;
                }

                //pos.dot(1, -1) > lr.dot(1, -1)
                if (pos[a1] -pos[a2] > lr[a1] -lr[a2])
                {
                    lowerRight = v;
                }

                //pos.dot(-1, 1) > ul.dot(-1, 1)
                if (-pos[a1] +pos[a2] > -ul[a1] +ul[a2])
                {
                    upperLeft = v;
                }

                //pos.dot(1, 1) > ur.dot(1, 1)
                if (pos[a1] +pos[a2] > ur[a1] +ur[a2])
                {
                    upperRight = v;
                }
            }
            return new List<Vertex> { lowerLeft, lowerRight, upperLeft, upperRight };
        }

        /// <summary>
        /// Save a mesh to disk with an optional filename
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="textureFilename"></param>
        public void Save(string filename, string textureFilename = null, string indexFilename = null)
        {
            string ext = Path.GetExtension(filename).ToLower();
            var serializers = MeshSerializers.Instance;
            MeshSerializer s = serializers.GetSerializer(ext);
            if (s == null)
            {
                throw new MeshSerializerException(string.Format("mesh format \"{0}\" not supported, " +
                                                                "supported formats: {1}", ext,
                                                                string.Join(", ", serializers.SupportedFormats())));
            }

            bool isB3dm = s.GetType() == typeof(B3DMSerializer);
            bool hasIndex = !String.IsNullOrEmpty(indexFilename);

            if (hasIndex)
            {
                if (isB3dm)
                {
                    B3DMSerializer.Save(this, filename, textureFilename, indexFilename);
                }
                else
                {
                    throw new MeshSerializerException("Index image only supported for b3dm serializer");
                }
            }
            else
            {
                s.Save(this, filename, textureFilename);
            }
        }

        public static Mesh Load(string filename, out string imageFilename, bool onlyGetImageFilename = false)
        {
            string ext = Path.GetExtension(filename).ToLower();
            MeshSerializer s = MeshSerializers.Instance.GetSerializer(ext);
            if (s == null)
            {
                throw new MeshSerializerException("Mesh format not supported");
            }
            return s.Load(filename, out imageFilename, onlyGetImageFilename);
        }

        public static Mesh Load(string filename)
        {
            return Load(filename, out string imageFilename);
        }

        public static List<Mesh> LoadAllLODs(string filename, out string imageFilename,
                                             bool onlyGetImageFilename = false)
        {
            string ext = Path.GetExtension(filename).ToLower();
            MeshSerializer s = MeshSerializers.Instance.GetSerializer(ext);
            if (s == null)
            {
                throw new MeshSerializerException("Mesh format not supported");
            }
            return s.LoadAllLODs(filename, out imageFilename, onlyGetImageFilename);
        }

        public static List<Mesh> LoadAllLODs(string filename)
        {
            return LoadAllLODs(filename, out string imageFilename);
        }
    }
}
