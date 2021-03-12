using System;
using System.Collections.Generic;
using System.Linq;
using OPS.Util;

namespace OPS.Geometry
{
    /// <summary>
    /// Static methods for getting UV's
    /// </summary>
    public static class UVAtlas
    {
        public const int DEF_RESOLUTION = 512;
        public const int DEF_MAX_CHARTS = 0;
        public const double DEF_MAX_STRETCH = 0.1666;
        //public const double DEF_MAX_STRETCH = 1;
        public const double DEF_GUTTER = 2;

        /// <summary>
        /// Resulting UV coordinates will be normalized 0 - 1 and centered on pixels
        /// for an image with resolution `width` x `height`.
        /// UV Atlas will have at most `maxCharts` disconnected components (0 inidicates no limit)
        /// `maxStretch` should be 0-1, 0 being no stretch, 1 being no limit
        /// `gutter` indicates minimum distance between components in pixels
        /// </summary>
        public static bool Atlas(Mesh mesh, int width = DEF_RESOLUTION, int height = DEF_RESOLUTION,
                                 int maxCharts = DEF_MAX_CHARTS, double maxStretch = DEF_MAX_STRETCH,
                                 double gutter = DEF_GUTTER, bool forceHighestQuality = false,
                                 double adjacencyEpsilon = 0, ILogger logger = null, bool fallbackToNaive = true)
        {
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

            int[] indices = new int[mesh.Faces.Count * 3];
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                var f = mesh.Faces[i];
                indices[i * 3 + 0] = f.P0;
                indices[i * 3 + 1] = f.P1;
                indices[i * 3 + 2] = f.P2;
            }

            float[] outU = null, outV = null;
            int[] outVertexRemap = null;
            UVAtlasNET.UVAtlas.Quality quality = forceHighestQuality ? 
                UVAtlasNET.UVAtlas.Quality.UVATLAS_GEODESIC_QUALITY : 
                UVAtlasNET.UVAtlas.Quality.UVATLAS_DEFAULT;

            UVAtlasNET.UVAtlas.ReturnCode rc = UVAtlasNET.UVAtlas.ReturnCode.UNKNOWN;
            try
            {
                rc = UVAtlasNET.UVAtlas.Atlas(inX, inY, inZ, indices,
                                              out outU, out outV, out indices, out outVertexRemap,
                                              maxCharts, (float)maxStretch, (float)gutter, width, height, quality,
                                              (float)adjacencyEpsilon);
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogWarn("UVAtlas error: " + ex.Message);
                }
            }

            if (rc != UVAtlasNET.UVAtlas.ReturnCode.SUCCESS)
            {
                if (fallbackToNaive)
                {
                    if (logger != null)
                    {
                        logger.LogWarn("UVAtlas failed, return code {0}, falling back to naive atlasing", rc);
                    }
                    if (!NaiveAtlas.Compute(mesh, out outU, out outV, out indices, out outVertexRemap))
                    {
                        if (logger != null)
                        {
                            logger.LogWarn("naive atlasing failed");
                        }
                        return false;
                    }
                }
                else
                {
                    if (logger != null)
                    {
                        logger.LogWarn("UVAtlas failed, fallback to naive atlasing disabled");
                    }
                    return false;
                }
            }

            mesh.ApplyAtlas(outU, outV, indices, outVertexRemap);

            mesh.RescaleUVsForTexture(width, height, maxStretch, gutter);

            return true;
        }
    }
}
