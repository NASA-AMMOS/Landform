using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    /// <summary>
    /// Static methods for getting UV's
    /// </summary>
    public static class UVAtlas
    {
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
        public static Mesh Atlas(Mesh mesh, int width, int height, int maxCharts = 0, float maxStretch = 0.25f, float gutter = 4)
        {
            List<Triangle> triangleList = mesh.Triangles();
            int nFaces = triangleList.Count;
            int nVerts = nFaces * 3;

            float[] inX = new float[nVerts];
            float[] inY = new float[nVerts];
            float[] inZ = new float[nVerts];
            float[] outU = new float[nVerts];
            float[] outV = new float[nVerts];
            float[] outX = new float[nVerts];
            float[] outY = new float[nVerts];
            float[] outZ = new float[nVerts];
            uint[] indices = new uint[nVerts];

            uint i = 0;
            foreach (Triangle tri in triangleList)
            {
                inX[i] = (float)tri.V0.Position.X;
                inX[i + 1] = (float)tri.V1.Position.X;
                inX[i + 2] = (float)tri.V2.Position.X;
                inY[i] = (float)tri.V0.Position.Y;
                inY[i + 1] = (float)tri.V1.Position.Y;
                inY[i + 2] = (float)tri.V2.Position.Y;
                inZ[i] = (float)tri.V0.Position.Z;
                inZ[i + 1] = (float)tri.V1.Position.Z;
                inZ[i + 2] = (float)tri.V2.Position.Z;
                i += 3;
            }

            for (i = 0; i < nVerts; i++)
            {
                indices[i] = i;
            }

            UVAtlasNET.UVAtlas.Atlas(outU, outV, inX, inY, inZ, nFaces, indices, maxCharts, maxStretch, gutter, width, height);

            i = 0;
            foreach (Triangle tri in triangleList)
            {
                tri.V0.UV.U = outU[i];
                tri.V0.UV.V = outV[i];
                tri.V1.UV.U = outU[i + 1];
                tri.V1.UV.V = outV[i + 1];
                tri.V2.UV.U = outU[i + 2];
                tri.V2.UV.V = outV[i + 2];
                i += 3;
            }
            return new Mesh(triangleList, hasNormals: mesh.HasNormals, hasColors: mesh.HasColors, hasUVs: true);
        }
    }
}
