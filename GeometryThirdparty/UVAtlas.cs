using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;

namespace OPS.Geometry
{
    /// <summary>
    /// Static methods for getting UV's
    /// </summary>
    public static class UVAtlas
    {

        private static readonly ILog logger = LogManager.GetLogger(typeof(UVAtlas));

        /// <summary>
        /// Returns a new mesh with UV's.
        /// Resulting UV coordinates will be normalized 0 - 1, and centered on pixels for an image with resolution `width` x `height`
        /// UV Atlas will have at most `maxCharts` disconnected components (0 inidicates no limit)
        /// `maxStretch` should be 0-1, 0 being no stretch, 1 being no limit
        /// `gutter` indicates minimum distance between components in pixels
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <param name="maxCharts"></param>
        /// <param name="maxStretch"></param>
        /// <param name="gutter"></param>
        /// <returns></returns>
        public static Mesh Atlas(Mesh mesh, int width = 512, int height = 512, int maxCharts = 0, float maxStretch = 0.1666f, float gutter = 2, bool forceHighestQuality = false, float adjacencyEpsilon = 0)
        {
            // Populate vertex arrays and create output arrays
            int nVerts = mesh.Vertices.Count;
            float[] inX = new float[nVerts];
            float[] inY = new float[nVerts];
            float[] inZ = new float[nVerts];

            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var p = mesh.Vertices[i].Position;
                inX[i] = (float)p.X;
                inY[i] = (float)p.Y;
                inZ[i] = (float)p.Z;
            }
            // Populate indices
            int[] indices = new int[mesh.Faces.Count * 3];
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                var f = mesh.Faces[i];
                indices[i * 3 + 0] = f.P0;
                indices[i * 3 + 1] = f.P1;
                indices[i * 3 + 2] = f.P2;

            }
            float[] outU, outV;
            int[] outVertexRemap;
            UVAtlasNET.UVAtlas.Quality quality = forceHighestQuality ? UVAtlasNET.UVAtlas.Quality.UVATLAS_GEODESIC_QUALITY : UVAtlasNET.UVAtlas.Quality.UVATLAS_DEFAULT;
            UVAtlasNET.UVAtlas.Atlas(inX, inY, inZ, indices, out outU, out outV, out indices, out outVertexRemap, maxCharts, maxStretch, gutter, width, height, quality, adjacencyEpsilon);
            if (indices.Length % 3 != 0)
            {
                throw new Exception("Atlas output indices not divisible by 3");
            }
            Mesh result = new Mesh(hasUVs: true, hasNormals: mesh.HasNormals, hasColors: mesh.HasColors);
            for (int i = 0; i < outVertexRemap.Length; i++)
            {
                var vert = new Vertex(mesh.Vertices[outVertexRemap[i]]);
                vert.UV = new Microsoft.Xna.Framework.Vector2(outU[i], outV[i]);
                result.Vertices.Add(vert);
            }
            for (int i = 0; i < indices.Length; i += 3)
            {
                result.Faces.Add(new Face(indices[i], indices[i + 1], indices[i + 2]));
            }
            result.HasNormals = true;
            return result;
        }
    }
}
