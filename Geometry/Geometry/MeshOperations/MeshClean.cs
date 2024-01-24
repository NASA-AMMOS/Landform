using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using RTree;
using JPLOPS.MathExtensions;
using JPLOPS.Util;

namespace JPLOPS.Geometry
{
    public static class MeshClean
    {
        public static bool CheckUV(Vector2 uv)
        {
            return 0 <= uv.X && uv.X <= 1 && 0 <= uv.Y && uv.Y <= 1;
        }

        /// <summary>
        /// Returns true if any points are NaN or infinity
        /// </summary>
        /// <returns></returns>
        public static bool ContainsInvalidPoints(this Mesh mesh)
        {
            foreach (var v in mesh.Vertices)
            {
                if (!v.Position.Valid())
                {
                    return true;
                }
            }
            return false;
        }
        
        public static int RemoveInvalidPoints(this Mesh mesh)
        {
            if (mesh.Faces.Count > 0)
            {
                throw new Exception("not supported in meshes that contain faces");
            }
            int nr = 0;
            var newverts = new List<Vertex>(mesh.Vertices.Count);
            foreach (var v in mesh.Vertices)
            {
                if (!v.Position.Valid())
                {
                    nr++;
                    continue;
                }
                newverts.Add(v);
            }
            mesh.Vertices = newverts;
            return nr;
        }

        /// <summary>
        /// Returns true if any normals are very small, NaN, or infinity
        /// </summary>
        /// <returns></returns>
        public static bool ContainsInvalidNormals(this Mesh mesh, double eps = 1e-5)
        {
            if (!mesh.HasNormals)
            {
                return false;
            }
            foreach (var v in mesh.Vertices)
            {
                if (!v.Normal.Valid() || v.Normal.Length() < eps) 
                {
                    return true;
                }
            }
            return false;
        }
        
        public static int RemoveInvalidNormals(this Mesh mesh, double eps = 1e-5)
        {
            if (!mesh.HasNormals)
            {
                return 0;
            }
            if (mesh.Faces.Count > 0)
            {
                throw new Exception("not supported in meshes that contain faces");
            }
            int nr = 0;
            var newverts = new List<Vertex>(mesh.Vertices.Count);
            foreach (var v in mesh.Vertices)
            {
                if (!v.Normal.Valid() || v.Normal.Length() < eps)
                {
                    nr++;
                    continue;
                }
                newverts.Add(v);
            }
            mesh.Vertices = newverts;
            return nr;
        }

