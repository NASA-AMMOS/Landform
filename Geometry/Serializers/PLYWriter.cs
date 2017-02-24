using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    /// <summary>
    /// Extendable base class for implementing different kinds of PLYWriter
    /// </summary>
    public abstract class PLYWriter
    {
        protected abstract void WriteVertexPositionHeader(StreamWriter sw);
        protected abstract void WriteVertexPostion(Vertex v, Stream s);
        protected abstract void WriteVertexNormalHeader(StreamWriter sw);
        protected abstract void WriteVertexNormal(Vertex v, Stream s);
        protected abstract void WriteVertexUVHeader(StreamWriter sw);
        protected abstract void WriteVertexUV(Vertex v, Stream s);
        protected abstract void WriteVertexColorHeader(StreamWriter sw);
        protected abstract void WriteVertexColor(Vertex v, Stream s);

        public virtual void WriteHeader(Mesh m, StreamWriter sw, string textureName = null, List<string> comments = null)
        {
            sw.WriteLine("ply");
            sw.WriteLine("format binary_little_endian 1.0");

            if (textureName != null)
            {
                sw.WriteLine("comment " + PLYSerializer.TextureFileCommentName +" " + textureName);
            }
            if (comments != null)
            {
                foreach (string comm in comments)
                {
                    sw.WriteLine("comment " + comm);
                }
            }
            sw.WriteLine("element vertex " + m.Vertices.Count);
            WriteVertexStructureHeader(m, sw);
            if (m.HasFaces)
            {
                WriteFaceHeader(m, sw);
            }
            sw.WriteLine("end_header");
        }

        protected virtual void WriteFaceHeader(Mesh m, StreamWriter sw)
        {
            sw.WriteLine("element face " + m.Faces.Count);
            sw.WriteLine("property list uchar int vertex_indices");
        }

        protected virtual void WriteVertexStructureHeader(Mesh m, StreamWriter sw)
        {
            WriteVertexPositionHeader(sw);
            if (m.HasNormals)
            {
                WriteVertexNormalHeader(sw);
            }
            if (m.HasUVs)
            {
                WriteVertexUVHeader(sw);
            }
            if (m.HasColors)
            {
                WriteVertexColorHeader(sw);
            }
        }

        public virtual void WriteVertex(Mesh m, Vertex v, Stream s)
        {
            WriteVertexPostion(v, s);
            if (m.HasNormals)
            {
                WriteVertexNormal(v, s);
            }
            if (m.HasUVs)
            {
                WriteVertexUV(v, s);
            }
            if (m.HasColors)
            {
                WriteVertexColor(v, s);
            }
        }

        public virtual void WriteFace(Mesh m, Face f, Stream s)
        {
            s.WriteByte(3);
            WriteIntValue(f.P0, s);
            WriteIntValue(f.P1, s);
            WriteIntValue(f.P2, s);
        }

        protected void WriteIntValue(int value, Stream s)
        {
            byte[] b = BitConverter.GetBytes(value);
            s.Write(b, 0, b.Length);
        }

        protected void WriteFloatValue(float value, Stream s)
        {
            byte[] b = BitConverter.GetBytes(value);
            s.Write(b, 0, b.Length);
        }

        protected void WriteDoubleValue(double value, Stream s)
        {
            byte[] b = BitConverter.GetBytes(value);
            s.Write(b, 0, b.Length);
        }
    }

    /// <summary>
    /// Partial implementation of PLY writer handling the following fields
    /// 
    /// Sample Header                           
    /// ply
    /// format binary_little_endian 1.0
    /// [comments TextureFile filename]
    /// element vertex 3
    /// property [float/double] x
    /// property [float/double] y
    /// property [float/double] z
    /// property float nx
    /// property float ny
    /// property float nz
    /// element face 1
    /// property list uchar int vertex_indices
    /// end_header
    /// </summary>
    public abstract class PLYBaseWriter : PLYWriter
    {
        protected bool writeXYZValuesAsFloat;
        public PLYBaseWriter(bool writeXYZValuesAsFloat = false)
        {
            this.writeXYZValuesAsFloat = writeXYZValuesAsFloat;
        }

        protected override void WriteVertexPositionHeader(StreamWriter sw)
        {
            string numberFormat = writeXYZValuesAsFloat ? "float" : "double";
            sw.WriteLine("property " + numberFormat + " x");
            sw.WriteLine("property " + numberFormat + " y");
            sw.WriteLine("property " + numberFormat + " z");
        }

        protected override void WriteVertexPostion(Vertex v, Stream s)
        {
            if (writeXYZValuesAsFloat)
            {
                WriteFloatValue((float)v.Position.X, s);
                WriteFloatValue((float)v.Position.Y, s);
                WriteFloatValue((float)v.Position.Z, s);
            }
            else
            {
                WriteDoubleValue(v.Position.X, s);
                WriteDoubleValue(v.Position.Y, s);
                WriteDoubleValue(v.Position.Z, s);
            }
        }

        protected override void WriteVertexNormalHeader(StreamWriter sw)
        {
            sw.WriteLine("property float nx");
            sw.WriteLine("property float ny");
            sw.WriteLine("property float nz");
        }

        protected override void WriteVertexNormal(Vertex v, Stream s)
        {
            WriteFloatValue((float)v.Normal.X, s);
            WriteFloatValue((float)v.Normal.Y, s);
            WriteFloatValue((float)v.Normal.Z, s);
        }
    }

    /// <summary>
    /// Seeks to make a ply that can be correctly interpreted by most tools.
    /// The truth table / sample header below shows what properties are required by which tools
    /// 
    /// MeshLab Notes
    ///     Requires the face elements contain "float texcoord" property in order for the uv map to appear on meshlabs uv map.  These are "wedge textCoords" in meshlab parlance 
    ///     Requires the vertex properties "texture_u" and "texture_v" exist and are of type "float" in order to recognise what it calls "vert textCoords"
    ///     As a result we duplicate these values
    ///     Requires color values be stored as uchar
    ///     
    /// Blender Compatibility mode
    ///     Requires uv coordinates be stored as per vertex named "s" and "t".  We include these in addition to "texture_u" and "texture_v" even though they are exact duplicates
    ///         
    /// Sample Header / Truth table of supported properties
    /// Blank means full support
    /// A type value means only supported with that type
    /// NA means ignored
    /// 
    ///                                                 MeshLab         Blender         CloudCompare
    /// ply
    /// format binary_little_endian 1.0
    /// [comments TextureFile filename] 
    /// element vertex 3
    /// property [float/double] x
    /// property [float/double] y
    /// property [float/double] z
    /// property float nx
    /// property float ny
    /// property float nz
    /// property float texture_u                         float          NA              NA
    /// property float texture_v                         float          NA              NA
    /// property double s                                NA             float/double    NA
    /// property double t                                NA             float/double    NA
    /// property uchar red                               uchar          uchar/float     uchar/float
    /// property uchar green                             uchar          uchar/float     uchar/float
    /// property uchar blue                              uchar          uchar/float     uchar/float
    /// property uchar alpha                             uchar          uchar/float     uchar/float
    /// element face 1
    /// property list uchar int vertex_indices
    /// property list uchar float texcoord               float          NA              float/double
    /// end_header
    /// </summary>
    public class PLYMaximumCompatibilityWriter : PLYBaseWriter
    {
        public PLYMaximumCompatibilityWriter(bool writeXYZValuesAsFloat) : base(writeXYZValuesAsFloat) { }

        protected override void WriteVertexUV(Vertex v, Stream s)
        {
            WriteFloatValue((float)v.UV.U, s);
            WriteFloatValue((float)v.UV.V, s);
            WriteDoubleValue(v.UV.U, s);
            WriteDoubleValue(v.UV.V, s);
        }

        protected override void WriteVertexUVHeader(StreamWriter sw)
        {
            sw.WriteLine("property float texture_u");
            sw.WriteLine("property float texture_v");
            sw.WriteLine("property double s");
            sw.WriteLine("property double t");
        }

        protected override void WriteVertexColorHeader(StreamWriter sw)
        {
            sw.WriteLine("property uchar red");
            sw.WriteLine("property uchar green");
            sw.WriteLine("property uchar blue");
            sw.WriteLine("property uchar alpha");
        }

        protected override void WriteVertexColor(Vertex v, Stream s)
        {
            s.WriteByte((byte)(v.Color.R * 255));
            s.WriteByte((byte)(v.Color.G * 255));
            s.WriteByte((byte)(v.Color.B * 255));
            s.WriteByte((byte)(v.Color.A * 255));
        }

        protected override void WriteFaceHeader(Mesh m, StreamWriter sw)
        {
            base.WriteFaceHeader(m, sw);
            if (m.HasUVs)
            {
                sw.WriteLine("property list uchar float texcoord");
            }
        }

        public override void WriteFace(Mesh m, Face f, Stream s)
        {
            base.WriteFace(m, f, s);
            if (m.HasUVs)
            {
                s.WriteByte(6);
                WriteFloatValue((float)m.Vertices[f.P0].UV.U, s);
                WriteFloatValue((float)m.Vertices[f.P0].UV.V, s);
                WriteFloatValue((float)m.Vertices[f.P1].UV.U, s);
                WriteFloatValue((float)m.Vertices[f.P1].UV.V, s);
                WriteFloatValue((float)m.Vertices[f.P2].UV.U, s);
                WriteFloatValue((float)m.Vertices[f.P2].UV.V, s);
            }
        }
    }

    /// <summary>
    /// Same as maximum compatibility writer except
    /// - Position valeus always stored as doubles
    /// - Color values stored as floats
    /// - Texture coordintes stored as doubles.  Only uses s and t        
    /// Sample Header 
    /// ply
    /// format binary_little_endian 1.0
    /// [comments TextureFile filename]
    /// element vertex 3
    /// property double x
    /// property double y
    /// property double z
    /// property float nx
    /// property float ny
    /// property float nz
    /// property double s 
    /// property double t 
    /// property float red
    /// property float green
    /// property float blue
    /// property float alpha
    /// element face 1
    /// property list uchar int vertex_indices
    /// end_header
    /// </summary>
    public class PLYHighPrecisionWriter : PLYBaseWriter
    {
        public PLYHighPrecisionWriter() : base(false) { }

        protected override void WriteVertexUVHeader(StreamWriter sw)
        {
            sw.WriteLine("property double s");
            sw.WriteLine("property double t");
        }

        protected override void WriteVertexUV(Vertex v, Stream s)
        {
            WriteDoubleValue(v.UV.U, s);
            WriteDoubleValue(v.UV.V, s);
        }

        protected override void WriteVertexColorHeader(StreamWriter sw)
        {
            sw.WriteLine("property float red");
            sw.WriteLine("property float green");
            sw.WriteLine("property float blue");
            sw.WriteLine("property float alpha");
        }

        protected override void WriteVertexColor(Vertex v, Stream s)
        {
            WriteFloatValue((float)v.Color.R, s);
            WriteFloatValue((float)v.Color.G, s);
            WriteFloatValue((float)v.Color.B, s);
            WriteFloatValue((float)v.Color.A, s);
        }
    }
}
