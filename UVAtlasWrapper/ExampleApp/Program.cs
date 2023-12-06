using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExampleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var mesh = OPS.Geometry.Mesh.Load(args[0]);
            Console.WriteLine("Input: " + mesh.Vertices.Count + " " + mesh.Faces.Count);
            var result = Atlas(mesh, 512, 512);
            Console.WriteLine("Output: " + mesh.Vertices.Count + " " + mesh.Faces.Count);
            result.Save(args[1]);
        }

        /// <summary>
        /// Mesh will lose all attributes such as color and normals
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="width"></param>
        /// 
        /// <param name="height"></param>
        /// <param name="maxCharts"></param>
        /// <param name="maxStretch"></param>
        /// <param name="gutter"></param>
        /// <returns></returns>
        public static OPS.Geometry.Mesh Atlas(OPS.Geometry.Mesh mesh, int width = 512, int height = 512, int maxCharts = 0, float maxStretch = 0.1666f, float gutter = 2)
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
            UVAtlasNET.UVAtlas.Atlas(inX, inY, inZ, indices, out outU, out outV, out indices, out outVertexRemap, maxCharts, maxStretch, gutter, width, height);
            if (indices.Length % 3 != 0)
            {
                throw new Exception("Atlas output indices not divisible by 3");
            }
            OPS.Geometry.Mesh result = new OPS.Geometry.Mesh(hasUVs: true, hasNormals: mesh.HasNormals, hasColors: mesh.HasColors);
            for (int i = 0; i < outVertexRemap.Length; i++)
            {
                var vert = new OPS.Geometry.Vertex(mesh.Vertices[outVertexRemap[i]]);
                vert.UV = new Microsoft.Xna.Framework.Vector2(outU[i], outV[i]);
                result.Vertices.Add(vert);
            }
            for (int i = 0; i < indices.Length; i += 3)
            {
                result.Faces.Add(new OPS.Geometry.Face(indices[i], indices[i + 1], indices[i + 2]));
            }
            result.HasNormals = true;
            return result;
        }
    }
}
