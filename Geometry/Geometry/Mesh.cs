using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.Xna.Framework;
using System.Diagnostics;
using OPS.MathExtensions;
using OPS.Util;
using OPS.Imaging;

namespace OPS.Geometry
{
    /// <summary>
    /// X, Y, or Z axis which the skirt is directed along
    /// </summary>
    public enum SkirtMode { X, Y, Z, Normal, None }

    public enum MeshColor { None, Texture, Normals, Elevation, Curvature };
    
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
            this.Faces = new List<Face>(other.Faces.Count);
            for (int i = 0; i < other.Faces.Count; i++)
            {
                this.Faces.Add(other.Faces[i]);
            }
            this.Vertices = new List<Vertex>(other.Vertices.Count);
            for (int i = 0; i < other.Vertices.Count; i++)
            {
                this.Vertices.Add((Vertex)other.Vertices[i].Clone());
            }
            SetProperties(other);
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

        public void SetProperties(Mesh other)
        {
            SetProperties(other.HasNormals, other.HasUVs, other.HasColors);
        }

        /// <summary>
        /// Generates vertex normals for all vertices based on the sum of the connected face normals
        /// </summary>
        public void GenerateVertexNormals()
        {
            // Start with each vertex normal at 0
            foreach (Vertex vertex in Vertices)
            {
                vertex.Normal = Vector3.Zero;
            }

            // Calculate each face's normal and add that normal to each point face's points
            foreach (Face face in Faces)
            {
                // Find the three vertices used in the face
                Vertex v0 = Vertices[face.P0];
                Vertex v1 = Vertices[face.P1];
                Vertex v2 = Vertices[face.P2];

                // Calculate the face's normal
                Vector3 faceNormal = new Triangle(v0, v1, v2).Normal;

                // Add the face's normal to the three vertices
                v0.Normal += faceNormal;
                v1.Normal += faceNormal;
                v2.Normal += faceNormal;
            }

            // Normalize each vertex normal
            NormalizeNormals();

            // The mesh should now be set as having normals
            HasNormals = true;
        }

        /// <summary>
        /// Normalize all normals
        /// </summary>
        public void NormalizeNormals()
        {
            foreach (Vertex vertex in Vertices)
            {
                if (vertex.Normal.Length() > MathHelper.Epsilon)
                {
                    vertex.Normal.Normalize();
                }
            }
        }

        /// <summary>
        /// For each vertex, compute a ray from the vertex to the observation point
        /// If the normal for the vertex points away (more than 90 degrees) from this ray
        /// flip the normal.  This method is useful when points have been captured from a 
        /// sensor and you want to disambiguate normal direction to point toward the sensor
        /// </summary>
        /// <param name="observationPoint"></param>
        public void FlipNormalsTowardPoint(Vector3 observationPoint)
        {
            foreach (var v in this.Vertices)
            {
                Vector3 pointToVert = v.Position - observationPoint;               
                if (Vector3.Dot(v.Normal, pointToVert) > 0)
                {
                    v.Normal *= -1;
                }                
            }
        }

        /// <summary>
        /// Remove vertex normals from this mesh
        /// set all vertex normals to zero and set meshes HasNormals flag to false
        /// </summary>
        public void ClearNormals()
        {
            this.HasNormals = false;
            foreach (var v in this.Vertices)
            {
                v.Normal = Vector3.Zero;
            }
        }

        /// <summary>
        /// Remove uvs from this mesh
        /// set all vertex uvs to zero and set meshes HasUVs flag to false
        /// </summary>
        public void ClearUVs()
        {
            this.HasUVs = false;
            foreach (var v in this.Vertices)
            {
                v.UV = Vector2.Zero;
            }
        }

        /// <summary>
        /// Remove colors from this mesh
        /// set all vertex colors to zero and set meshes HasColors flag to false
        /// </summary>
        public void ClearColors()
        {
            this.HasColors = false;
            foreach (var v in this.Vertices)
            {
                v.Color = Vector4.Zero;
            }
        }


