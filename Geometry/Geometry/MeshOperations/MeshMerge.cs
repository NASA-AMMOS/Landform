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
    public static class MeshMerge
    {
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
                                     Action<string> warn = null)
        {
            int numNewVerts = otherMeshes.Aggregate(0, (sum, m) => m == null ? sum : sum + m.Vertices.Count);
            int numNewFaces = otherMeshes.Aggregate(0, (sum, m) => m == null ? sum : sum + m.Faces.Count);
            mesh.Vertices.Capacity = Math.Max(mesh.Vertices.Capacity, mesh.Vertices.Count + numNewVerts);
            mesh.Faces.Capacity = Math.Max(mesh.Faces.Capacity, mesh.Faces.Count + numNewFaces);
            Vector4[] colors = null;
            if (uniqueColors)
            {
                colors = Colorspace.RandomHues(otherMeshes.Length)
                    .Select(c => new Vector4(c[0], c[1], c[2], 1))
                    .ToArray();
                mesh.HasColors = true;
            }
            for (int i = 0; i < otherMeshes.Length; i++)
            {
                Mesh m = otherMeshes[i];
                if (m == null)
                {
                    continue;
                }
                if (!mesh.AttributesSubsetOf(m, checkColors: !uniqueColors))
                {
                    throw new MeshException("mesh to merge missing one or more attributes required by aggregate mesh");
                }
                int vertexBaseCount = mesh.Vertices.Count;
                for (int j = 0; j < m.Vertices.Count; j++)
                {
                    Vertex v = (Vertex)(m.Vertices[j].Clone());
                    if (uniqueColors)
                    {
                        v.Color = colors[i];
                    }
                    mesh.Vertices.Add(v);
                }
                for (int j = 0; j < m.Faces.Count; j++)
                {
                    Face f = new Face(m.Faces[j]);
                    f.P0 += vertexBaseCount;
                    f.P1 += vertexBaseCount;
                    f.P2 += vertexBaseCount;
                    mesh.Faces.Add(f);
                }
            }
            if (clean)
            {
                mesh.Clean(normalize, removeDuplicateVerts, warn: warn);
            }
        }

        public static void MergeWith(this Mesh mesh, params Mesh[] otherMeshes)
        {
            mesh.MergeWith(otherMeshes, true, true, true); //specify params or will be a self-call (infinite recursion)
        }

        public static void MergeWith(this Mesh mesh, Action<string> warn, params Mesh[] otherMeshes)
        {
            //specify params or will be a self-call (infinite recursion)
            mesh.MergeWith(otherMeshes, true, true, true, false, warn);
        }

        /// <summary>
        /// Combines and returns one or more meshes
        /// The proprties of the input meshes must match this one
        /// Vertex objects are cloned to avoid side effects in case the meshes are modifed in the future
        /// </summary>
        public static Mesh Merge(Mesh[] meshesToCombine, bool clean = true, bool normalize = true,
                                 bool removeDuplicateVerts = true, bool uniqueColors = false,
                                 Action<string> warn = null)
        {
            Mesh first = meshesToCombine[0];
            return Merge(first.HasNormals, first.HasUVs, first.HasColors, meshesToCombine,
                         clean, normalize, removeDuplicateVerts, uniqueColors, warn);
        }

        public static Mesh Merge(Action<string> warn, params Mesh[] meshesToCombine)
        {
            return Merge(meshesToCombine, true, true, true, false, warn);
        }

        public static Mesh Merge(params Mesh[] meshesToCombine)
        {
            return Merge(meshesToCombine, true, true, true, false, null);
        }

        /// <summary>
        /// Combines and returns one or more meshes
        /// The combined mesh will have an attribute (normals, uvs, colors)
        /// only if all the input meshes have that attribute
        /// </summary>
        public static Mesh MergeWithCommonAttributes(Mesh[] meshesToCombine, bool clean = true, bool normalize = true,
                                                     bool removeDuplicateVerts = true, bool uniqueColors = false,
                                                     Action<string> warn = null)
        {
            bool normals = meshesToCombine.All(m => m.HasNormals);
            bool uvs = meshesToCombine.All(m => m.HasUVs);
            bool colors = meshesToCombine.All(m => m.HasColors) || uniqueColors;
            return Merge(normals, uvs, colors, meshesToCombine, clean, normalize, removeDuplicateVerts, uniqueColors,
                         warn);
        }

        public static Mesh MergeWithCommonAttributes(Action<string> warn, params Mesh[] meshesToCombine)
        {
            return MergeWithCommonAttributes(meshesToCombine, true, true, true, false, warn);
        }

        public static Mesh MergeWithCommonAttributes(params Mesh[] meshesToCombine)
        {
            return MergeWithCommonAttributes(meshesToCombine, true, true, true, false, null);
        }

        /// <summary>
        /// Combines several meshes and returnes a new mesh with the specified attributes
        /// </summary>
        public static Mesh Merge(bool hasNormals, bool hasUVs, bool hasColors, Mesh[] meshesToCombine,
                                 bool clean = true, bool normalize = true, bool removeDuplicateVerts = true,
                                 bool uniqueColors = false, Action<string> warn = null)
        {
            Mesh result = new Mesh(hasNormals, hasUVs, hasColors);
            result.MergeWith(meshesToCombine, clean, normalize, removeDuplicateVerts, uniqueColors, warn);
            return result;
        }

        public static Mesh Merge(bool hasNormals, bool hasUvs, bool hasColors, params Mesh[] meshesToCombine)
        {
            return Merge(hasNormals, hasUvs, hasColors, meshesToCombine, true, true, true, false, null);
        }
            
        public static Mesh Merge(bool hasNormals, bool hasUvs, bool hasColors, Action<string> warn,
                                 params Mesh[] meshesToCombine)
        {
            return Merge(hasNormals, hasUvs, hasColors, meshesToCombine, true, true, true, false, warn);
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
    }
}
