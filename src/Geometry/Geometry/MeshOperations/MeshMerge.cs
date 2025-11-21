using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using RTree;
using JPLOPS.MathExtensions;
using JPLOPS.Imaging;

namespace JPLOPS.Geometry
{
    public static class MeshMerge
    {
        public const float INVALID_ATLAS_VALUE = 0.3f;

        /// <summary>
        /// Combine meshes together without merging duplicate vertices.
        /// The latter meshes must have at least the vertex attributes (normals, UVs, colors) that the first one has.
        /// </summary>
        public static Mesh Join(Mesh[] meshes, bool clone = true)
        {
            var inputs = meshes.Where(m => m != null && m.Vertices.Count > 0).ToList();

            if (inputs.Count == 0)
            {
                return new Mesh();
            }

            for (int i = 1; i < inputs.Count; i++)
            {
                if (!inputs[i].AttributesSubsetOf(inputs[0]))
                {
                    throw new MeshException("mesh to join missing one or more attributes required by aggregate mesh");
                }
            }

            var ret = clone ? new Mesh(inputs[0]) : inputs[0];

            //do it like this in part so that the degenerate case of meshes.Length=1 clone=false does not modify mesh
            ret.Vertices.Capacity = Math.Max(ret.Vertices.Capacity, inputs.Sum(m => m.Vertices.Count));
            ret.Faces.Capacity = Math.Max(ret.Faces.Capacity, inputs.Sum(m => m.Faces.Count));

            int nv = ret.Vertices.Count;
            for (int i = 1; i < inputs.Count; i++)
            {
                if (clone)
                {
                    foreach (var v in inputs[i].Vertices)
                    {
                        ret.Vertices.Add((Vertex)(v.Clone()));
                    }
                }
                else
                {
                    ret.Vertices.AddRange(inputs[i].Vertices);
                }

                foreach (var f in inputs[i].Faces)
                {
                    ret.Faces.Add(new Face(f.P0 + nv, f.P1 + nv, f.P2 + nv));
                }

                nv += inputs[i].Vertices.Count;
            }

            return ret;
        }

