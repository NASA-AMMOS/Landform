using Microsoft.Xna.Framework;
using OPS.Imaging;
using OPS.MathExtensions;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry.GLTF
{
    /// <summary>
    /// Structure classes for serializing gltf files as json
    /// </summary>
    public class GLTFFile
    {
        public int scene;
        public List<string> extensionsUsed = new List<string>();
        public GLTFAsset asset = new GLTFAsset();
        public List<GLTFAccessor> accessors = new List<GLTFAccessor>();
        public List<GLTFNode> nodes = new List<GLTFNode>();
        public List<GLTFScene> scenes = new List<GLTFScene>();
        public List<GLTFBuffer> buffers = new List<GLTFBuffer>();
        public List<GLTFBufferView> bufferViews = new List<GLTFBufferView>();
        public List<GLTFMesh> meshes = new List<GLTFMesh>();
        public List<GLTFImage> images = new List<GLTFImage>();
        public List<GLTFSampler> samplers = new List<GLTFSampler>();
        public List<GLTFTexture> textures = new List<GLTFTexture>();
        public List<GLTFMaterial> materials = new List<GLTFMaterial>();

        [Newtonsoft.Json.JsonIgnore]
        public byte[] Data { get; set; }

        /// <summary>
        /// Create a GLTF file.  
        /// </summary>
        /// <param name="m"></param>
        /// <param name="imageFilename"></param>
        /// <param name="embedData">If true mesh and image data will be base64 encoded and included in the json segment.  Otherwise they will be stored as a byte array in this.Data.  Set to false when writing binary gltf files (glb)</param>
        public GLTFFile(Mesh m, string imageFilename, bool embedData = true)
        {
            extensionsUsed.Add("KHR_materials_unlit");

            // Add a single node pointing at the first mesh
            GLTFNode n = new GLTFNode();
            n.mesh = 0;
            nodes.Add(n);

            // Add a single scene pointing at the first node
            GLTFScene s = new GLTFScene();
            s.name = "scene";
            s.nodes.Add(0);
            scenes.Add(s);

            // Specify the first and only scene id
            scene = 0;

            List<byte> bytes = new List<byte>();
            GLTFPrimitive primitive = new GLTFPrimitive();
            // Vertices
            {
                var bounds = m.Bounds();
                GLTFAccessor accessor = new GLTFAccessor()
                {
                    bufferView = 0,
                    byteOffset = 0,
                    componentType = GLTFAccessor.FLOAT_COMPONENT,
                    count = m.Vertices.Count,
                    type = GLTFAccessor.VEC3_TYPE,
                    name = "vertices",
                    min = bounds.Min.ToFloatArray(),
                    max = bounds.Max.ToFloatArray(),
                };
                accessors.Add(accessor);
                List<byte> curBytes = new List<byte>();
                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    curBytes.AddRange(FloatBytes(m.Vertices[i].Position.X));
                    curBytes.AddRange(FloatBytes(m.Vertices[i].Position.Y));
                    curBytes.AddRange(FloatBytes(m.Vertices[i].Position.Z));
                }
                bytes.AddRange(curBytes);
                primitive.attributes.Add("POSITION", primitive.attributes.Count);
                GLTFBufferView bufferView = new GLTFBufferView()
                {
                    buffer = 0,
                    byteLength = curBytes.Count,
                    byteOffset = 0,
                };
                bufferViews.Add(bufferView);
            }
            if (bytes.Count % 4 != 0)
            {
                throw new Exception("Unexpected alignment");
            }
            // Normals
            if (m.HasNormals)
            {
                var bounds = m.NormalBounds();
                GLTFAccessor accessor = new GLTFAccessor()
                {
                    bufferView = 1,
                    byteOffset = 0,
                    componentType = GLTFAccessor.FLOAT_COMPONENT,
                    count = m.Vertices.Count,
                    type = GLTFAccessor.VEC3_TYPE,
                    name = "normals",
                    min = bounds.Min.ToFloatArray(),
                    max = bounds.Max.ToFloatArray(),
                };
                accessors.Add(accessor);
                List<byte> curBytes = new List<byte>();
                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    curBytes.AddRange(FloatBytes(m.Vertices[i].Normal.X));
                    curBytes.AddRange(FloatBytes(m.Vertices[i].Normal.Y));
                    curBytes.AddRange(FloatBytes(m.Vertices[i].Normal.Z));
                }
                bytes.AddRange(curBytes);
                primitive.attributes.Add("NORMAL", primitive.attributes.Count);
                GLTFBufferView bufferView = new GLTFBufferView()
                {
                    buffer = 0,
                    byteLength = curBytes.Count,
                    byteOffset = bufferViews.Last().byteOffset + bufferViews.Last().byteLength,
                };
                bufferViews.Add(bufferView);
            }
            if (bytes.Count % 4 != 0)
            {
                throw new Exception("Unexpected alignment");
            }
            // UVs
            if (m.HasUVs)
            {
                GLTFAccessor accessor = new GLTFAccessor()
                {
                    bufferView = 2,
                    byteOffset = 0,
                    componentType = GLTFAccessor.FLOAT_COMPONENT,
                    count = m.Vertices.Count,
                    type = GLTFAccessor.VEC2_TYPE,
                    name = "uvs"
                };
                Vector2 firstUV = m.Vertices[0].UV;
                firstUV.Y = 1 - firstUV.Y;
                Vector2 uvMin = firstUV;
                Vector2 uvMax = firstUV;
                accessors.Add(accessor);
                List<byte> curBytes = new List<byte>();
                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    var uv = m.Vertices[i].UV;
                    uv.Y = 1 - uv.Y;
                    uvMin = Vector2.Min(uv, uvMin);
                    uvMax = Vector2.Max(uv, uvMax);
                    curBytes.AddRange(FloatBytes(uv.X));
                    curBytes.AddRange(FloatBytes(uv.Y));
                }
                bytes.AddRange(curBytes);
                accessor.min = uvMin.ToFloatArray();
                accessor.max = uvMax.ToFloatArray();
                primitive.attributes.Add("TEXCOORD_0", primitive.attributes.Count);
                GLTFBufferView bufferView = new GLTFBufferView()
                {
                    buffer = 0,
                    byteLength = curBytes.Count,
                    byteOffset = bufferViews.Last().byteOffset + bufferViews.Last().byteLength,
                };
                bufferViews.Add(bufferView);
            }
            if (bytes.Count % 4 != 0)
            {
                throw new Exception("Unexpected alignment");
            }            

            // indices
            if (m.HasFaces)
            {
                GLTFAccessor accessor = new GLTFAccessor()
                {
                    bufferView = 3,
                    byteOffset = 0,
                    componentType = GLTFAccessor.USHORT_COMPONENT,
                    count = m.Faces.Count * 3,
                    type = GLTFAccessor.SCALAR_TYPE,
                    name = "indices",
                    min = new float[] {ushort.MaxValue},
                    max = new float[] {ushort.MinValue}
                };
                accessors.Add(accessor);
                List<byte> curBytes = new List<byte>();
                for (int i = 0; i < m.Faces.Count; i++)
                {
                    var face = m.Faces[i];
                    accessor.min[0] = MathE.Min(face.P0, face.P1, face.P2, accessor.min[0]);
                    accessor.max[0] = MathE.Max(face.P0, face.P1, face.P2, accessor.max[0]);
                    curBytes.AddRange(UShortBytes(face.P0));
                    curBytes.AddRange(UShortBytes(face.P1));
                    curBytes.AddRange(UShortBytes(face.P2));
                }
                bytes.AddRange(curBytes);   
                GLTFBufferView bufferView = new GLTFBufferView()
                {
                    buffer = 0,
                    byteLength = curBytes.Count,
                    byteOffset = bufferViews.Last().byteOffset + bufferViews.Last().byteLength,
                };                
                bufferViews.Add(bufferView);
                primitive.indices = accessors.Count - 1;
                primitive.mode = GLTFPrimitive.TRIANGLES;
            }
            else
            {
                primitive.mode = GLTFPrimitive.POINTS;
            }
            int paddingAdded = 0;
            // Add padding to ensure 4 byte alignment
            while (bytes.Count % 4 != 0)
            {
                bytes.Add((byte)0);
                paddingAdded++;
            }
            StringBuilder builder = new StringBuilder();
            builder.Append(GLTFBuffer.OCTET_HEADER);
            builder.Append(System.Convert.ToBase64String(bytes.ToArray()));

           
            GLTFMesh gm = new GLTFMesh();
            gm.primitives.Add(primitive);
            meshes.Add(gm);


            if (imageFilename != null)
            {
                byte[] imageData = File.ReadAllBytes(imageFilename); ;

                GLTFImage img = new GLTFImage();
                string ext = Path.GetExtension(imageFilename).ToLower();
                if (embedData)
                {
                    StringBuilder sb = new StringBuilder();   
                    if (ext == ".jpg")
                    {
                        sb.Append(GLTFImage.JPG_HEADER);
                    }
                    else if (ext == ".png")
                    {
                        sb.Append(GLTFImage.PNG_HEADER);
                    }
                    else
                    {
                        throw new MeshSerializerException("Unsupported image format for gltf export");
                    }
                    sb.Append(System.Convert.ToBase64String(imageData));
                    img.uri = sb.ToString();
                }
                else
                {
                    var prevView = bufferViews[bufferViews.Count - 1];
                    var imgView = new GLTFBufferView()
                    {
                        buffer = 0,
                        byteLength = imageData.Length,
                        byteOffset = bufferViews.Last().byteOffset + bufferViews.Last().byteLength + paddingAdded
                    };
                    bytes.AddRange(imageData);
                    bufferViews.Add(imgView);
                    img.bufferView = bufferViews.Count - 1;
                    if (ext == ".jpg")
                    {
                        img.mimeType = GLTFImage.JPG_MIME;
                    }
                    else if (ext == ".png")
                    {
                        img.mimeType = GLTFImage.PNG_MIME;
                    }
                    else
                    {
                        throw new MeshSerializerException("Unsupported image format for gltf export");
                    }
                }
                // Add padding to ensure 4 byte alignment
                while (bytes.Count % 4 != 0)
                {
                    bytes.Add((byte)0);
                }
                images.Add(img);
                samplers.Add(new GLTFSampler());
                GLTFTexture texture = new GLTFTexture()
                {
                    sampler = 0,
                    source = 0
                };
                textures.Add(texture);

                GLTFMaterial material = new GLTFMaterial();
                material.extensions = new Dictionary<string, object>();
                material.extensions.Add("KHR_materials_unlit", new Dictionary<string, object>());
                materials.Add(material);
                primitive.material = 0;
            } else
            {
                images = null;
                samplers = null;
                textures = null;
                materials = null;
            }


            GLTFBuffer buffer = new GLTFBuffer()
            {
                byteLength = bytes.Count
            };
            if (embedData)
            {
                buffer.uri = builder.ToString();
            }
            else
            {
                this.Data = bytes.ToArray();
            }
            buffers.Add(buffer);
        }

        public static byte[] FloatBytes(double value)
        {
            float f = (float) value;
            byte[] bytes = BitConverter.GetBytes(f);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return bytes;
        }

        public static byte[] UShortBytes(int value)
        {
            ushort f = (ushort) value;
            byte[] bytes = BitConverter.GetBytes(f);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return bytes;
        }
    }

    public class GLTFAsset
    {
        public string generator = "landform";
        public string version = "2.0";
    }

    public class GLTFNode
    {
        public int mesh;
    }

    public class GLTFScene
    {
        public string name;
        public List<int> nodes = new List<int>();
    }

    public class GLTFAccessor
    {
        public const int FLOAT_COMPONENT = 5126;
        public const int USHORT_COMPONENT = 5123;
        public const string VEC3_TYPE = "VEC3";
        public const string VEC2_TYPE = "VEC2";
        public const string SCALAR_TYPE = "SCALAR";
        
        public int bufferView;
        public int byteOffset;
        public int componentType;
        public int count;
        public float[] min;
        public float[] max;
        public string type;
        public string name;
    }

    public class GLTFBuffer
    {
        public const string OCTET_HEADER = "data:application/octet-stream;base64,";
        public int byteLength;
        public string uri;


    }

    public class GLTFBufferView
    {
        public int buffer;
        public int byteLength;
        public int byteOffset;
        public int? byteStride;
    }

    public class GLTFMesh
    {
        public List<GLTFPrimitive> primitives = new List<GLTFPrimitive>();
    }

    public class GLTFPrimitive
    {
        public const int POINTS = 0;
        public const int TRIANGLES = 4;

        public Dictionary<string, int> attributes = new Dictionary<string, int>();
        public int? indices = null;
        public int? material = null;
        public int mode = TRIANGLES;
    }

    public class GLTFImage
    {
        public const string JPG_HEADER = "data:image/jpeg;base64,";
        public const string PNG_HEADER = "data:image/png;base64,";
        public const string JPG_MIME = "image/jpeg";
        public const string PNG_MIME = "image/png";
        public string uri;
        public string mimeType;
        public int? bufferView;

    }

    public class GLTFSampler
    {
        public const int LINEAR = 9729;
        public const int CLAMP = 33071;
        public const int REPEAT = 10497;

        public int magFilter = LINEAR;
        public int minFilter = LINEAR;
        public int wrapS = CLAMP;
        public int wrapT = CLAMP;
    }

    public class GLTFTexture
    {
        public int sampler;
        public int source;
    }

    public class GLTFMaterial
    {
        public GLTFPBRMetallicRoughness pbrMetallicRoughness = new GLTFPBRMetallicRoughness();
        public Dictionary<string, object> extensions;
    }

    public class GLTFPBRMetallicRoughness
    {
        public float[] baseColorFactor = new float[] { 1, 1, 1, 1 };
        public GLTFTextureIndex baseColorTexture = new GLTFTextureIndex();
        public float metallicFactor = 0;
        public float roughnessFactor = 1;
    }

    public class GLTFTextureIndex
    {
        public int index = 0;
    }
}