        /// <summary>
        /// Checks if the face is logically or geometrically degenerate.
        /// Also checks that the involved UVs, if any, are valid.
        /// </summary>
        public static bool FaceIsValid(this Mesh mesh, Face f)
        {
            if (f.P0 < 0 || f.P0 >= mesh.Vertices.Count ||
                f.P1 < 0 || f.P1 >= mesh.Vertices.Count ||
                f.P2 < 0 || f.P2 >= mesh.Vertices.Count)
            {
                return false;
            }

            // Are any two of the vertices referenced by this face the same index
            if (!f.IsValid())
            {
                return false;
            }
            if (mesh.HasUVs && (!CheckUV(mesh.Vertices[f.P0].UV) || !CheckUV(mesh.Vertices[f.P1].UV) ||
                                !CheckUV(mesh.Vertices[f.P2].UV)))
            {
                return false;
            }
            // Are any of the faces vertices at the same location
            if ((mesh.Vertices[f.P0].Position == mesh.Vertices[f.P1].Position) ||
                (mesh.Vertices[f.P1].Position == mesh.Vertices[f.P2].Position) ||
                (mesh.Vertices[f.P2].Position == mesh.Vertices[f.P0].Position))
            {
                return false;
            }
            // Is the face degenerate? 
            // Note this check includes an epsilon tolerance, so may be false even if no two verts are exactly the same.
            if (!Triangle.ComputeNormal(mesh.Vertices[f.P0].Position, mesh.Vertices[f.P1].Position,
                                        mesh.Vertices[f.P2].Position, out Vector3 n))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Returns true if any face in the mesh has 2 or more of its vertices in the same position (zero area face)
        /// </summary>
        /// <returns></returns>
        public static bool HasInvalidFaces(this Mesh mesh)
        {
            foreach (var f in mesh.Faces)
            {
                if (!mesh.FaceIsValid(f))
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
        public static void RemoveInvalidFaces(this Mesh mesh)
        {
            List<Face> validFaces = new List<Face>();
            foreach (var f in mesh.Faces)
            {
                if (mesh.FaceIsValid(f))
                {
                    validFaces.Add(f);
                }
            }
            mesh.Faces = validFaces;
        }

        /// <summary>
        /// Removes any identical faces
        /// Note that this method only removes faces that are strictly identical
        /// Faces that have the same indicies and in the same order (winding) but with different
        /// offsets will not be removed.  Simillarly, faces that have different vertices but are 
        /// logically identifcal because their vertices have identical properties will not be removed
        /// </summary>
        public static void RemoveIdenticalFaces(this Mesh mesh)
        {
            HashSet<Face> fs = new HashSet<Face>();
            List<Face> uniqueFaces = new List<Face>();
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                if (!fs.Contains(mesh.Faces[i]))
                {
                    uniqueFaces.Add(mesh.Faces[i]);
                    fs.Add(mesh.Faces[i]);
                }
            }
            mesh.Faces = uniqueFaces;
        }

        /// <summary>
        /// Removes logically identical faces.  Two faces are logically identical
        /// if they have the same winding and identical vertices.  Note that we
        /// compare vertex equivalence and not just indices.
        /// </summary>
        public static void RemoveDuplicateFaces(this Mesh mesh)
        {
            // Create a mapping from each vertex to a list of face indices that contain that vertex
            Dictionary<Vertex, HashSet<int>> vertexToFaceIndex = new Dictionary<Vertex, HashSet<int>>();
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                Vertex[] vs = mesh.FaceToVertexArray(mesh.Faces[i]);
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
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                // If there is another face like this one, then all of the other faces vertices must be identical
                // Thus we can just look up the hashset for one of this faces vertices
                HashSet<int> potentiallyIdenticalFaces = vertexToFaceIndex[mesh.Vertices[mesh.Faces[i].P0]];

                // Check to see if there are any faces are identical to this one AND has a smaller index
                //(occurs earlier in the face list)
                // if so we are not the first occurence of this face
                bool isFirstInstance = true;
                foreach (int j in potentiallyIdenticalFaces)
                {
                    if (j < i)
                    {
                        // Check the three possible offsets the vertices could have
                        Vertex[] a = mesh.FaceToVertexArray(mesh.Faces[i]);
                        Vertex[] b = mesh.FaceToVertexArray(mesh.Faces[j]);
                        for (int offset = 0; offset < 3; offset++)
                        {
                            // For each offset, check to see if the vertices are identical between the faces
                            if (a[0].Equals(b[(0 + offset) % 3]) && a[1].Equals(b[(1 + offset) % 3]) &&
                                a[2].Equals(b[(2 + offset) % 3]))
                            {
                                isFirstInstance = false;
                                break;
                            }
                        }
                    }
                }
                if (isFirstInstance)
                {
                    uniqueFaces.Add(mesh.Faces[i]);
                }
            }
            mesh.Faces = uniqueFaces;
        }
        
        /// <summary>
        /// Removes any vertices that are not referenced by a face.
        /// </summary>
        public static void RemoveUnreferencedVertices(this Mesh mesh)
        {
            var referencedIndices = mesh.VertexIndicesReferencedByFaces();
            var referencedVertices = new List<Vertex>();
            var oldToNewIndex = new Dictionary<int, int>();
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                // Is this vertex referenced by a face?
                if (referencedIndices.Contains(i))
                {
                    oldToNewIndex.Add(i, referencedVertices.Count);
                    referencedVertices.Add(mesh.Vertices[i]);
                }
            }
            mesh.Vertices = referencedVertices;
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                Face f = mesh.Faces[i];
                f.P0 = oldToNewIndex[f.P0];
                f.P1 = oldToNewIndex[f.P1];
                f.P2 = oldToNewIndex[f.P2];
                mesh.Faces[i] = f;
            }
        }

