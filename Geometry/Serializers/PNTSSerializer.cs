using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
namespace JPLOPS.Geometry
{
    public class PNTSSerializer : MeshSerializer
    {
        public override string GetExtension()
        {
            return ".pnts";
        }

        const int VERSION_NUM = 1;
        private const int HEADER_SIZE = 28;

        public override Mesh Load(string filename)
        {
            Mesh m;
            bool hasNormals = false, hasColors = false;
            using (BinaryReader br = new BinaryReader(File.Open(filename, FileMode.Open)))
            {
                byte[] magic = br.ReadBytes(4);
                if (Encoding.ASCII.GetString(magic) != "pnts")
                {
                    throw new Exception("Pnts magic field mismatch");
                }
                UInt32 version = br.ReadUInt32();
                if (version != VERSION_NUM)
                {
                    throw new Exception("Pnts version mismatch");
                }
                br.ReadUInt32();    //total byte length
                UInt32 featureTableJsonByteLength = br.ReadUInt32();
                UInt32 featureTableBinaryByteLength = br.ReadUInt32();
                br.ReadUInt32();    //batch table JSON byte length
                br.ReadUInt32();    //batch table binary byte length
                var jsonString = Encoding.ASCII.GetString(br.ReadBytes((int)featureTableJsonByteLength));
                FeatureTable featureTableJson = JsonConvert.DeserializeObject<FeatureTable>(jsonString);
                byte[] binary = br.ReadBytes((int)featureTableBinaryByteLength);

                List<Vertex> vertices = new List<Vertex>();

                for (int i = featureTableJson.POSITION.byteOffset; i < featureTableJson.POINTS_LENGTH * 12 + featureTableJson.POSITION.byteOffset; i += 12)
                {
                    float x = BitConverter.ToSingle(binary, i);
                    float y = BitConverter.ToSingle(binary, i + 4);
                    float z = BitConverter.ToSingle(binary, i + 8);
                    vertices.Add(new Vertex(new Vector3(x, y, z)));
                }
                if (featureTableJson.NORMAL != null)
                {
                    hasNormals = true;
                    int cnt = 0;
                    for (int i = featureTableJson.NORMAL.byteOffset; i < featureTableJson.POINTS_LENGTH * 12 + featureTableJson.NORMAL.byteOffset; i += 12)
                    {
                        float x = BitConverter.ToSingle(binary, i);
                        float y = BitConverter.ToSingle(binary, i + 4);
                        float z = BitConverter.ToSingle(binary, i + 8);
                        vertices[cnt++].Normal = new Vector3(x, y, z);
                    }
                }
                if (featureTableJson.RGB != null)
                {
                    hasColors = true;
                    int cnt = 0;
                    for (int i = featureTableJson.RGB.byteOffset; i < featureTableJson.POINTS_LENGTH * 3 + featureTableJson.RGB.byteOffset; i += 3)
                    {
                        byte r = binary[i];
                        byte g = binary[i + 1];
                        byte b = binary[i + 2];
                        vertices[cnt++].Color = new Vector4(r, g, b, 255) / byte.MaxValue;

                    }
                }
                m = new Mesh(hasNormals, false, hasColors, featureTableJson.POINTS_LENGTH);
                m.Vertices = vertices;
                if (featureTableJson.RTC_CENTER != null)
                {
                    m.Translate((new Vector3(featureTableJson.RTC_CENTER[0], featureTableJson.RTC_CENTER[1], featureTableJson.RTC_CENTER[2])));
                }
            }
            return m;
        }

        class BinaryBodyReference
        {
            public int byteOffset;
            public BinaryBodyReference() { }
            public BinaryBodyReference(int offset)
            {
                this.byteOffset = offset;
            }
        }

        class FeatureTable
        {
            public int POINTS_LENGTH;
            public BinaryBodyReference POSITION;
            public BinaryBodyReference RGB;
            public BinaryBodyReference NORMAL;
            public float[] RTC_CENTER = null;

            public FeatureTable() { }
            public FeatureTable(int pointsLength, bool hasNormals, bool hasColors)
            {
                this.POINTS_LENGTH = pointsLength;
                this.POSITION = new BinaryBodyReference(0);
                if (hasNormals)
                {
                    this.NORMAL = new BinaryBodyReference(pointsLength * 12);
                }
                if (hasColors)
                {
                    this.RGB = new BinaryBodyReference(pointsLength * 12 + (hasNormals ? pointsLength * 12 : 0));
                }
            }
        }

        public override void Save(Mesh m, string filename, string imageFilename = null)
        {

            string featureTableJsonString = JsonConvert.SerializeObject(new FeatureTable(m.Vertices.Count, m.HasNormals, m.HasColors), Formatting.Indented);

            if (featureTableJsonString.Length % 4 != 0)
            {
                featureTableJsonString += new string(' ', 4 - (featureTableJsonString.Length % 4));
            }
            byte[] featureTableJson = Encoding.ASCII.GetBytes(featureTableJsonString);

            List<float> positionBinary = new List<float>();
            List<byte> colorBinary = new List<byte>();
            List<float> normalBinary = new List<float>();

            for (int i = 0; i < m.Vertices.Count; i++)
            {
                positionBinary.Add((float)m.Vertices[i].Position.X);
                positionBinary.Add((float)m.Vertices[i].Position.Y);
                positionBinary.Add((float)m.Vertices[i].Position.Z);


            }
            if (m.HasColors)
            {
                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    colorBinary.Add((byte)(m.Vertices[i].Color.X * byte.MaxValue));
                    colorBinary.Add((byte)(m.Vertices[i].Color.Y * byte.MaxValue));
                    colorBinary.Add((byte)(m.Vertices[i].Color.Z * byte.MaxValue));
                }
            }
            if (m.HasNormals)
            {
                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    normalBinary.Add((float)m.Vertices[i].Normal.X);
                    normalBinary.Add((float)m.Vertices[i].Normal.Y);
                    normalBinary.Add((float)m.Vertices[i].Normal.Z);
                }
            }

            using (FileStream fs = new FileStream(filename, FileMode.Create))
            {
                using (BinaryWriter br = new BinaryWriter(fs))
                {
                    // Write Magic pnts character string
                    br.Write((UInt32)0x73746E70);
                    // Write version
                    br.Write((UInt32)VERSION_NUM);
                    // Write total byte length including header
                    UInt32 binaryLength = (UInt32)(positionBinary.Count * 4 + colorBinary.Count + normalBinary.Count * 4);
                    UInt32 totalLength = (UInt32)(HEADER_SIZE + featureTableJson.Length + binaryLength);
                    br.Write(totalLength);
                    // Write feature table 
                    br.Write((UInt32)featureTableJson.Length);      // json length
                    br.Write((binaryLength));  // binary length
                    // Write batch table 
                    br.Write((UInt32)0);                            // json length
                    br.Write((UInt32)0);                            // binary length
                    // Write feature table
                    br.Write(featureTableJson);
                    foreach (float p in positionBinary)
                    {
                        br.Write(p);
                    }
                    foreach (float n in normalBinary)
                    {
                        br.Write(n);
                    }
                    foreach (byte c in colorBinary)
                    {
                        br.Write(c);
                    }
                }
            }
        }
    }
}
