using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{

    /// <summary>
    /// PLY Writer to support creation of ply files that can be read by FSSR
    /// This class uses a single scale value for all points
    /// In the future it could be extended with a derived vertex class with per vertex scale
    /// Or scale could be integrated into the base vertex class
    /// </summary>
    public class FSSRPlyWriter : PLYBaseWriter
    {
        float scale;

        /// <summary>
        /// Scale value to use for all points in the mesh
        /// </summary>
        /// <param name="scale"></param>
        public FSSRPlyWriter(float scale)
        {
            this.writeXYZValuesAsFloat = true;
            this.scale = scale;
        }
        
        protected override void WriteVertexStructureHeader(Mesh m, StreamWriter sw)
        {
            base.WriteVertexStructureHeader(m, sw);
            sw.WriteLine("property float value"); // scale value
        }

        public override void WriteVertex(Mesh m, Vertex v, Stream s)
        {
            base.WriteVertex(m, v, s);
            WriteFloatValue(scale, s);
        }

        protected override void WriteVertexColor(Vertex v, Stream s)
        {
            s.WriteByte((byte)(v.Color.R * 255));
            s.WriteByte((byte)(v.Color.G * 255));
            s.WriteByte((byte)(v.Color.B * 255));
        }

        protected override void WriteVertexColorHeader(StreamWriter sw)
        {
            sw.WriteLine("property uchar red");
            sw.WriteLine("property uchar green");
            sw.WriteLine("property uchar blue");
        }

        protected override void WriteVertexUV(Vertex v, Stream s)
        {
            throw new Exception("FSSR meshes cannot contain texture coordinates");
        }

        protected override void WriteVertexUVHeader(StreamWriter sw)
        {
            throw new Exception("FSSR meshes cannot contain texture coordinates");
        }
    }
}