        /// <summary>
        /// Merge verticies that are within distance eps and delete any invalid, collapsed, or duplicate faces
        /// </summary>
        public static RTree<int> MergeNearbyVertices(this Mesh mesh, double eps)
        {
            var rTree = new RTree<int>();
            var newVertices = new List<Vertex>(mesh.Vertices.Count);
            var oldToNewIndex = mesh.Faces.Count > 0 ? new Dictionary<int, int>() : null;
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Vertex v = mesh.Vertices[i];
                var nearestIndices = rTree.Intersects(v.Position.ToRectangle(eps));
                if (nearestIndices.Count > 0)
                {
                    if (oldToNewIndex != null)
                    {
                        double minDistSq = double.PositiveInfinity;
                        int closest = -1;
                        foreach (int j in nearestIndices)
                        {
                            double d2 = Vector3.DistanceSquared(mesh.Vertices[j].Position, v.Position);
                            if (d2 < minDistSq)
                            {
                                closest = j;
                                minDistSq = d2;
                            }
                        }
                        oldToNewIndex[i] = oldToNewIndex[closest];
                    }
                }
                else
                {
                    rTree.Add(v.Position.ToRectangle(eps), i);
                    if (oldToNewIndex != null)
                    {
                        oldToNewIndex[i] = newVertices.Count;
                    }
                    newVertices.Add(v);
                }
            }
            newVertices.TrimExcess();
            mesh.Vertices = newVertices;

            if (oldToNewIndex != null)
            {
                for (int i = 0; i < mesh.Faces.Count; i++)
                {
                    Face f = mesh.Faces[i];
                    f.P0 = oldToNewIndex[f.P0];
                    f.P1 = oldToNewIndex[f.P1];
                    f.P2 = oldToNewIndex[f.P2];
                    mesh.Faces[i] = f;
                }
                mesh.RemoveInvalidFaces();
                mesh.RemoveIdenticalFaces();
            }

