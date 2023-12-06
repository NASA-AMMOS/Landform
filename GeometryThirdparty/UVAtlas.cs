using System;
using System.Threading;
using JPLOPS.Util;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Static methods for getting UV's
    /// </summary>
    public static class UVAtlas
    {
        public const int DEF_RESOLUTION = 512;
        public const int DEF_MAX_CHARTS = 0;
        //public const double DEF_MAX_STRETCH = 0.1666;
        public const double DEF_MAX_STRETCH = 0.5;
        //public const double DEF_MAX_STRETCH = 1;
        public const double DEF_GUTTER = 2;

        public const int DEF_MAX_SEC = 5 * 60;

        private class ThreadState
        {
            public volatile UVAtlasNET.UVAtlas.ReturnCode rc = UVAtlasNET.UVAtlas.ReturnCode.UNKNOWN;
            public volatile bool done = false;
            public volatile Exception error = null;
        }

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
                                 double adjacencyEpsilon = 0, ILogger logger = null, bool fallbackToNaive = true,
                                 int maxSec = DEF_MAX_SEC)
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

            //ThreadState fields are volatile to ensure safe publication (memory fencing) from the worker thread to us
            //need to put them in the ThreadState class because local variables can't be volatile in c#
            var ts = new ThreadState();
            try
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        ts.rc = UVAtlasNET.UVAtlas.Atlas(inX, inY, inZ, indices,
                                                         out outU, out outV, out indices, out outVertexRemap,
                                                         maxCharts, (float)maxStretch, (float)gutter, width, height,
                                                         quality, (float)adjacencyEpsilon);
                        ts.done = true;
                    }
                    catch (Exception ex)
                    {
                        ts.error = ex;
                    }
                });
                thread.IsBackground = true; //don't make the process hang around just for this thread
                double startTime = UTCTime.Now();
                thread.Start();
                while (!ts.done)
                {
                    Thread.Sleep(100);
                    double durationSec = UTCTime.Now() - startTime;
                    if (durationSec > maxSec)
                    {
                        //could call thread.Abort() here but that'll be deprecated in .NET 5
                        //and also it won't work on unmanaged code
                        //so for now just let it run, this should be rare...
                        //the only way to kill UVAtlas would be to run it in a separate subprocess
                        //but that would mean serializing out the mesh
                        //and that would take a bit of a rearchitecture to do well
                        //typically the entire tactical or contextual mesh pipeline is run in a separate process
                        //and we ran UVAtlas as a background thread, so it'll eat CPU but will die with the process
                        if (logger != null)
                        {
                            logger.LogError("UVAtlas runtime {0} > {1}",
                                            Fmt.HMS(durationSec * 1000), Fmt.HMS(maxSec * 1000));
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                ts.error = ex;
            }

            if (ts.error != null && logger != null)
            {
                logger.LogError("UVAtlas error: " + ts.error.Message);
            }

            if (!ts.done || ts.error != null || ts.rc != UVAtlasNET.UVAtlas.ReturnCode.SUCCESS)
            {
                bool fallback = fallbackToNaive && ts.done;
                if (logger != null)
                {
                    logger.LogError("UVAtlas failed, return code {0}{1}",
                                    ts.rc, fallback ? ", falling back to naive atlasing" : "");
                }
                if (!fallback)
                {
                    return false;
                }
                if (!NaiveAtlas.Compute(mesh, out outU, out outV, out indices, out outVertexRemap))
                {
                    if (logger != null)
                    {
                        logger.LogError("UVAtlas fallback naive atlasing failed");
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