        /// <summary>
        /// Combines one or more meshes with this one.
        /// The other meshes must have at least the vertex attributes (normals, UVs, colors) that this one has.
        /// Vertex objects are cloned to avoid side effects in case the meshes are modifed in the future.
        /// </summary>
        public static void MergeWith(this Mesh mesh, Mesh[] otherMeshes, bool clean = true, bool normalize = true,
                                     bool removeDuplicateVerts = true, bool uniqueColors = false,
                                     double mergeNearbyVertices = 0, Action<int> afterEach = null,
                                     Action<string> warn = null)
        {
            otherMeshes = otherMeshes.Where(m => m != null).ToArray();

            foreach (var m in otherMeshes)
            {
                if (!mesh.AttributesSubsetOf(m, checkColors: !uniqueColors))
                {
                    throw new MeshException("mesh to merge missing one or more attributes required by aggregate mesh");
                }
            }

            mesh.Vertices.Capacity = Math.Max(mesh.Vertices.Capacity, mergeNearbyVertices > 0 ?
                                              Math.Max(mesh.Vertices.Count, otherMeshes.Max(m => m.Vertices.Count)) :
                                              (mesh.Vertices.Count + otherMeshes.Sum(m => m.Vertices.Count)));

            mesh.Faces.Capacity = Math.Max(mesh.Faces.Capacity, mesh.Faces.Count + otherMeshes.Sum(m => m.Faces.Count));


            Vector4[] colors = null;
            if (uniqueColors)
            {
                colors = Colorspace.RandomHues(otherMeshes.Length)
                    .Select(c => new Vector4(c[0], c[1], c[2], 1))
                    .ToArray();
                mesh.HasColors = true;
            }

            int k = mesh.Vertices.Count;
            RTree<int> rTree = null;
            Dictionary<int, int> oldToNewIndex = null;
            if (mergeNearbyVertices > 0)
            {
                rTree = mesh.MergeNearbyVertices(mergeNearbyVertices);
                if (otherMeshes.Any(m => m.Faces.Count > 0))
                {
                    oldToNewIndex = new Dictionary<int, int>();
                }
            }

            for (int i = 0; i < otherMeshes.Length; i++)
            {
                Mesh m = otherMeshes[i];

                int vertexBaseCount = k;

                for (int j = 0; j < m.Vertices.Count; j++, k++)
                {
                    Vertex v = m.Vertices[j];
                    bool doAdd = true;
                    if (rTree != null)
                    {
                        var rect = v.Position.ToRectangle(mergeNearbyVertices);
                        var nearestIndices = rTree.Intersects(rect);
                        if (nearestIndices.Count > 0)
                        {
                            if (oldToNewIndex != null)
                            {
                                double minDistSq = double.PositiveInfinity;
                                int closest = -1;
                                foreach (int n in nearestIndices)
                                {
                                    double d2 = Vector3.DistanceSquared(mesh.Vertices[n].Position, v.Position);
                                    if (d2 < minDistSq)
                                    {
                                        closest = n;
                                        minDistSq = d2;
                                    }
                                }
                                oldToNewIndex[k] = oldToNewIndex[closest];
                            }
                            doAdd = false;
                        }
                        else
                        {
                            rTree.Add(rect, k);
                            if (oldToNewIndex != null)
                            {
                                oldToNewIndex[k] = mesh.Vertices.Count;
                            }
                        }
                    }
                    if (doAdd)
                    {
                        v = (Vertex)(v.Clone());
                        if (uniqueColors)
                        {
                            v.Color = colors[i];
                        }
                        mesh.Vertices.Add(v);
                    }
                }
                for (int j = 0; j < m.Faces.Count; j++)
                {
                    Face f = new Face(m.Faces[j]);
                    f.P0 += vertexBaseCount;
                    f.P1 += vertexBaseCount;
                    f.P2 += vertexBaseCount;
                    if (oldToNewIndex != null)
                    {
                        f.P0 = oldToNewIndex[f.P0];
                        f.P1 = oldToNewIndex[f.P1];
                        f.P2 = oldToNewIndex[f.P2];
                    }
                    mesh.Faces.Add(f);
                }
                if (afterEach !=null)
                {
                    afterEach(i);
                }
            }
            mesh.Vertices.TrimExcess();
            if (clean)
            {
                mesh.Clean(normalize, removeDuplicateVerts, warn: warn);
            }
        }

        public static void MergeWith(this Mesh mesh, params Mesh[] otherMeshes)
        {
            //specify params or will be a self-call (infinite recursion)
            mesh.MergeWith(otherMeshes, true, true, true, false, 0, null, null);
        }

        public static void MergeWith(this Mesh mesh, Action<string> warn, params Mesh[] otherMeshes)
        {
            //specify params or will be a self-call (infinite recursion)
            mesh.MergeWith(otherMeshes, true, true, true, false, 0, null, warn);
        }

        /// <summary>
        /// Combines several meshes and returnes a new mesh with the specified attributes
        /// </summary>
        public static Mesh Merge(bool hasNormals, bool hasUVs, bool hasColors, Mesh[] meshesToCombine,
                                 bool clean = true, bool normalize = true, bool removeDuplicateVerts = true,
                                 bool uniqueColors = false, double mergeNearbyVertices = 0,
                                 Action<int> afterEach = null, Action<string> warn = null)
        {
            Mesh result = new Mesh(hasNormals, hasUVs, hasColors);
            result.MergeWith(meshesToCombine, clean, normalize, removeDuplicateVerts, uniqueColors, mergeNearbyVertices,
                             afterEach, warn);
            return result;
        }