            return rTree;
        }

        /// <summary>
        /// Remove any vertices that are identical and delete any invalid or duplicate faces
        /// </summary>
        public static void RemoveDuplicateVertices(this Mesh mesh, IEqualityComparer<Vertex> comparer = null)
        {
            Dictionary<Vertex, int> vertexToIndex = new Dictionary<Vertex, int>(mesh.Vertices.Count, comparer);
            Dictionary<int, int> oldToNewIndex = new Dictionary<int, int>(mesh.Vertices.Count);
            List<Vertex> uniqueVertices = new List<Vertex>(mesh.Vertices.Count);
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                Vertex v = mesh.Vertices[i];
                if (!vertexToIndex.ContainsKey(v))
                {
                    vertexToIndex.Add(v, vertexToIndex.Count);
                    uniqueVertices.Add(v);
                }
                oldToNewIndex.Add(i, vertexToIndex[v]);
            }
            mesh.Vertices = uniqueVertices;
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                Face f = mesh.Faces[i];
                f.P0 = oldToNewIndex[f.P0];
                f.P1 = oldToNewIndex[f.P1];
                f.P2 = oldToNewIndex[f.P2];
                mesh.Faces[i] = f;
            }
            mesh.RemoveInvalidFaces();
            mesh.RemoveIdenticalFaces();
        }

        /// <summary>
        /// Removes duplicate vertices and faces.
        /// If mesh has faces this will also remove
        /// * degenerate faces
        /// * faces with invalid UVs
        /// * vertices not referenced by a face
        /// Normalizes normals.
        /// </summary>
        public static void Clean(this Mesh mesh, bool normalize = true, bool removeDuplicateVerts = true,
                                 Action<string> verbose = null, Action<string> warn = null)
        {
            verbose = verbose ?? (msg => {});
            warn = warn ?? (msg => {});
            if (mesh.HasFaces)
            {
                int nf = mesh.Faces.Count;
                mesh.RemoveInvalidFaces();
                if (mesh.Faces.Count < nf)
                {
                    warn($"removed {nf - mesh.Faces.Count} invalid faces");
                }

                int nv = mesh.Vertices.Count;
                mesh.RemoveUnreferencedVertices();
                if (mesh.Vertices.Count < nv)
                {
                    verbose($"removed {nv - mesh.Vertices.Count} unreferenced vertices");
                }

                nf = mesh.Faces.Count;
                mesh.RemoveDuplicateFaces();
                if (mesh.Faces.Count < nf)
                {
                    verbose($"removed {nf - mesh.Faces.Count} duplicate faces");
                }
            }
            if (removeDuplicateVerts)
            {
                int nv = 0;
                mesh.RemoveDuplicateVertices();
                if (mesh.Vertices.Count < nv)
                {
                    verbose($"removed {nv - mesh.Vertices.Count} duplicate vertices");
                }
            }
            if (normalize && mesh.HasNormals)
            {
                mesh.NormalizeNormals();
            }
        }

        //remove disconnected islands
        //less than minIslandRatio times the largest island
        //or just return single largest island if minIslandRatio = 1
        //island size is diameter of bounding box
        //unless useVertexCount = true in which case it's number of vertices
        //returns number of removed islands
        public static int RemoveIslands(this Mesh mesh, double minIslandRatio = 0.1, bool useVertexCount = false)
        {
            var disjointSet = new DisjointSet(mesh.Vertices.Count);
            foreach (Face f in mesh.Faces)
            {
                disjointSet.Union(f.P0, f.P1);
                disjointSet.Union(f.P1, f.P2);
            }

            var islandSizes = new Dictionary<int, double>();
            int maxIsland = -1;
            double maxIslandSize = double.NegativeInfinity;

            if (!useVertexCount)
            {
                var islandSize = new Dictionary<int, BoundingBox>();
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    int p = disjointSet.Find(i);
                    if (islandSize.ContainsKey(p))
                    {
                        var tmp = islandSize[p];
                        islandSize[p] = BoundingBoxExtensions.Extend(ref tmp, mesh.Vertices[i].Position);
                    }
                    else
                    {
                        islandSize[p] = BoundingBoxExtensions.CreateFromPoint(mesh.Vertices[i].Position);
                    }
                }
                foreach (var entry in islandSize)
                {
                    double size = entry.Value.Diameter();
                    islandSizes[entry.Key] = size;
                    if (size > maxIslandSize)
                    {
                        maxIsland = entry.Key;
                        maxIslandSize = size;
                    }
                }
            }
            else
            {
                var islandSize = new Dictionary<int, int>();
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    int p = disjointSet.Find(i);
                    if (islandSize.ContainsKey(p))
                    {
                        islandSize[p] = islandSize[p] + 1;
                    }
                    else
                    {
                        islandSize[p] = 1;
                    }
                }
                foreach (var entry in islandSize)
                {
                    double size = entry.Value;
                    islandSizes[entry.Key] = size;
                    if (size > maxIslandSize)
                    {
                        maxIsland = entry.Key;
                        maxIslandSize = size;
                    }
                }
            }

            if (maxIsland < 0)
            {
                return 0;
            }

            int nr = 0;
            if (minIslandRatio < 1)
            {
                double threshold = minIslandRatio * maxIslandSize;
                mesh.Faces = mesh.Faces.Where(f => islandSizes[disjointSet.Find(f.P0)] >= threshold).ToList();
                nr = islandSizes.Values.Count(d => d < threshold);
            }
            else if (islandSizes.Count > 1)
            {
                mesh.Faces = mesh.Faces.Where(f => disjointSet.Find(f.P0) == maxIsland).ToList();
                nr = islandSizes.Count - 1;
            }

            mesh.RemoveUnreferencedVertices();

            return nr;
        }

        public static void FilterFaces(this Mesh mesh, Func<Face, bool> filter)
        {
            var keepers = new List<Face>(mesh.Faces.Count);
            foreach (var face in mesh.Faces)
            {
                if (filter(face))
                {
                    keepers.Add(face);
                }
            }
            mesh.Faces = keepers;
            mesh.RemoveUnreferencedVertices();
        }
    }
}