        /// <summary>
        /// Checks if the face contains any vertices located at the same point in space which would render it invalid
        /// </summary>
        /// <param name="f"></param>
        /// <returns></returns>
        bool FaceIsValid(Face f)
        {
            // Are any two of the vertices referenced by this face the same index
            if (!f.IsValid())
            {
                return false;
            }
            // Are any of the faces vertices at the same location
            if ((Vertices[f.P0].Position == Vertices[f.P1].Position) ||
               (Vertices[f.P1].Position == Vertices[f.P2].Position) ||
               (Vertices[f.P2].Position == Vertices[f.P0].Position))
            {
                return false;
            }
            // Is the face zero-length? 
            Vector3 n;
            if (!Triangle.ComputeNormal(Vertices[f.P0].Position, Vertices[f.P1].Position, Vertices[f.P2].Position, out n))
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
            foreach (var f in Faces)
            {
                if (!FaceIsValid(f))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns true if any normals are zero
        /// </summary>
        /// <returns></returns>
        public bool ContainsZeroLengthNormals()
        {
            foreach (var v in Vertices)
            {
                if (v.Normal.Length() < 1e-5)
                {
                    return true;
                }
            }
            return false;
        }
        
        public void RemoveZeroLengthNormals()
        {
            if (this.Faces.Count > 0)
            {
                throw new Exception("Mesh.RemoveZeroLengthNormals is not supported in meshes that contain faces");
            }
            var newverts = new List<Vertex>();
            foreach (var v in Vertices)
            {
                if (v.Normal.Length() < 1e-5)
                {
                    continue;
                }
                newverts.Add(v);
            }
            this.Vertices = newverts;
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
                for (int k = 0; k < vs.Length; k++)
                {
                    if (!vertexToFaceIndex.ContainsKey(vs[k]))
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
                    if (j < i)
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
                if (isFirstInstance)
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
            var referencedIndices = VertexIndicesReferencedByFaces();

            // Remove unused vertices
            List<Vertex> referencedVertices = new List<Vertex>();
            Dictionary<int, int> oldToNewIndex = new Dictionary<int, int>();
            for (int i = 0; i < this.Vertices.Count; i++)
            {
                // Is this vertex referenced by a face?
                if (referencedIndices.Contains(i))
                {
                    oldToNewIndex.Add(i, referencedVertices.Count);
                    referencedVertices.Add(this.Vertices[i]);
                }
            }
            this.Vertices = referencedVertices;
            // Update face indices
            for (int i = 0; i < this.Faces.Count; i++)
            {
                Face f = this.Faces[i];
                f.P0 = oldToNewIndex[f.P0];
                f.P1 = oldToNewIndex[f.P1];
                f.P2 = oldToNewIndex[f.P2];
                this.Faces[i] = f;
            }
        }


        /// <summary>
        /// Merge verticies that are within distance eps and delete any collapsed or duplicate faces
        /// </summary>
        public void MergeNearbyVertices(double eps)
        {
            VertexKDTree kdTree = new VertexKDTree(this.Vertices);
            // Make a list of unique vertices and compute a mapping between old and new indices
            Dictionary<Vertex, int> vertexToIndex = new Dictionary<Vertex, int>();
            Dictionary<int, int> oldToNewIndex = new Dictionary<int, int>();
            List<Vertex> uniqueVertices = new List<Vertex>();
            for (int i = 0; i < this.Vertices.Count; i++)
            {
                Vertex v = this.Vertices[i];
                if (!vertexToIndex.ContainsKey(v))
                {
                    //find nearest neighbor
                    Vertex closest = kdTree.NearestNeighbors(v.Position, 1).First();
                    //if close enough to existing point, merge
                    if (vertexToIndex.ContainsKey(closest) && Vector3.Distance(v.Position, closest.Position) < eps)
                    {
                        vertexToIndex.Add(v, vertexToIndex[closest]);
                    }
                    //else add as new point
                    else
                    {
                        vertexToIndex.Add(v, uniqueVertices.Count);
                        uniqueVertices.Add(v);
                    }
                }
                oldToNewIndex.Add(i, vertexToIndex[v]);
            }
            // Update the vertex list
            this.Vertices = uniqueVertices;
            // Update the face indices
            for (int i = 0; i < this.Faces.Count; i++)
            {
                Face f = this.Faces[i];
                f.P0 = oldToNewIndex[f.P0];
                f.P1 = oldToNewIndex[f.P1];
                f.P2 = oldToNewIndex[f.P2];
                this.Faces[i] = f;
            }
            RemoveInvalidFaces();
            RemoveIdenticalFaces();
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
            for (int i = 0; i < this.Vertices.Count; i++)
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
            for (int i = 0; i < this.Faces.Count; i++)
            {
                Face f = this.Faces[i];
                f.P0 = oldToNewIndex[f.P0];
                f.P1 = oldToNewIndex[f.P1];
                f.P2 = oldToNewIndex[f.P2];
                this.Faces[i] = f;
            }
            RemoveIdenticalFaces();
        }

        Dictionary<Vector3, List<Vertex>> GetPositionToVertexMap()
        {
            Dictionary<Vector3, List<Vertex>> map = new Dictionary<Vector3, List<Vertex>>();
            foreach (Vertex v in this.Vertices)
            {
                if (map.ContainsKey(v.Position))
                {
                    map[v.Position].Add(v);
                }
                else
                {
                    map.Add(v.Position, new List<Vertex> { v });
                }
            }
            return map;
        }

        /// <summary>
        /// Remove the skirt along the specified axis
        /// The skirt's edge vertex must share the normals, UVs, and color of the connected skirt vertex on the mesh
        /// The edge and its connected corresponding one on the mesh must be aligned on the axis specified
        /// </summary>
        /// <param name="axis">The axis which the skirt is extruded along, where the other two axes must be equal between the two skirt vertices</param>
        public void RemoveSkirt(SkirtMode axis)
        {
            if(axis == SkirtMode.Normal)
            {
                throw new Exception("Mesh.RemoveSkirt not implemented for normals...");
            }

            // List of edges in the mesh located on the exterior (edges adjacent to only one triangle)
            // Vertices that are part of the skirt that need to be removed at the end
            List<Vertex> edgeVertices = EdgeVertices();
            List<Vertex> verticesToRemove = new List<Vertex>();
            // Run through each unique vertex on the edge and remove it if it qualifies as part of a skirt
            foreach (Vertex edgeVertex in edgeVertices)
            {
                // Find index of the current edge vertex
                int vertexIndexInMesh = Vertices.IndexOf(edgeVertex);

                // Find the connected faces to the edge vertex
                Face[] facesUsingEdgeVertex = Faces.Where(face => Vertices[face.P0] == edgeVertex || Vertices[face.P1] == edgeVertex || Vertices[face.P2] == edgeVertex).ToArray();

                // All vertices connected to this vertex
                HashSet<Vertex> otherVertices = new HashSet<Vertex>();

                // Add the other two vertices to the set
                foreach (Face face in facesUsingEdgeVertex)
                {
                    int[] vertexIndices = face.ToArray();
                    int vertexIndexInFace = Array.IndexOf(vertexIndices, vertexIndexInMesh);
                    otherVertices.Add(Vertices[vertexIndices[(vertexIndexInFace + 1) % 3]]);
                    otherVertices.Add(Vertices[vertexIndices[(vertexIndexInFace + 2) % 3]]);
                }

                // Go through each vertex and attempt to find one that matches this one's normals/UVs/color/position
                foreach (Vertex candidateVertex in otherVertices)
                {
                    // Skip this point because it doesn't qualify if it has a mismatched normal or UV or color
                    if ((HasNormals && !edgeVertex.Normal.AlmostEqual(candidateVertex.Normal)) ||
                        (HasUVs && !edgeVertex.UV.AlmostEqual(candidateVertex.UV)) ||
                        (HasColors && !edgeVertex.Color.AlmostEqual(candidateVertex.Color))
                    )
                    {
                        continue;
                    }

                    // Check if the other two axes are almost equal between the edge vertex and candidate vertex
                    if ((axis == SkirtMode.X && edgeVertex.Position.Y.AlmostEqual(candidateVertex.Position.Y) && edgeVertex.Position.Z.AlmostEqual(candidateVertex.Position.Z)) ||
                        (axis == SkirtMode.Y && edgeVertex.Position.X.AlmostEqual(candidateVertex.Position.X) && edgeVertex.Position.Z.AlmostEqual(candidateVertex.Position.Z)) ||
                        (axis == SkirtMode.Z && edgeVertex.Position.X.AlmostEqual(candidateVertex.Position.X) && edgeVertex.Position.Y.AlmostEqual(candidateVertex.Position.Y))
                    )
                    {
                        // Include the edge vertex in the list to be deleted
                        verticesToRemove.Add(edgeVertex);
                    }
                }
            }
            // Remove the vertices selected on the edge that are part of the skirt
            RemoveVertices(verticesToRemove);
        }

        /// <summary>
        /// Adds a skirt to all open edges (edges which are connected on only one side) in the direction specified
        /// If SkirtMode.Normal used, then skirt position will be based on average of 2-ring face normals.
        /// Because skirt points can be projected in different directions (and create bad looking skirts),
        /// skirtpoints will be merged if the distance between them divided by the skirt height falls below threshold
        /// </summary>
        /// <param name="axis">Extrudes the skirt in the X, Y, or Z axis</param>
        /// <param name="heightAsPercentOfWidth">Specifies the height of the skirt, where 100% is the width or</param>
        public void AddSkirt(SkirtMode axis, double heightAsPercentOfWidth = 0.02, double threshold = 0.15, bool invert=false)
        {
            // Calculate skirt offset height
            Vector3 size = Bounds().Size();

            if (axis == SkirtMode.Normal)
            {
                double height = heightAsPercentOfWidth * Math.Max(Math.Max(size.X, size.Y), size.Z); //Always within factor ( * or / ) sqrt 2
                height = invert ? height * -1 : height;
                Dictionary<Vertex, Vertex> skirtMap = new Dictionary<Vertex, Vertex>();

                //Store mapping from positions to vertexes
                Dictionary<Vector3, List<Vertex>> posToVert = this.GetPositionToVertexMap();

                //Work only with positions
                Mesh copy = new Mesh(this);
                copy.ClearColors();
                copy.ClearNormals();
                copy.ClearUVs();
                copy.Clean();

                //Create node edge graph to find triangles on perimeter
                EdgeGraph edgeGraph = new EdgeGraph(copy);

                //Compute a skirt location for each perimeter vertex based on the normals of surrounding triangles. If a previous skirt vertex is "good enough" based on `threshold', it may be used instead of creating a new one
                foreach (VertexNode vNode in edgeGraph.VertNodes)
                {
                    if (vNode.IsOnPerimeter)
                    {
                        List<Vertex> candidates = new List<Vertex>();
                        Vector3 averageNormal = new Vector3(0, 0, 0);
                        foreach (OPS.Geometry.Edge e1 in vNode.AdjacentEdges)
                        {
                            if (e1.IsPerimeterEdge)
                            {
                                candidates.Add(e1.Dst.Vert);
                            }
                            foreach (OPS.Geometry.Edge e2 in e1.Dst.AdjacentEdges)
                            {
                                if (e2.Left != null)
                                {
                                    Triangle t = new Triangle(e2.Src.Vert.Position, e2.Dst.Vert.Position, e2.Left.Vert.Position);
                                    averageNormal += t.Normal * t.Area();
                                }
                            }
                        }
                        averageNormal.Normalize();
                        Vector3 offset = averageNormal * -1 * height;

                        Vertex vSkirt = new Vertex(vNode.Vert.Position + offset, vNode.Vert.Normal, vNode.Vert.Color, vNode.Vert.UV);

                        bool shouldAddSkirtVertex = true;

                        foreach (Vertex candidate in skirtMap.Keys)
                        {
                            Vertex skirtCandidate = skirtMap[candidate];
                            if ((vSkirt.Position - vNode.Vert.Position).LengthSquared() > (skirtCandidate.Position - vNode.Vert.Position).LengthSquared() || (skirtCandidate.Position - vSkirt.Position).Length() < threshold * offset.Length())
                            {
                                skirtMap.Add(vNode.Vert, skirtCandidate);
                                shouldAddSkirtVertex = false;
                                break;
                            }
                        }

                        if (shouldAddSkirtVertex)
                        {
                            this.Vertices.Add(vSkirt);
                            posToVert.Add(vSkirt.Position, new List<Vertex> { vSkirt });
                            skirtMap.Add(vNode.Vert, vSkirt);
                        }
                    }

                }

                //Add in the faces for the new skirt vertices
                foreach (VertexNode vNode in edgeGraph.VertNodes)
                {
                    if (vNode.IsOnPerimeter)
                    {
                        foreach (OPS.Geometry.Edge e in vNode.AdjacentEdges)
                        {
                            if (e.IsPerimeterEdge && e.Left != null)
                            {
                                Vertex v1 = skirtMap[e.Src.Vert];
                                Vertex v2 = skirtMap[e.Dst.Vert];

                                int v1Index = Vertices.IndexOf(posToVert[v1.Position][0]);
                                int v2Index = Vertices.IndexOf(posToVert[v2.Position][0]);
                                int srcIndex = Vertices.IndexOf(posToVert[e.Src.Vert.Position][0]);
                                int dstIndex = Vertices.IndexOf(posToVert[e.Dst.Vert.Position][0]);
                                this.Faces.Add(new Face(srcIndex, v1Index, dstIndex));
                                this.Faces.Add(new Face(v1Index, v2Index, dstIndex));
                            }
                        }
                    }
                }
            }
            else
            {

                // Finds the maximum extent of either of the other two axes that the skirt is not being created along
                double maxDimension;
                if (axis == SkirtMode.X)
                {
                    maxDimension = Math.Max(size.Y, size.Z);
                }
                else if (axis == SkirtMode.Y)
                {
                    maxDimension = Math.Max(size.X, size.Z);
                }
                else
                {
                    maxDimension = Math.Max(size.X, size.Y);
                }

                // Determines the actual number of model units to extrude the skirt along
                double actualHeight = maxDimension * -heightAsPercentOfWidth / 100;
                actualHeight = invert ? actualHeight * -1 : actualHeight;

                // Set the offset in the correct axis
                Vector3 offset = Vector3.Zero;
                if (axis == SkirtMode.X)
                {
                    offset = new Vector3(actualHeight, 0, 0);
                }
                else if (axis == SkirtMode.Y)
                {
                    offset = new Vector3(0, actualHeight, 0);
                }
                else if (axis == SkirtMode.Z)
                {
                    offset = new Vector3(0, 0, actualHeight);
                }

                // List of resulting exterior edges that are connected by only one face
                List<Edge> edges = GetExteriorEdges();

                // Pairing between points at the edge and the corresponding skirt point on the mesh
                Dictionary<Vertex, Vertex> edgeToSkirtPoints = new Dictionary<Vertex, Vertex>();

                // Copy each vertex down from the edge of the mesh to the skirt and form two triangles along the edge
                foreach (Edge edge in edges)
                {
                    // Copy edge vertex A to the skirt position
                    if (!edgeToSkirtPoints.ContainsKey(edge.A))
                    {
                        Vertex newVertex = new Vertex(edge.A.Position + offset, edge.A.Normal, edge.A.Color, edge.A.UV);
                        Vertices.Add(newVertex);
                        edgeToSkirtPoints.Add(edge.A, newVertex);
                    }
                    Vertex aSkirt = edgeToSkirtPoints[edge.A];

                    // Get the indexes of the new point and skirt point in the list of mesh vertices
                    int aIndex = Vertices.IndexOf(edge.A);
                    int aSkirtIndex = Vertices.IndexOf(aSkirt);

                    // Copy edge vertex B to the skirt position
                    if (!edgeToSkirtPoints.ContainsKey(edge.B))
                    {
                        Vertex newVertex = new Vertex(edge.B.Position + offset, edge.B.Normal, edge.B.Color, edge.B.UV);
                        Vertices.Add(newVertex);
                        edgeToSkirtPoints.Add(edge.B, newVertex);
                    }
                    Vertex bSkirt = edgeToSkirtPoints[edge.B];

                    // Get the indexes of the new point and skirt point in the list of mesh vertices
                    int bIndex = Vertices.IndexOf(edge.B);
                    int bSkirtIndex = Vertices.IndexOf(bSkirt);

                    // Construct both triangles for the face
                    Faces.Add(new Face(aIndex, aSkirtIndex, bIndex));
                    Faces.Add(new Face(bIndex, aSkirtIndex, bSkirtIndex));
                }
            }
            // Clean the mesh for good measure
            Clean();
        }

        public List<Vertex> EdgeVertices()
        {
            // List of edges in the mesh located on the exterior (edges adjacent to only one triangle)
            List<Edge> edges = GetExteriorEdges();
            // Put each vertex in another hashset from all the edges
            HashSet<Vertex> edgeVertices = new HashSet<Vertex>();
            foreach (Edge edge in edges)
            {
                edgeVertices.Add(edge.A);
                edgeVertices.Add(edge.B);
            }
            return edgeVertices.ToList();
        }

        /// <summary>
        /// Returns a list of edge structs holding the two vertices forming the edges wherever the mesh has only one face using the edge
        /// </summary>
        /// <returns></returns>
        private List<Edge> GetExteriorEdges()
        {
            // Unordered set of edges
            HashSet<Edge> edges = new HashSet<Edge>();

            // Put each edge in the hashset and remove it if it already exists
            foreach (Face face in Faces)
            {
                Edge edge0 = new Edge(Vertices[face.P0], Vertices[face.P1]);
                if (edges.Contains(edge0))
                {
                    edges.Remove(edge0);
                }
                else
                {
                    edges.Add(edge0);
                }

                Edge edge1 = new Edge(Vertices[face.P1], Vertices[face.P2]);
                if (edges.Contains(edge1))
                {
                    edges.Remove(edge1);
                }
                else
                {
                    edges.Add(edge1);
                }

                Edge edge2 = new Edge(Vertices[face.P2], Vertices[face.P0]);
                if (edges.Contains(edge2))
                {
                    edges.Remove(edge2);
                }
                else
                {
                    edges.Add(edge2);
                }
            }

            return edges.ToList();
        }

        /// <summary>
        /// Removes duplicate and degenerate faces
        /// Removes duplicate vertices
        /// If any faces are defined this will also remove any vertices that are not referenced
        /// by a face
        /// </summary>
        public void Clean(bool normalize=true, bool removeDuplicateVerts=true)
        {
            if (removeDuplicateVerts)
            {
                RemoveDuplicateVertices();
            }

            if (HasFaces)
            {
                RemoveInvalidFaces();
                RemoveUnreferencedVertices();
                RemoveDuplicateFaces();
            }
            if (normalize && HasNormals)
            {
                NormalizeNormals();
            }
        }

        public void Scale(double s)
        {
            foreach(Vertex v in this.Vertices)
            {
                v.Position *= s;
            }
        }

        /// <summary>
        /// Applies a transformation matrix to each vertex in the mesh
        /// </summary>
        /// <param name="m"></param>
        public void Transform(Matrix m)
        {
            foreach (Vertex v in this.Vertices)
            {
                v.Position = Vector3.Transform(v.Position, m);
                if (this.HasNormals)
                {
                    v.Normal = Vector3.TransformNormal(v.Normal, m);
                }
            }
        }

        /// <summary>
        /// Return a copy of this mesh with a transformation applied.
        /// </summary>
        public static Mesh Transformed(Mesh mesh, Matrix mat)
        {
            Mesh res = new Mesh(mesh);
            res.Transform(mat);
            return res;
        }

        /// <summary>
        /// Applies an offset to all vertices in the mesh
        /// </summary>
        /// <param name="offset"></param>
        public void Translate(Vector3 offset)
        {
            for (int i = 0; i < this.Vertices.Count; i++)
            {
                this.Vertices[i].Position += offset;
            }
        }

        /// <summary>
        /// Returns an array of the three vertices held by the given face
        /// </summary>
        /// <param name="f"></param>
        /// <returns></returns>
        public Vertex[] FaceToVertexArray(Face f)
        {
            return new Vertex[] { this.Vertices[f.P0], this.Vertices[f.P1], this.Vertices[f.P2] };
        }

        public Triangle FaceToTriangle(Face f)
        {
            return new Triangle(this.Vertices[f.P0], this.Vertices[f.P1], this.Vertices[f.P2]);
        }

        /// <summary>
        /// Returns a list of triangles for this mesh. Triangles each contain thier own
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
        /// Returns total mesh surface area by summing area of each triangle
        /// </summary>
        /// <returns></returns>
        public double SurfaceArea()
        {
            double area = 0;
            this.Triangles().ForEach(tri =>
            {
                area += tri.Area();
            });
            return area;
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
        public bool AttributesSubsetOf(Mesh other)
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
        public void MergeWith(Mesh[] otherMeshes, bool clean = true, bool normalize = true, bool removeDuplicateVerts = true)
        {
            int numNewVerts = otherMeshes.Aggregate(0, (sum, mesh) => mesh == null ? sum : sum + mesh.Vertices.Count);
            int numNewFaces = otherMeshes.Aggregate(0, (sum, mesh) => mesh == null ? sum : sum + mesh.Faces.Count);
            Vertices.Capacity = Math.Min(Vertices.Capacity, Vertices.Count + numNewVerts);
            Faces.Capacity = Math.Min(Faces.Capacity, Faces.Count + numNewFaces);
            for (int i = 0; i < otherMeshes.Length; i++)
            {
                Mesh m = otherMeshes[i];
                if (m == null)
                    continue;

                if (!AttributesSubsetOf(m))
                {
                    throw new MeshException("Mesh to merge missing one or more attributes required by aggregate mesh");
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

            if (clean)
            {
                Clean(normalize, removeDuplicateVerts);
            }
        }

        public void MergeWith(params Mesh[] otherMeshes)
        {
            MergeWith(otherMeshes, true, true); //specify params or this will be a self-call (infinite recursion)
        }

        /// <summary>
        /// Combines and returns one or more meshes
        /// The proprties of the input meshes must match this one
        /// Vertex objects are cloned to avoid side effects in case the meshes are modifed in the future
        /// </summary>
        /// <param name="meshesToCombine"></param>
        /// <returns></returns>
        public static Mesh Merge(Mesh[] meshesToCombine, bool clean = true, bool normalize = true)
        {
            Mesh first = meshesToCombine[0];
            return Merge(first.HasNormals, first.HasUVs, first.HasColors, meshesToCombine, clean, normalize);
        }

        public static Mesh Merge(params Mesh[] meshesToCombine)
        {
            return Merge(meshesToCombine, true, true); //specify params or this will be a self-call (infinite recursion)
        }

        /// <summary>
        /// Combines and returns one or more meshes
        /// The combined mesh will have an attribute (normals, uvs, colors) only if all the input meshes have that attribute
        /// </summary>
        /// <param name="meshesToCombine"></param>
        /// <returns></returns>
        public static Mesh MergeWithCommonAttributes(Mesh[] meshesToCombine, bool clean = true, bool normalize = true)
        {
            bool normals = meshesToCombine.All(m => m.HasNormals);
            bool uvs = meshesToCombine.All(m => m.HasUVs);
            bool colors = meshesToCombine.All(m => m.HasColors);
            return Merge(normals, uvs, colors, meshesToCombine, clean, normalize);
        }

        public static Mesh MergeWithCommonAttributes(params Mesh[] meshesToCombine)
        {
            return MergeWithCommonAttributes(meshesToCombine, true, true); //all params or this will be infinite recursion
        }

        /// <summary>
        /// Combines several meshes and returnes a new mesh with the specified attributes
        /// </summary>
        /// <param name="hasNormals"></param>
        /// <param name="hasUvs"></param>
        /// <param name="hasColors"></param>
        /// <param name="meshesToCombine"></param>
        /// <returns></returns>
        public static Mesh Merge(bool hasNormals, bool hasUvs, bool hasColors, Mesh[] meshesToCombine,
                                 bool clean = true, bool normalize = true)
        {
            Mesh result = new Mesh(hasNormals, hasUvs, hasColors);
            result.MergeWith(meshesToCombine, clean, normalize);
            return result;
        }

        public static Mesh Merge(bool hasNormals, bool hasUvs, bool hasColors, params Mesh[] meshesToCombine)
        {
            return Merge(hasNormals, hasUvs, hasColors, meshesToCombine, true, true); //all params or infinite recursion
        }

        /// <summary>
        /// Clips the mesh to fit within the given bounding box
        /// </summary>
        /// <param name="m"></param>
        /// <param name="box"></param>
        /// <returns></returns>
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
                    if (box.Contains(v.Position) != ContainmentType.Disjoint)
                    {
                        result.Vertices.Add(v);
                    }
                }
            }
            if (!box.FuzzyContains(result.Bounds(), 1E-5))
            {
                throw new Exception("Clipped mesh exceeds bounding box");
            }
            return result;
        }

        /// <summary>
        /// TODO doc  
        /// https://github.jpl.nasa.gov/OnSight/Landform/issues/577 
        /// </summary>
        public static Tuple<Mesh, Image> MergeMeshesAndTextures(IEnumerable<Tuple<Mesh, Image>> inputs)
        {
            var meshes = inputs
                .Where(pair => pair.Item1 != null)
                .Select(pair => pair.Item1)
                .ToArray();

            var textures = inputs
                .Where(pair => pair.Item1 != null && pair.Item1.HasUVs)
                .Where(pair => pair.Item2 != null)
                .Select(pair => pair.Item2)
                .ToArray();

            int bands = 0;
            if (textures.Length > 0)
            {
                if (textures.Length != meshes.Length)
                {
                    throw new ArgumentException("cannot merged textured meshes with untextured");
                }
                bands = textures.Select(t => t.Bands).Max();
                foreach (var texture in textures)
                {
                    if (texture.Bands < bands && texture.Bands != 1)
                    {
                        throw new ArgumentException(string.Format("cannot merge {0} band texture and {1} band textures",
                                                                  texture.Bands, bands));
                    }
                }
            }

            var merged = Merge(meshes, clean: false);

            Image atlas = null;
            if (textures.Length > 0)
            {
                int maxWidth = textures.Select(t => t.Width).Max();
                int maxHeight = textures.Select(t => t.Height).Max();

                int cols = (int)Math.Sqrt(textures.Length);
                int rows = (int)Math.Ceiling((double)(textures.Length) / cols);

                var uvScale = new Vector2(1.0 / cols, 1.0 / rows);

                atlas = new Image(bands, cols * maxWidth, rows * maxHeight);

                int row = 0, col = 0, index = 0;
                for (int i = 0; i < textures.Length; i++)
                {
                    int x = col * maxWidth, y = row * maxHeight;

                    Image texture = textures[i];
                    if (texture.Bands < bands)
                    {
                        float[] intensity = texture.GetBandData(0);
                        texture = new Image(texture);
                        while (texture.Bands < bands)
                        {
                            Array.Copy(intensity, texture.GetBandData(texture.AddBand()), intensity.Length);
                        }
                    }

                    atlas.Blit(texture, x, y);

                    var offset = atlas.PixelToUV(new Vector2(x, y + maxHeight - 1));
                    var mesh = meshes[i];
                    for (int j = 0; j < mesh.Vertices.Count; j++)
                    {
                        var vert = merged.Vertices[index++];
                        vert.UV.X *= uvScale.X;
                        vert.UV.Y *= uvScale.Y;
                        vert.UV += offset;
                    }

                    col++;
                    if (col >= cols)
                    {
                        col = 0;
                        row++;
                    }
                }
            }

            return new Tuple<Mesh, Image>(merged, atlas);
        }

        /// <summary>
        /// Clips a mesh to remove everything within the given bounding box
        /// </summary>
        /// <param name="m"></param>
        /// <param name="box"></param>
        /// <returns></returns>
        public static Mesh Cut(Mesh m, BoundingBox box)
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
                    resTriangles.AddRange(t.Cut(box));
                }
                result = new Mesh(resTriangles, m.HasNormals, m.HasUVs, m.HasColors);
            }
            else
            {
                result = new Mesh(m.HasNormals, m.HasUVs, m.HasColors);
                // this is a point cloud
                foreach (var v in m.Vertices)
                {
                    if (box.Contains(v.Position) == ContainmentType.Disjoint)
                    {
                        result.Vertices.Add(v);
                    }
                }
            }
            return result;
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
            for (int i = 0; i < this.Vertices.Count; i++)
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
            for (int i = 0; i < this.Faces.Count; i++)
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
            for (int i = 0; i < this.Faces.Count; i++)
            {
                Face f = this.Faces[i];
                this.Faces[i] = new Face(f.P0, f.P2, f.P1);
            }
        }

