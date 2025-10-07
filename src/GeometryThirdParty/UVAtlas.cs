using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JPLOPS.Util;

namespace JPLOPS.Geometry
{
    public static class UVAtlas
    {
        public enum Quality
        {
            UVATLAS_DEFAULT = 0x00,
            UVATLAS_GEODESIC_FAST = 0x01,
            UVATLAS_GEODESIC_QUALITY = 0x02,
            UVATLAS_LIMIT_MERGE_STRETCH = 0x04,
            UVATLAS_LIMIT_FACE_STRETCH = 0x08,
        }

        public enum ReturnCode
        {
            SUCCESS = 0,
            UNKNOWN = 1,
            GENERATE_ADJACENCY_FAILED  = 2,
            CREATE_ATLAS_FAILED = 3,
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UVAtlasMesh
        {
            public UInt32 numVertices;
            public UInt32 numFaces;
            public IntPtr xs;
            public IntPtr ys;
            public IntPtr zs;
            public IntPtr indices;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UVAtlasResult
        {
            public UInt32 numVertices;
            public UInt32 numFaces;
            public IntPtr us;
            public IntPtr vs;
            public IntPtr indices;
            public IntPtr vertexRemap;
        };

        [DllImport("UVAtlasWrapper", EntryPoint = "UVAtlas_Compute", CallingConvention = CallingConvention.Cdecl)]
        private static unsafe extern UVAtlasResult*
            UVAtlas_Compute(UVAtlasMesh* mesh, int maxCharts, float maxStretch, float gutter, int width, int height,
                            uint quality, float adjacencyEpsilon, out int returnCode);

        [DllImport("UVAtlasWrapper", EntryPoint = "UVAtlas_Delete", CallingConvention = CallingConvention.Cdecl)]
        private static unsafe extern void UVAtlas_Delete(UVAtlasResult* result);

        /// <summary>
        /// Generates UVs for a mesh
        /// </summary>
        ///
        /// <param name="inX">Per vertex x positions</param>
        /// <param name="inY">Per vertex y positions</param>
        /// <param name="inZ">Per vertex z positions</param>
        /// <param name="inIndices">Array specifying vertex indices for faces.  Each 3 elements specify a face.</param>
        /// <param name="outU">Output array of u texcoords, may be a different length than input vertices</param>
        /// <param name="outV">Output array of v texcoords, may be a different length than input vertices</param>
        /// <param name="outIndices">Output array of indices specifying the returned set of faces</param>
        /// <param name="outVertexRemap">Array that maps new vertex index to old vertex index.  Given an index into the
        /// UV output arrays this will give the corresponding index in the input XYZ arrays</param>
        /// <param name="maxCharts">Max number of charts allowed when creating the atlas.  0 is unlimited</param>
        /// <param name="maxStretch">Max triangle stretch allowed 0-1</param>
        /// <param name="gutter">Number of gutter pixels allowed</param>
        /// <param name="width">Image width in number of pixels</param>
        /// <param name="height">Image height in number of pixels</param>
        /// <param name="quality"> Maps to the DirectX UVAtlasCreate options, see below</param>
        /// <param name="adjacencyEpsilon">Vertices that are closer than this will be treated as coincident</param>
        ///
        /// UVATLAS_DEFAULT - meshes with 25,000 or more faces use "fast", otherwise they use "quality".
        ///
        /// UVATLAS_GEODESIC_FAST - Uses approximations to improve speed at the cost of added stretch or more charts.
        ///
        /// UVATLAS_GEODESIC_QUALITY - Provides better quality charts, but requires more time and memory than fast.
        ///
        /// Resulting mesh data after UV atlasing which will have a different vertex order and new vertices due to
        /// duplication.The position data is replicated from the original vertices, but has unique uv data.The number of
        /// output faces is the same as the input positions array.
        public static unsafe ReturnCode RunUVAtlas(float[] inX, float[] inY, float[] inZ, int[] inIndices,
                                                   out float[] outU, out float[] outV,
                                                   out int[] outIndices, out int[] outVertexRemap,
                                                   int maxCharts = 0, float maxStretch = 0.1666f, float gutter = 2,
                                                   int width = 512, int height = 512,
                                                   Quality quality = Quality.UVATLAS_DEFAULT,
                                                   float adjacencyEpsilon = 0)
        {
            outU = null;
            outV = null;
            outIndices = null;
            outVertexRemap = null;

            if (inX.Length != inY.Length || inY.Length != inZ.Length)
            {
                throw new ArgumentException("Atlas input vector array length's do not match");
            }

            if (inIndices.Length % 3 != 0)
            {
                throw new ArgumentException("Atlas input indicies not divisible by 3");
            }

            UVAtlasMesh mesh = new UVAtlasMesh();

            int inNumVertices = inX.Length;

            mesh.numVertices = (UInt32)inNumVertices;

            mesh.xs = Marshal.AllocHGlobal(inNumVertices * Marshal.SizeOf<float>());
            mesh.ys = Marshal.AllocHGlobal(inNumVertices * Marshal.SizeOf<float>());
            mesh.zs = Marshal.AllocHGlobal(inNumVertices * Marshal.SizeOf<float>());
            Marshal.Copy(inX, 0, mesh.xs, inNumVertices);
            Marshal.Copy(inY, 0, mesh.ys, inNumVertices);
            Marshal.Copy(inZ, 0, mesh.zs, inNumVertices);

            mesh.numFaces = (UInt32)(inIndices.Length / 3);

            mesh.indices = Marshal.AllocHGlobal(inIndices.Length * Marshal.SizeOf<UInt32>());

            //cruelty: there is no Marshal.Copy() for unsigned int types
            //https://stackoverflow.com/questions/20981551
            unsafe
            {
                UInt32* idxs = (UInt32*)mesh.indices.ToPointer();
                for (int i = 0; i < inIndices.Length; i++)
                {
                    idxs[i] = (UInt32)inIndices[i];
                }
            }

            int rc;
            UVAtlasResult* res = UVAtlas_Compute(&mesh, maxCharts, maxStretch, gutter, width, height,
                                                 Convert.ToUInt32(quality), adjacencyEpsilon, out rc);
            ReturnCode returnCode = (ReturnCode)rc;

            Marshal.FreeHGlobal(mesh.xs);
            Marshal.FreeHGlobal(mesh.ys);
            Marshal.FreeHGlobal(mesh.zs);
            Marshal.FreeHGlobal(mesh.indices);

            if (res != (UVAtlasResult*)0 && returnCode == ReturnCode.SUCCESS)
            {
                outU = new float[res->numVertices];
                outV = new float[res->numVertices];
                Marshal.Copy(res->us, outU, 0, outU.Length);
                Marshal.Copy(res->vs, outV, 0, outV.Length);

                outIndices = new int[res->numFaces * 3];
                Marshal.Copy(res->indices, outIndices, 0, outIndices.Length);

                outVertexRemap = new int[res->numVertices];
                Marshal.Copy(res->vertexRemap, outVertexRemap, 0, outVertexRemap.Length);
            }

            UVAtlas_Delete(res);

            return returnCode;
        }

        public const int DEF_RESOLUTION = 512;
        public const int DEF_MAX_CHARTS = 0;
        //public const double DEF_MAX_STRETCH = 0.1666;
        public const double DEF_MAX_STRETCH = 0.5;
        //public const double DEF_MAX_STRETCH = 1;
        public const double DEF_GUTTER = 2;

        public const int DEF_MAX_SEC = 5 * 60;

        private class ThreadState
        {
            public volatile ReturnCode rc = ReturnCode.UNKNOWN;
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
            Quality quality = forceHighestQuality ? Quality.UVATLAS_GEODESIC_QUALITY : Quality.UVATLAS_DEFAULT;

            //ThreadState fields are volatile to ensure safe publication (memory fencing) from the worker thread to us
            //need to put them in the ThreadState class because local variables can't be volatile in c#
            var ts = new ThreadState();
            try
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        ts.rc = RunUVAtlas(inX, inY, inZ, indices,
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

            if (!ts.done || ts.error != null || ts.rc != ReturnCode.SUCCESS)
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