        /// <summary>
        /// Combines and returns one or more meshes
        /// The proprties of the input meshes must match this one
        /// Vertex objects are cloned to avoid side effects in case the meshes are modifed in the future
        /// </summary>
        public static Mesh Merge(Mesh[] meshesToCombine, bool clean = true, bool normalize = true,
                                 bool removeDuplicateVerts = true, bool uniqueColors = false,
                                 double mergeNearbyVertices  = 0, Action<int> afterEach = null,
                                 Action<string> warn = null)
        {
            Mesh first = meshesToCombine[0];
            return Merge(first.HasNormals, first.HasUVs, first.HasColors, meshesToCombine,
                         clean, normalize, removeDuplicateVerts, uniqueColors, mergeNearbyVertices, afterEach, warn);
        }

        public static Mesh Merge(Action<string> warn, params Mesh[] meshesToCombine)
        {
            return Merge(meshesToCombine, true, true, true, false, 0, null, warn);
        }

        public static Mesh Merge(params Mesh[] meshesToCombine)
        {
            return Merge(meshesToCombine, true, true, true, false, 0, null, null);
        }

        public static Mesh Merge(bool hasNormals, bool hasUvs, bool hasColors, params Mesh[] meshesToCombine)
        {
            return Merge(hasNormals, hasUvs, hasColors, meshesToCombine, true, true, true, false, 0, null, null);
        }
            
        public static Mesh Merge(bool hasNormals, bool hasUvs, bool hasColors, Action<string> warn,
                                 params Mesh[] meshesToCombine)
        {
            return Merge(hasNormals, hasUvs, hasColors, meshesToCombine, true, true, true, false, 0, null, warn);
        }

        /// <summary>
        /// Combines and returns one or more meshes
        /// The combined mesh will have an attribute (normals, uvs, colors)
        /// only if all the input meshes have that attribute
        /// </summary>
        public static Mesh MergeWithCommonAttributes(Mesh[] meshesToCombine, bool clean = true, bool normalize = true,
                                                     bool removeDuplicateVerts = true, bool uniqueColors = false,
                                                     double mergeNearbyVertices = 0, Action<int> afterEach = null, 
                                                     Action<string> warn = null)
        {
            bool normals = meshesToCombine.All(m => m.HasNormals);
            bool uvs = meshesToCombine.All(m => m.HasUVs);
            bool colors = meshesToCombine.All(m => m.HasColors) || uniqueColors;
            return Merge(normals, uvs, colors, meshesToCombine, clean, normalize, removeDuplicateVerts, uniqueColors,
                         mergeNearbyVertices, afterEach, warn);
        }

        public static Mesh MergeWithCommonAttributes(Action<string> warn, params Mesh[] meshesToCombine)
        {
            return MergeWithCommonAttributes(meshesToCombine, true, true, true, false, 0, null, warn);
        }

        public static Mesh MergeWithCommonAttributes(params Mesh[] meshesToCombine)
        {
            return MergeWithCommonAttributes(meshesToCombine, true, true, true, false, 0, null, null);
        }

        /// <summary>
        /// TODO doc  
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

            var merged = MergeWithCommonAttributes(meshes, clean: false);

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

                    var llc = atlas.PixelToUV(new Vector2(x, y + texture.Height - 1));
                    var urc = atlas.PixelToUV(new Vector2(x + texture.Width - 1, y));
                    var mesh = meshes[i];
                    for (int j = 0; j < mesh.Vertices.Count; j++)
                    {
                        var vert = merged.Vertices[index++];
                        vert.UV.X = llc.X * (1.0f - vert.UV.X) + urc.X * vert.UV.X;
                        vert.UV.Y = llc.Y * (1.0f - vert.UV.Y) + urc.Y * vert.UV.Y;
                    }

                    col++;
                    if (col >= cols)
                    {
                        col = 0;
                        row++;
                    }
                }
            }

            float[] zeroColor = new float[bands];
            float[] invalidColor = new float[bands];
            for (int i = 0; i < bands; i++)
            {
                invalidColor[i] = INVALID_ATLAS_VALUE;
            }
            atlas.ReplaceBandValues(zeroColor, invalidColor);

            return new Tuple<Mesh, Image>(merged, atlas);
        }
    }
}
