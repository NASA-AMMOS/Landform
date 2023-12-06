using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;


namespace UVAtlasNET
{
    public static class UVAtlas
    {

        public enum Quality
        {
            UVATLAS_DEFAULT = 0x00,
            UVATLAS_GEODESIC_FAST = 0x01,
            UVATLAS_GEODESIC_QUALITY = 0x02,
        }

        public enum ReturnCode
        {
            SUCCESS = 0,
            UNKNOWN = 1,
            SET_INDEX_FAILED = 2,
            SET_VERTEX_FAILED = 3,
            GENERATE_ADJACENCY_FAILED  = 4,
            CREATE_ATLAS_FAILED = 5,
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UVAtlasData
        {
            public UInt32 numVertices;
            public IntPtr us;
            public IntPtr vs;
            public IntPtr xs;
            public IntPtr ys;
            public IntPtr zs;

            public UInt32 numFaces;
            public IntPtr indices;

            public IntPtr vertexRemap;
        };

        const string DLL_NAME = "UVAtlasLib_";

        [DllImport(DLL_NAME + "x32.dll", EntryPoint = "UVAtlas", CallingConvention = CallingConvention.Cdecl)]
        private static unsafe extern UVAtlasData* UVAtlas32(UVAtlasData* data, int maxCharts, float maxStretch, float gutter, int width, int height, Quality quality, float adjacencyEpsilon, out int returnCode);

        [DllImport(DLL_NAME + "x32.dll", EntryPoint = "UVAtlasData_Destroy", CallingConvention = CallingConvention.Cdecl)]
        private static unsafe extern void UVAtlasDestroy32(UVAtlasData* data);

        [DllImport(DLL_NAME + "x64.dll", EntryPoint = "UVAtlas", CallingConvention = CallingConvention.Cdecl)]
        private static unsafe extern UVAtlasData* UVAtlas64(UVAtlasData* data, int maxCharts, float maxStretch, float gutter, int width, int height, Quality quality, float adjacencyEpsilon, out int returnCode);

        [DllImport(DLL_NAME + "x64.dll", EntryPoint = "UVAtlasData_Destroy", CallingConvention = CallingConvention.Cdecl)]
        private static unsafe extern void UVAtlasDestroy64(UVAtlasData* data);

        /// <summary>
        /// Generates UVs for a mesh
        /// </summary>
        /// <param name="inX">Per vertex x positions</param>
        /// <param name="inY">Per vertex y positions</param>
        /// <param name="inZ">Per vertex z positions</param>
        /// <param name="inIndices">Array specifying vertex indices for faces.  Each 3 elements specify a face.</param>
        /// <param name="outU">Output array of u texture coordinates, may be a different length than input vertices</param>
        /// <param name="outV">Output array of v texture coordinates, may be a different length than input vertices</param>
        /// <param name="outIndices">Output array of indices specifying the returned set of faces</param>
        /// <param name="outVertexRemap">Array that maps new vertex index to old vertex index.  Given an index into the UV output arrays this will give the corresponding index in the input XYZ arrays</param>
        /// <param name="maxCharts">Max number of charts allowed when creating the atlas.  0 is unlimited</param>
        /// <param name="maxStretch">Max triangle stretch allowed 0-1</param>
        /// <param name="gutter">Number of gutter pixels allowed</param>
        /// <param name="width">Image width in number of pixels</param>
        /// <param name="height">Image height in number of pixels</param>
        /// <param name="quality">
        /// Default, Fast, or Quality.  Default will choose between fast and quality based on the size of the mesh
        /// These map directly to the DirectX UVAtlasCreate quality flags which are described by the DirectX documentation as:
        /// 
        /// UVATLAS_DEFAULT - By default, meshes with 25,000 or more faces default to "fast", otherwise they use "quality".
        /// 
        /// UVATLAS_GEODESIC_FAST - Uses approximations to improve charting speed at the cost of added stretch or more charts.
        /// 
        /// UVATLAS_GEODESIC_QUALITY - Provides better quality charts, but requires more time and memory than fast.
        /// Resulting mesh data after UV atlasing which will have a different 
        /// vertex order and new vertices due to duplication.The position data is replicated from the original vertices, 
        /// but has unique uv data.The number of output faces is the same as the input positions array.
        /// 
        /// </param>
        /// <param name="adjacencyEpsilon">Vertices that are closer than this value will be treated as coincident</param>
        /// <returns></returns>
        public static unsafe ReturnCode Atlas(
            float[] inX, float[] inY, float[] inZ, int[] inIndices,
            out float[] outU, out float[] outV, out int[] outIndices, out int[] outVertexRemap,
            int maxCharts = 0, float maxStretch = 0.1666f, float gutter = 2, int width = 512, int height = 512, Quality quality = Quality.UVATLAS_DEFAULT, float adjacencyEpsilon = 0)
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

            UVAtlasData data = new UVAtlasData();
            data.numVertices = (UInt32)inX.Length;
            data.xs = Marshal.AllocHGlobal(inX.Length * Marshal.SizeOf<float>());
            Marshal.Copy(inX, 0, data.xs, inX.Length);
            data.ys = Marshal.AllocHGlobal(inX.Length * Marshal.SizeOf<float>());
            Marshal.Copy(inY, 0, data.ys, inY.Length);
            data.zs = Marshal.AllocHGlobal(inX.Length * Marshal.SizeOf<float>());
            Marshal.Copy(inZ, 0, data.zs, inZ.Length);
            
            data.numFaces = (UInt32)(inIndices.Length / 3);
            data.indices = Marshal.AllocHGlobal(inIndices.Length * Marshal.SizeOf<UInt32>());
            unsafe
            {
                UInt32* idxs = (UInt32*)data.indices.ToPointer();
                for (int i = 0; i < inIndices.Length; i++)
                {
                    idxs[i] = (UInt32)inIndices[i];
                }
            }
            UVAtlasData* res;
            int rc;
            if (Environment.Is64BitProcess)
            {
                res = UVAtlas64(&data, maxCharts, maxStretch, gutter, width, height, quality, adjacencyEpsilon, out rc);
            }
            else
            {
                res = UVAtlas32(&data, maxCharts, maxStretch, gutter, width, height, quality, adjacencyEpsilon, out rc);
            }
            ReturnCode returnCode = (ReturnCode)rc;
            if (res == (UVAtlasData*) 0 || returnCode != ReturnCode.SUCCESS)
            {
                return returnCode;
            }
            outU = new float[res->numVertices];
            outV = new float[res->numVertices];
            outIndices = new int[res->numFaces * 3];
            outVertexRemap = new int[res->numVertices];

            Marshal.Copy(res->us, outU, 0, outU.Length);
            Marshal.Copy(res->vs, outV, 0, outV.Length);
            Marshal.Copy(res->indices, outIndices, 0, outIndices.Length);
            Marshal.Copy(res->vertexRemap, outVertexRemap, 0, outVertexRemap.Length);

            Marshal.FreeHGlobal(data.xs);
            Marshal.FreeHGlobal(data.ys);
            Marshal.FreeHGlobal(data.zs);
            Marshal.FreeHGlobal(data.indices);

            if (Environment.Is64BitProcess)
            {
                UVAtlasDestroy64(res);
            }
            else
            {
                UVAtlasDestroy32(res);
            }
            return returnCode;
        }
    }
 }
                                                                             