        private HashSet<int> VertexIndicesReferencedByFaces()
        {
            // Mark which vertices are referenced by faces
            HashSet<int> referencedIndices = new HashSet<int>();
            for (int i = 0; i < this.Faces.Count; i++)
            {
                referencedIndices.Add(this.Faces[i].P0);
                referencedIndices.Add(this.Faces[i].P1);
                referencedIndices.Add(this.Faces[i].P2);
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
                var referencedIndices = VertexIndicesReferencedByFaces();
                for (int idxVert = 0; idxVert < this.Vertices.Count(); idxVert++)
                {
                    if (referencedIndices.Contains(idxVert))
                    {
                        var v = this.Vertices[idxVert];
                        b.Min = Vector3.Min(b.Min, v.Position);
                        b.Max = Vector3.Max(b.Max, v.Position);
                    }
                }
            }
            else
            {
                foreach (Vertex v in this.Vertices)
                {
                    b.Min = Vector3.Min(b.Min, v.Position);
                    b.Max = Vector3.Max(b.Max, v.Position);
                }
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
        /// compute total texture area in pixels covered by this mesh
        /// </summary>
        public double ComputePixelArea(Image image)
        {
            if (image == null || !HasUVs)
            {
                return 0;
            }
            double area = 0;
            foreach (var t in Triangles())
            {
                Vector3 a = new Vector3(image.UVToPixel(t.V0.UV), 0);
                Vector3 b = new Vector3(image.UVToPixel(t.V1.UV), 0);
                Vector3 c = new Vector3(image.UVToPixel(t.V2.UV), 0);
                double triArea = (new Triangle(a, b, c)).Area();
                if (double.IsNaN(triArea))
                {
                    throw new Exception("Triangle area not a number");
                }
                area += triArea;
            }
            return area;
        }

        /// <summary>
        /// compute total texture space area covered by this mesh
        /// </summary>
        public double ComputeUVArea()
        {
            if (!HasUVs)
            {
                return 0;
            }
            double area = 0;
            foreach (var t in Triangles())
            {
                Vector3 a = new Vector3(t.V0.UV, 0);
                Vector3 b = new Vector3(t.V1.UV, 0);
                Vector3 c = new Vector3(t.V2.UV, 0);
                double triArea = (new Triangle(a, b, c)).Area();
                if (double.IsNaN(triArea))
                {
                    throw new Exception("Triangle area not a number");
                }
                area += triArea;
            }
            return area;
        }

        /// <summary>
        /// Compute Hausdorff difference between this mesh and 1 or more other meshes
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public double HausdorffDistance(double maxErrorEpsilon, params Mesh[] other)
        {
            Mesh merged = Mesh.Merge(this.HasNormals, this.HasUVs, this.HasColors, other);
            if (!this.Bounds().Intersects(merged.Bounds()))
            {
                return merged.Bounds().MaxDimension();
            }
            return OPS.Geometry.HausdorffDistance.Calculate(this, merged, maxErrorEpsilon);
        }

        /// <summary>
        ///  Compute Hausdorff difference between this mesh and 1 or more other meshes using default maxErrorEpsilon
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public double HausdorffDistance(params Mesh[] other)
        {
            return HausdorffDistance(0.001, other);
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
        /// Attempt to estimate the average distance between vertices in this mesh
        /// </summary>
        /// <param name="samples"></param>
        /// <returns></returns>
        public double AverageDensity(int samples = 0)
        {
            VertexKDTree tree = new VertexKDTree(this.Vertices);
            return tree.AverageDensity(samples: samples);
        }

        /// <summary>
        /// Translate this mesh to be centered on its bounds
        /// </summary>
        public void Center()
        {
            this.Translate(-this.Bounds().Center());
        }

        /// <summary>
        /// Save a mesh to disk with an optional filename
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="textureFilename"></param>
        public void Save(string filename, string textureFilename = null)
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
            s.Save(this, filename, textureFilename);
        }

        /// <summary>
        /// like Image.ApplyStdDevStretch() but operates on the vertex colors of this mesh  
        /// if greyscale=true then the colors are interpreted as greyscale (R = G = B)
        /// </summary>
        public void ApplyStdDevStretchToColors(bool greyscale = false, double nStddev = 3)
        {
            void applyToChannel(Func<Vertex, double> getter, Action<Vertex, double> setter)
            {
                int n = 0;
                double min = double.PositiveInfinity;
                double max = double.NegativeInfinity;
                double mean = 0;
                foreach (var v in Vertices)
                {
                    var val = getter(v);
                    mean += val;
                    min = Math.Min(min, val);
                    max = Math.Max(max, val);
                    n++;
                }
                mean /= n;

                double variance = 0;
                foreach (var v in Vertices)
                {
                    var d = getter(v) - mean;
                    variance += d * d;
                }
                variance /= n;
                double stddev = Math.Sqrt(variance);

                double lower = Math.Max(mean - stddev * nStddev, min);
                double upper = Math.Min(mean + stddev * nStddev, max);

                if (min != max)
                {
                    foreach (var v in Vertices)
                    {
                        setter(v, MathE.Clamp01((getter(v) - lower) / (upper - lower)));
                    }
                }
            }

            if (greyscale)
            {
                applyToChannel(v => v.Color.X, (v, g) => { v.Color.X = v.Color.Y = v.Color.Z = g; });
            }
            else
            {
                applyToChannel(v => v.Color.X, (v, r) => { v.Color.X = r; });
                applyToChannel(v => v.Color.Y, (v, g) => { v.Color.Y = g; });
                applyToChannel(v => v.Color.Z, (v, b) => { v.Color.Z = b; });
            }
        }

        /// <summary>
        /// set vertex color components as absolute values of normal components
        /// if tiltMode is set then a greyscale color is set instead, see OrganizedPointCloud.NormalToTilt()
        /// up defaults to (0, 0, -1)
        /// </summary>
        public void ColorByNormals(out double minTilt, out double maxTilt, TiltMode? tiltMode = null,
                                   Vector3? up = null) 
        {
            if (!HasNormals)
            {
                throw new ArgumentException("cannot color mesh without normals by normals");
            }

            if (up == null)
            {
                up = new Vector3(0, 0, -1);
            }

            minTilt = double.PositiveInfinity;
            maxTilt = double.NegativeInfinity;
            foreach (var v in Vertices)
            {
                var n = v.Normal;
                if (!tiltMode.HasValue)
                {
                    v.Color.X = Math.Abs(n.X);
                    v.Color.Y = Math.Abs(n.Y);
                    v.Color.Z = Math.Abs(n.Z);
                }
                else
                {
                    var tilt = OrganizedPointCloud.NormalToTilt(n, tiltMode.Value, up.Value);
                    minTilt = Math.Min(minTilt, tilt);
                    maxTilt = Math.Max(maxTilt, tilt);
                    v.Color.X = v.Color.Y = v.Color.Z = tilt;
                }
            }
            HasColors = true;
        }

        public void ColorByNormals(TiltMode? tiltMode = null, Vector3? up = null) 
        {
            ColorByNormals(out double minTilt, out double maxTilt, tiltMode, up);
        }

        /// <summary>
        /// compute elevation at each vertex and set it as greyscale vertex color
        /// </summary>
        public void ColorByElevation(out double min, out double max, bool absolute = false, Vector3? up = null) 
        {
            if (up == null)
            {
                up = new Vector3(0, 0, 1);
            }

            var ctr = absolute ? new Vector3(0, 0, 0) : Bounds().Center();

            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            foreach (var v in Vertices)
            {
                var elev = (v.Position - ctr).Dot(up.Value);
                v.Color.X = v.Color.Y = v.Color.Z = elev;
                min = Math.Min(min, elev);
                max = Math.Max(max, elev);
            }
            
            HasColors = true;
        }

        public void ColorByElevation(bool absolute = false, Vector3? up = null) 
        {
            ColorByElevation(out double min, out double max, absolute, up);
        }

        /// <summary>
        /// https://computergraphics.stackexchange.com/a/1719
        /// </summary>
        public static double Curvature(Vector3 p1, Vector3 p2, Vector3 n1, Vector3 n2)
        {
            var d = p2 - p1;
            return (n2 - n1).Dot(d) / d.LengthSquared();
        }

        /// <summary>
        /// compute approximate max abs curvature at each vertex and set it as greyscale vertex color
        /// the mesh must have normals
        /// </summary>
        public void ColorByCurvature(out double min, out double max)
        {
            if (!HasNormals)
            {
                throw new ArgumentException("cannot color mesh without normals by curvature");
            }

            var graph = new EdgeGraph(this);

            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            foreach (var v in graph.VertNodes)
            {
                double maxAbsCurvature = 0;
                foreach (var e in v.AdjacentEdges)
                {
                    var c = Math.Abs(Curvature(v.Vert.Position, e.Dst.Vert.Position, v.Vert.Normal, e.Dst.Vert.Normal));
                    maxAbsCurvature = Math.Max(maxAbsCurvature, c);
                }
                v.Vert.Color.X = v.Vert.Color.Y = v.Vert.Color.Z = maxAbsCurvature;
                min = Math.Min(min, maxAbsCurvature);
                max = Math.Max(max, maxAbsCurvature);
            }
            HasColors = true;
        }

        public void ColorByCurvature()
        {
            ColorByCurvature(out double min, out double max);
        }

        /// <summary>
        /// set the colors of this mesh according to the specified mode 
        /// does nothing if mode=Texture or mode=None
        /// otherwise if allowAdjustColors=false then result is same as calling ColorBy{Normals,Curvature,Elevation}()
        /// but if allowAdjustColors=true then the resulting colors are optimized
        /// if stretch=true then ApplyStdDevStretchToColors() is called
        /// otherwise if if the resulting colors are greyscale they are normalized to [0,1]
        /// the colors will be greyscale for Curvature and Elevation modes and Normals modes where tiltMode is not None
        /// </summary>
        public void ColorBy(MeshColor mode, TiltMode tiltMode = TiltMode.None, bool allowAdjustColors = true,
                            bool stretch = false, double nStddev = 3)
        {
            bool greyscale = false;
            bool adjustColors = false;
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;

            switch (mode)
            {
                case MeshColor.None: break;
                case MeshColor.Texture: break;
                case MeshColor.Normals:
                {
                    ColorByNormals(out min, out max, tiltMode);
                    adjustColors = greyscale = tiltMode != TiltMode.None;
                    break;
                }
                case MeshColor.Curvature:
                {
                    ColorByCurvature(out min, out max);
                    adjustColors = greyscale = true;
                    break;
                }
                case MeshColor.Elevation:
                {
                    ColorByElevation(out min, out max);
                    adjustColors = greyscale = true;
                    break;
                }
            }

            if (adjustColors && allowAdjustColors)
            {
                if (stretch)
                {
                    ApplyStdDevStretchToColors(greyscale, nStddev);
                }
                else if (greyscale)
                {
                    foreach (var v in Vertices)
                    {
                        v.Color.X = v.Color.Y = v.Color.Z = (v.Color.X - min) / (max - min);
                    }
                }
            }
        }

        /// <summary>
        /// add texture coordinates to a mesh by projecting vertices onto an image
        /// also optionally removes any vertices of the mesh that aren't visible in the image
        /// </summary>
        public void ProjectTexture(Image img, bool removeVertsOutsideView = true, bool processVertsInParallel = false,
                                   Matrix? meshToImage = null)
        {
            Matrix xform = meshToImage ?? Matrix.Identity;
            ConcurrentBag<Vertex> verticesToRemove = new ConcurrentBag<Vertex>();
            Action<Vertex> generateUV = v =>
            {
                double range;
                Vector2 pixel = img.CameraModel.Project(Vector3.Transform(v.Position, xform), out range);
                if (range < 0 || pixel.X < 0 || pixel.X > (img.Width - 1) || pixel.Y < 0 || pixel.Y > (img.Height - 1))
                {
                    verticesToRemove.Add(v);
                }
                else
                {
                    // TODO: review this half pixel offset
                    //v.UV =  new Vector2((pixel.X - 0.5) / (image.Width+1), 1 - ((pixel.Y - 0.5) / (image.Height+1)));
                    //https://github.jpl.nasa.gov/OnSight/Landform/issues/488
                    v.UV = img.PixelToUV(pixel);
                    v.UV = Vector2.Clamp(v.UV, Vector2.Zero, Vector2.One);
                }
            };
            if (processVertsInParallel)
            {
                CoreLimitedParallel.ForEach(Vertices, generateUV);
            }
            else
            {
                Vertices.ForEach(generateUV);
            }

            HasUVs = true;

            if (removeVertsOutsideView)
            {
                RemoveVertices(verticesToRemove);
            }
        }

        /// <summary>
        /// Read a mesh to disk
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static Mesh Load(string filename)
        {
            string ext = Path.GetExtension(filename).ToLower();
            MeshSerializer s = MeshSerializers.Instance.GetSerializer(ext);
            if (s == null)
            {
                throw new MeshSerializerException("Mesh format not supported");
            }
            return s.Load(filename);
        }

        public static List<Mesh> LoadAllLODs(string filename)
        {
            string ext = Path.GetExtension(filename).ToLower();
            MeshSerializer s = MeshSerializers.Instance.GetSerializer(ext);
            if (s == null)
            {
                throw new MeshSerializerException("Mesh format not supported");
            }
            return s.LoadAllLODs(filename);
        }

        /// <summary>
        /// An edge that holds two vertices
        /// </summary>
        private struct Edge
        {
            public Vertex A;
            public Vertex B;

            public Edge(Vertex a, Vertex b)
            {
                A = a;
                B = b;
            }

            public override int GetHashCode()
            {
                // Warning: Unlike the Equals() method below, this is exact, not AlmostEqual
                return A.GetHashCode() + B.GetHashCode();
            }

            public override bool Equals(Object other)
            {
                if (other.GetType() != typeof(Edge)) return false;

                Edge e = (Edge)other;
                return (A.Position.AlmostEqual(e.A.Position) && B.Position.AlmostEqual(e.B.Position))
                    || (B.Position.AlmostEqual(e.A.Position) && A.Position.AlmostEqual(e.B.Position));
            }
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
                double area = new Triangle(Vertices[face.P0], Vertices[face.P1], Vertices[face.P2]).Area();
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

        public void RemoveFloaters(int minIslandVertexCount = 300)
        {
            DisjointSet disjointSet = new DisjointSet(Vertices.Count);
            foreach (Face f in Faces)
            {
                disjointSet.Union(f.P0, f.P1);
                disjointSet.Union(f.P1, f.P2);
            }

            Dictionary<int, int> islandSizes = new Dictionary<int, int>();
            for (int i = 0; i < Vertices.Count; i++)
            {
                int p = disjointSet.FindPath(i);
                if (islandSizes.ContainsKey(p))
                {
                    islandSizes[p]++;
                }
                else
                {
                    islandSizes[p] = 1;
                }
            }

            Faces = Faces.Where(f =>
                islandSizes[disjointSet.FindPath(f.P0)] >= minIslandVertexCount
            ).ToList();

            RemoveUnreferencedVertices();
        }
    }
}
