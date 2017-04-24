using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry.GLTF;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;
using Newtonsoft.Json;
using System.IO;
namespace OPS.Geometry
{
    /// <summary>
    /// Class for writing gltf files that consit of a single mesh and texture with default material
    /// </summary>
    public class GLTFSerializer : MeshSerializer
    {
        public GLTFSerializer() { }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            GLTFSerializer.Write(m, filename, imageFilename);
        }

        public override Mesh Load(string filename)
        {
            throw new NotImplementedException();
        }

        public override string GetExtension()
        {
            return ".gltf";
        }

        public static void Write(Mesh m, string filename, string imageFilename = null)
        {
            GLTFFile f = new GLTFFile();
            // Add a single node pointing at the first mesh
            GLTFNode n = new GLTFNode();
            n.mesh = 0;
            f.nodes.Add(n);

            // Add a single scene pointing at the first node
            GLTFScene s = new GLTFScene();
            s.name = "scene";
            s.nodes.Add(0);
            f.scenes.Add(s);

            // Specify the first and only scene id
            f.scene = 0;

            List<byte> bytes = new List<byte>();
            GLTFPrimitive primitive = new GLTFPrimitive();
            // Vertices
            {
                var bounds = m.Bounds();
                GLTFAccessor accessor = new GLTFAccessor()
                {
                    bufferView = 0,
                    byteOffset = bytes.Count,
                    componentType = GLTFAccessor.FLOAT_COMPONENT,
                    count = m.Vertices.Count,
                    type = GLTFAccessor.VEC3_TYPE,
                    name = "vertices",
                    min = bounds.Min.ToFloatArray(),
                    max = bounds.Max.ToFloatArray(),
                };
                f.accessors.Add(accessor);
                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    bytes.AddRange(FloatBytes(m.Vertices[i].Position.X));
                    bytes.AddRange(FloatBytes(m.Vertices[i].Position.Y));
                    bytes.AddRange(FloatBytes(m.Vertices[i].Position.Z));
                }
                primitive.attributes.Add("POSITION", primitive.attributes.Count);
            }

            // Normals
            if (m.HasNormals)
            {
                var bounds = m.NormalBounds();
                GLTFAccessor accessor = new GLTFAccessor()
                {
                    bufferView = 0,
                    byteOffset = bytes.Count,
                    componentType = GLTFAccessor.FLOAT_COMPONENT,
                    count = m.Vertices.Count,
                    type = GLTFAccessor.VEC3_TYPE,
                    name = "normals",
                    min = bounds.Min.ToFloatArray(),
                    max = bounds.Max.ToFloatArray(),
                };
                f.accessors.Add(accessor);
                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    bytes.AddRange(FloatBytes(m.Vertices[i].Normal.X));
                    bytes.AddRange(FloatBytes(m.Vertices[i].Normal.Y));
                    bytes.AddRange(FloatBytes(m.Vertices[i].Normal.Z));
                }
                primitive.attributes.Add("NORMAL", primitive.attributes.Count);
            }

            // Normals
            if (m.HasUVs)
            {
                var bounds = m.UVBounds();
                GLTFAccessor accessor = new GLTFAccessor()
                {
                    bufferView = 0,
                    byteOffset = bytes.Count,
                    componentType = GLTFAccessor.FLOAT_COMPONENT,
                    count = m.Vertices.Count,
                    type = GLTFAccessor.VEC2_TYPE,
                    name = "uvs",
                    min = bounds.Min.ToFloatArray().Take(2).ToArray(),
                    max = bounds.Max.ToFloatArray().Take(2).ToArray(),
                };
                f.accessors.Add(accessor);
                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    bytes.AddRange(FloatBytes(m.Vertices[i].UV.X));
                    bytes.AddRange(FloatBytes(m.Vertices[i].UV.Y));
                }
                primitive.attributes.Add("TEXCOORD_0", primitive.attributes.Count);
            }
            GLTFBufferView firstView = new GLTFBufferView()
            {
                buffer = 0,
                byteLength = bytes.Count,
                byteOffset = 0,
                byteStride = 0
            };
            f.bufferViews.Add(firstView);
            // indices
            if (m.HasFaces)
            {
                GLTFAccessor accessor = new GLTFAccessor()
                {
                    bufferView = 1,
                    byteOffset = 0,
                    componentType = GLTFAccessor.USHORT_COMPONENT,
                    count = m.Faces.Count * 3,
                    type = GLTFAccessor.SCALAR_TYPE,
                    name = "indices",
                    min = new float[] { ushort.MaxValue },
                    max = new float[] { ushort.MinValue }
                };
                f.accessors.Add(accessor);
                for (int i = 0; i < m.Faces.Count; i++)
                {
                    var face = m.Faces[i];
                    accessor.min[0] = MathE.Min(face.P0, face.P1, face.P2, accessor.min[0]);
                    accessor.max[0] = MathE.Max(face.P0, face.P1, face.P2, accessor.max[0]);
                    bytes.AddRange(UShortBytes(face.P0));
                    bytes.AddRange(UShortBytes(face.P1));
                    bytes.AddRange(UShortBytes(face.P2));
                }
                GLTFBufferView secondView = new GLTFBufferView()
                {
                    buffer = 0,
                    byteLength = bytes.Count - firstView.byteLength,
                    byteOffset = firstView.byteLength,
                    byteStride = 0
                };
                f.bufferViews.Add(secondView);
                primitive.indices = f.accessors.Count - 1;
                primitive.mode = GLTFPrimitive.TRIANGLES;
            }
            else
            {
                primitive.mode = GLTFPrimitive.POINTS;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(GLTFBuffer.OCTET_HEADER);
            builder.Append(System.Convert.ToBase64String(bytes.ToArray()));

            GLTFBuffer buffer = new GLTFBuffer()
            {
                byteLength = bytes.Count,
                uri = builder.ToString()
            };
            f.buffers.Add(buffer);
            GLTFMesh gm = new GLTFMesh();
            gm.primitives.Add(primitive);
            f.meshes.Add(gm);


            if(imageFilename != null)
            {
                StringBuilder sb = new StringBuilder();
                string ext = Path.GetExtension(imageFilename).ToLower();
                if (ext == ".jpg")
                {
                    sb.Append(GLTFImage.JPG_HEADER);
                }
                else if(ext == ".png")
                {
                    sb.Append(GLTFImage.PNG_HEADER);
                }
                else
                {
                    throw new MeshSerializerException("Unsupported image format for gltf export");
                }
                sb.Append(System.Convert.ToBase64String(File.ReadAllBytes(imageFilename)));
                GLTFImage img = new GLTFImage()
                {
                    uri = sb.ToString()
                };
                f.images.Add(img);
                f.samplers.Add(new GLTFSampler());
                GLTFTexture texture = new GLTFTexture()
                {
                    sampler = 0,
                    source = 0
                };
                f.textures.Add(texture);
                
                GLTFMaterial material = new GLTFMaterial();
                f.materials.Add(material);
                primitive.material = 0;                
            }

            JsonSerializerSettings settings = new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Ignore                
            };
            File.WriteAllText(filename, JsonConvert.SerializeObject(f, Formatting.Indented, settings), new UTF8Encoding(false));
        }
        
        public static byte[] FloatBytes(double value)
        {
            float f = (float)value;
            byte[] bytes = BitConverter.GetBytes(f);
            if(!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return bytes;
        }

        public static byte[] UShortBytes(int value)
        {
            ushort f = (ushort)value;
            byte[] bytes = BitConverter.GetBytes(f);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return bytes;
        }

    }

    

}
