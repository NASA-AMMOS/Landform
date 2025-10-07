using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using JPLOPS.MathExtensions;

namespace JPLOPS.Geometry
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

        public virtual void WriteHeader(Mesh m, StreamWriter sw, string textureName = null,
                                        List<string> comments = null)
        {
            sw.WriteLine("ply");
            sw.WriteLine("format binary_little_endian 1.0");

            if (textureName != null)
            {
                sw.WriteLine("comment " + PLYSerializer.TextureFileCommentName + " " + Path.GetFileName(textureName));
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
    /// property [float/double] nx
    /// property [float/double ny
    /// property [float/double] nz
    /// [property [float/double] value]
    /// property [float/double] texture_u
    /// property [float/double] texture_v
    /// property [float/uchar] red
    /// property [float/uchar] green
    /// property [float/uchar] blue
    /// [property [float/uchar] alpha]
    /// element face 1
    /// property list uchar int vertex_indices
    /// end_header
    /// </summary>
    public abstract class PLYBaseWriter : PLYWriter
    {
        protected readonly bool writePositionAsFloat;
        protected readonly bool writeNormalAsFloat;
        protected readonly bool writeNormalLengthsAsValue;
        protected readonly bool writeValueAsFloat;
        protected readonly bool writeUVAsFloat;
        protected readonly bool writeColorAsFloat;
        protected readonly bool writeAlpha;

        public PLYBaseWriter(bool writePositionAsFloat = true, bool writeNormalAsFloat = true,
                             bool writeNormalLengthsAsValue = false, bool writeValueAsFloat = true,
                             bool writeUVAsFloat = true, bool writeColorAsFloat = false, bool writeAlpha = true)
        {
            this.writePositionAsFloat = writePositionAsFloat;
            this.writeNormalAsFloat = writeNormalAsFloat;
            this.writeNormalLengthsAsValue = writeNormalLengthsAsValue;
            this.writeValueAsFloat = writeValueAsFloat;
            this.writeUVAsFloat = writeUVAsFloat;
            this.writeColorAsFloat = writeColorAsFloat;
            this.writeAlpha = writeAlpha;
        }

        protected override void WriteVertexPositionHeader(StreamWriter sw)
        {
            var dt = writePositionAsFloat ? "float" : "double";
            sw.WriteLine($"property {dt} x");
            sw.WriteLine($"property {dt} y");
            sw.WriteLine($"property {dt} z");
        }

        protected override void WriteVertexPostion(Vertex v, Stream s)
        {
            if (writePositionAsFloat)
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

        protected override void WriteVertexUVHeader(StreamWriter sw)
        {
            string dt = writeUVAsFloat ? "float" : "double";
            sw.WriteLine($"property {dt} texture_u");
            sw.WriteLine($"property {dt} texture_v");
        }

        protected override void WriteVertexUV(Vertex v, Stream s)
        {
            if (writeUVAsFloat)
            {
                WriteFloatValue((float)v.UV.U, s);
                WriteFloatValue((float)v.UV.V, s);
            }
            else
            {
                WriteDoubleValue(v.UV.U, s);
                WriteDoubleValue(v.UV.V, s);
            }
        }

        protected override void WriteVertexColorHeader(StreamWriter sw)
        {
            string dt = writeColorAsFloat ? "float" : "uchar";
            sw.WriteLine($"property {dt} red");
            sw.WriteLine($"property {dt} green");
            sw.WriteLine($"property {dt} blue");
            if (writeAlpha)
            {
                sw.WriteLine($"property {dt} alpha");
            }
        }

        protected override void WriteVertexColor(Vertex v, Stream s)
        {
            if (writeColorAsFloat)
            {
                WriteFloatValue((float)(v.Color.R), s);
                WriteFloatValue((float)(v.Color.G), s);
                WriteFloatValue((float)(v.Color.B), s);
                if (writeAlpha)
                {
                    WriteFloatValue((float)(v.Color.A), s);
                }
            }
            else
            {
                s.WriteByte((byte)(v.Color.R * 255));
                s.WriteByte((byte)(v.Color.G * 255));
                s.WriteByte((byte)(v.Color.B * 255));
                if (writeAlpha)
                {
                    s.WriteByte((byte)(v.Color.A * 255));
                }
            }
        }

        protected override void WriteVertexNormalHeader(StreamWriter sw)
        {
            var dt = writeNormalAsFloat ? "float" : "double";
            sw.WriteLine($"property {dt} nx");
            sw.WriteLine($"property {dt} ny");
            sw.WriteLine($"property {dt} nz");
            if (writeNormalLengthsAsValue)
            {
                dt = writeValueAsFloat ? "float" : "double";
                sw.WriteLine($"property {dt} value");
            }
        }

        protected override void WriteVertexNormal(Vertex v, Stream s)
        {
            Vector3 n = v.Normal;
            double val = -1;
            if (writeNormalLengthsAsValue)
            {
                val = n.Length();
                if (val > MathE.EPSILON && Math.Abs(val - 1) > MathE.EPSILON)
                {
                    n.Normalize();
                }
            }
            if (writeNormalAsFloat)
            {
                WriteFloatValue((float)n.X, s);
                WriteFloatValue((float)n.Y, s);
                WriteFloatValue((float)n.Z, s);
            }
            else
            {
                WriteDoubleValue(n.X, s);
                WriteDoubleValue(n.Y, s);
                WriteDoubleValue(n.Z, s);
            }
            if (writeNormalLengthsAsValue)
            {
                if (writeValueAsFloat)
                {
                    WriteFloatValue((float)val, s);
                }
                else
                {
                    WriteDoubleValue(val, s);
                }
            }
        }
    }

    /// <summary>
    /// Seeks to make a ply that can be correctly interpreted by most tools.
    /// The truth table / sample header below shows what properties are required by which tools
    /// 
    /// MeshLab Notes
    ///
    ///     Requires the face elements contain "float texcoord" property in order for the uv map to appear on meshlabs
    ///     uv map.  These are "wedge textCoords" in meshlab parlance
    ///
    ///     Requires the vertex properties "texture_u" and "texture_v" exist and are of type "float" in order to
    ///     recognise what it calls "vert textCoords"
    ///
    ///     As a result we duplicate these values
    ///     Requires color values be stored as uchar
    ///     
    /// Blender Compatibility mode
    //
    ///     Requires uv coordinates be stored as per vertex named "s" and "t".  We include these in addition to
    ///     "texture_u" and "texture_v" even though they are exact duplicates
    ///         
    /// Sample Header / Truth table of supported properties
    /// Blank means full support
    /// A type value means only supported with that type
    /// NA means ignored
    /// 
    ///                                                 MeshLab         Blender         CloudCompare   PoissionRecon
    /// ply
    /// format binary_little_endian 1.0
    /// [comments TextureFile filename] 
    /// element vertex 3
    /// property float x
    /// property float y
    /// property float z
    /// property float nx
    /// property float ny
    /// property float nz
    /// [property float value]                           NA             NA              NA             density
    /// property float texture_u                         float          NA              NA
    /// property float texture_v                         float          NA              NA
    /// property float/double s                          NA             float/double    NA
    /// property float/double t                          NA             float/double    NA
    /// property uchar red                               uchar          uchar/float     uchar/float
    /// property uchar green                             uchar          uchar/float     uchar/float
    /// property uchar blue                              uchar          uchar/float     uchar/float
    /// [property uchar alpha]                           uchar          uchar/float     uchar/float
    /// element face 1
    /// property list uchar int vertex_indices
    /// property list uchar float/double texcoord        float          NA              float/double
    /// end_header
    /// </summary>
    public class PLYMaximumCompatibilityWriter : PLYBaseWriter
    {
        public PLYMaximumCompatibilityWriter(bool writeNormalLengthsAsValue = false, bool writeAlpha = true)
            : base(writeNormalLengthsAsValue: writeNormalLengthsAsValue, writeAlpha: writeAlpha)
        { }

        protected override void WriteVertexUVHeader(StreamWriter sw)
        {
            base.WriteVertexUVHeader(sw);
            string dt = writeUVAsFloat ? "float" : "double";
            sw.WriteLine($"property {dt} s");
            sw.WriteLine($"property {dt} t");
        }

        protected override void WriteVertexUV(Vertex v, Stream s)
        {
            base.WriteVertexUV(v, s);
            base.WriteVertexUV(v, s);
        }

        protected override void WriteFaceHeader(Mesh m, StreamWriter sw)
        {
            base.WriteFaceHeader(m, sw);
            if (m.HasUVs)
            {
                string dt = writeUVAsFloat ? "float" : "double";
                sw.WriteLine($"property list uchar {dt} texcoord");
            }
        }

        public override void WriteFace(Mesh m, Face f, Stream s)
        {
            base.WriteFace(m, f, s);
            if (m.HasUVs)
            {
                s.WriteByte(6);
                if (writeUVAsFloat)
                {
                    WriteFloatValue((float)m.Vertices[f.P0].UV.U, s);
                    WriteFloatValue((float)m.Vertices[f.P0].UV.V, s);
                    WriteFloatValue((float)m.Vertices[f.P1].UV.U, s);
                    WriteFloatValue((float)m.Vertices[f.P1].UV.V, s);
                    WriteFloatValue((float)m.Vertices[f.P2].UV.U, s);
                    WriteFloatValue((float)m.Vertices[f.P2].UV.V, s);
                }
                else
                {
                    WriteDoubleValue(m.Vertices[f.P0].UV.U, s);
                    WriteDoubleValue(m.Vertices[f.P0].UV.V, s);
                    WriteDoubleValue(m.Vertices[f.P1].UV.U, s);
                    WriteDoubleValue(m.Vertices[f.P1].UV.V, s);
                    WriteDoubleValue(m.Vertices[f.P2].UV.U, s);
                    WriteDoubleValue(m.Vertices[f.P2].UV.V, s);
                }
            }
        }
    }

    /// <summary>
    /// ply
    /// format binary_little_endian 1.0
    /// [comments TextureFile filename]
    /// element vertex 3
    /// property double x
    /// property double y
    /// property double z
    /// property double nx
    /// property double ny
    /// property double nz
    /// [property double value]
    /// property double texture_u
    /// property double texture_v
    /// property float red
    /// property float green
    /// property float blue
    /// [property float alpha]
    /// element face 1
    /// property list uchar int vertex_indices
    /// end_header
    /// </summary>
    public class PLYHighPrecisionWriter : PLYBaseWriter
    {
        public PLYHighPrecisionWriter(bool writeNormalLengthsAsValue = false, bool writeAlpha = true)
            : base(writeNormalLengthsAsValue: writeNormalLengthsAsValue, writeAlpha: writeAlpha,
                   writePositionAsFloat: false, writeNormalAsFloat: false, writeValueAsFloat: false,
                   writeUVAsFloat: false, writeColorAsFloat: true)
        { }
    }

    /// <summary>
    /// ply
    /// format binary_little_endian 1.0
    /// [comments TextureFile filename]
    /// element vertex 3
    /// property float x
    /// property float y
    /// property float z
    /// property float nx
    /// property float ny
    /// property float nz
    /// [property float value]
    /// property float texture_u
    /// property float texture_v
    /// property uchar red
    /// property uchar green
    /// property uchar blue
    /// [property uchar alpha]
    /// element face 1
    /// property list uchar int vertex_indices
    /// end_header
    /// </summary>
    public class PLYCompactFileWriter : PLYBaseWriter
    {
        public PLYCompactFileWriter(bool writeNormalLengthsAsValue = false, bool writeAlpha = true)
            : base(writeNormalLengthsAsValue: writeNormalLengthsAsValue, writeAlpha: writeAlpha)
        { }
    }
}
