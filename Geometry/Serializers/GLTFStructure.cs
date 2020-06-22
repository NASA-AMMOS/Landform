using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OPS.Util;
using OPS.MathExtensions;
using OPS.Imaging;

namespace OPS.Geometry.GLTF
{
    /// <summary>
    /// Structures for serializing gltf files as JSON.
    /// </summary>
    public class GLTFFile
    {
        public const string JPG_MIME = "image/jpeg";
        public const string PNG_MIME = "image/png";
        public const string PPMZ_MIME = "image/x-portable-pixmap+gzip";
        public const string PPM_MIME = "image/x-portable-pixmap";
        public const string BIN_MIME = "application/octet-stream";

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

        [JsonIgnore]
        public byte[] Data;

        /// <summary>
        /// Create a GLTF file.  
        /// </summary>
        /// <param name="m"></param>
        /// <param name="imageFilename"></param>
        /// <param name="embedData">If true mesh and image data will be base64 encoded and included in the json
        /// segment.  Otherwise they will be stored as a byte array in this.Data.  Set to false when writing binary
        /// gltf files (glb).</param>
        public GLTFFile(Mesh m, string imageFilename, string indexFilename = null, bool embedData = true)
        {
            extensionsUsed.Add("KHR_materials_unlit");

            // single node pointing at the first mesh
            var node  = new GLTFNode();
            node.mesh = 0;
            nodes.Add(node);

            // single scene pointing at the first node
            var scene = new GLTFScene();
            scene.name = "scene";
            scene.nodes.Add(0);
            scenes.Add(scene);

            // first and only scene id
            this.scene = 0;

            // single primitive
            var primitive = new GLTFPrimitive();

            // single mesh
            var mesh = new GLTFMesh();
            mesh.primitives.Add(primitive);
            meshes.Add(mesh);

            if (imageFilename == null && indexFilename != null)
            {
                throw new MeshSerializerException("glTF file cannot have index without texture");
            }

            if (imageFilename != null)
            {
                //single sampler
                samplers.Add(new GLTFSampler());

                //single material
                var material = new GLTFMaterial();
                material.extensions = new Dictionary<string, object>();
                material.extensions.Add("KHR_materials_unlit", new Dictionary<string, object>());
                materials.Add(material);

                primitive.material = 0;

                textures.Add(new GLTFTexture() { sampler = 0, source = 0 });

                if (indexFilename != null)
                {
                    textures.Add(new GLTFTexture() { sampler = 0, source = 1 });
                }
            }
            else
            {
                images = null;
                samplers = null;
                textures = null;
                materials = null;
            }

            //from here down we fill a big buffer with all the binary data
            //we also add bufferViews, accessors, and images

            //load binary image data first, we'll deal with it later
            //but we can pre-allocate the big byte buffer now if we know its total size
            var imageFiles = new List<string>();
            var imageBufs = new List<byte[]>();
            if (imageFilename != null)
            {
                imageFiles.Add(imageFilename);
                imageBufs.Add(File.ReadAllBytes(imageFilename));
            }
            if (indexFilename != null)
            {
                imageFiles.Add(indexFilename);
                imageBufs.Add(File.ReadAllBytes(indexFilename));
            }

            int numBytes = 3 * 4 * m.Vertices.Count; //positions
            if (m.HasNormals)
            {
                numBytes += 3 * 4 * m.Vertices.Count;
            }
            if (m.HasUVs)
            {
                numBytes += 2 * 4 * m.Vertices.Count;
            }
            if (m.HasFaces)
            {
                numBytes += Pad(3 * 2 * m.Faces.Count);
            }
            foreach (var buf in imageBufs)
            {
                numBytes += Pad(buf.Length);
            }

            //the big buffer
            var bytes = new List<byte>(numBytes);

            //vertex positions
            {
                var bufferView = new GLTFBufferView()
                {
                    buffer = 0,
                    byteLength = m.Vertices.Count * 3 * 4,
                    byteOffset = 0,
                };
                bufferViews.Add(bufferView);

                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    bytes.AddRange(FloatBytes(m.Vertices[i].Position.X));
                    bytes.AddRange(FloatBytes(m.Vertices[i].Position.Y));
                    bytes.AddRange(FloatBytes(m.Vertices[i].Position.Z));
                }

                var bounds = m.Bounds();
                var accessor = new GLTFAccessor()
                {
                    bufferView = bufferViews.Count - 1,
                    byteOffset = 0,
                    componentType = GLTFAccessor.FLOAT_COMPONENT,
                    count = m.Vertices.Count,
                    type = GLTFAccessor.VEC3_TYPE,
                    name = "vertices",
                    min = bounds.Min.ToFloatArray(),
                    max = bounds.Max.ToFloatArray(),
                };
                accessors.Add(accessor);

                primitive.attributes.Add("POSITION", accessors.Count - 1);
            }

            if (m.HasNormals)
            {
                var bufferView = new GLTFBufferView()
                {
                    buffer = 0,
                    byteLength = m.Vertices.Count * 3 * 4,
                    byteOffset = bytes.Count
                };
                bufferViews.Add(bufferView);

                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    bytes.AddRange(FloatBytes(m.Vertices[i].Normal.X));
                    bytes.AddRange(FloatBytes(m.Vertices[i].Normal.Y));
                    bytes.AddRange(FloatBytes(m.Vertices[i].Normal.Z));
                }

                var bounds = m.NormalBounds();
                var accessor = new GLTFAccessor()
                {
                    bufferView = bufferViews.Count - 1,
                    byteOffset = 0,
                    componentType = GLTFAccessor.FLOAT_COMPONENT,
                    count = m.Vertices.Count,
                    type = GLTFAccessor.VEC3_TYPE,
                    name = "normals",
                    min = bounds.Min.ToFloatArray(),
                    max = bounds.Max.ToFloatArray(),
                };
                accessors.Add(accessor);

                primitive.attributes.Add("NORMAL", accessors.Count - 1);
            }

            if (m.HasUVs)
            {
                var bufferView = new GLTFBufferView()
                {
                    buffer = 0,
                    byteLength = m.Vertices.Count * 2 * 4,
                    byteOffset = bytes.Count
                };
                bufferViews.Add(bufferView);

                //GLTF texture coordinates are Y down, Landform texture coordinates are Y up
                //https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#images

                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    bytes.AddRange(FloatBytes(m.Vertices[i].UV.X));
                    bytes.AddRange(FloatBytes(1 - m.Vertices[i].UV.Y));
                }

                var bounds = m.UVBounds(flipY: true);
                var accessor = new GLTFAccessor()
                {
                    bufferView = bufferViews.Count - 1,
                    byteOffset = 0,
                    componentType = GLTFAccessor.FLOAT_COMPONENT,
                    count = m.Vertices.Count,
                    type = GLTFAccessor.VEC2_TYPE,
                    name = "uvs",
                    min = bounds.Min.XY().ToFloatArray(),
                    max = bounds.Max.XY().ToFloatArray()
                };
                accessors.Add(accessor);

                primitive.attributes.Add("TEXCOORD_0", accessors.Count - 1);
            }

            if (m.HasFaces)
            {
                var bufferView = new GLTFBufferView()
                {
                    buffer = 0,
                    byteLength = m.Faces.Count * 3 * 2,
                    byteOffset = bytes.Count
                };                
                bufferViews.Add(bufferView);

                ushort minIndex = ushort.MaxValue, maxIndex = ushort.MinValue;
                for (int i = 0; i < m.Faces.Count; i++)
                {
                    var face = m.Faces[i];
                    minIndex = (ushort)MathE.Min(minIndex, face.P0, face.P1, face.P2);
                    maxIndex = (ushort)MathE.Max(maxIndex, face.P0, face.P1, face.P2);
                    bytes.AddRange(UShortBytes(face.P0));
                    bytes.AddRange(UShortBytes(face.P1));
                    bytes.AddRange(UShortBytes(face.P2));
                }
                PadBytes(bytes);

                var accessor = new GLTFAccessor()
                {
                    bufferView = bufferViews.Count - 1,
                    byteOffset = 0,
                    componentType = GLTFAccessor.USHORT_COMPONENT,
                    count = m.Faces.Count * 3,
                    type = GLTFAccessor.SCALAR_TYPE,
                    name = "indices",
                    min = new float[] { minIndex },
                    max = new float[] { maxIndex }
                };
                accessors.Add(accessor);

                primitive.indices = accessors.Count - 1;
                primitive.mode = GLTFPrimitive.TRIANGLES;
            }
            else
            {
                primitive.mode = GLTFPrimitive.POINTS;
            }

            for (int i = 0; i < imageFiles.Count; i++)
            {
                var image = new GLTFImage();
                images.Add(image);
                image.mimeType = ExtToMime(imageFiles[i]);
                if (embedData)
                {
                    image.uri = Base64Encode(image.mimeType, imageBufs[i]);
                }
                else
                {
                    var bufferView = new GLTFBufferView()
                    {
                        buffer = 0,
                        byteLength = imageBufs[i].Length,
                        byteOffset = bytes.Count
                    };
                    bufferViews.Add(bufferView);

                    bytes.AddRange(imageBufs[i]);
                    PadBytes(bytes);

                    image.bufferView = bufferViews.Count - 1;
                }
            }

            var buffer = new GLTFBuffer() { byteLength = bytes.Count };
            buffers.Add(buffer);
            if (embedData)
            {
                buffer.uri = Base64Encode(BIN_MIME, bytes.ToArray());
            }
            else
            {
                Data = bytes.ToArray();
            }
        }

        public string ToJson(bool indent = false)
        {
            var ignoreNulls = new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore };
            var formatting = indent ? Formatting.Indented : Formatting.None;
            return JsonConvert.SerializeObject(this, formatting, ignoreNulls);
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

        public static byte[] UIntBytes(int value)
        {
            return UIntBytes((UInt32)value);
        }

        public static byte[] UIntBytes(UInt32 value)
        {
            UInt32 i = (UInt32) value;
            byte[] bytes = BitConverter.GetBytes(i);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return bytes;
        }

        public static byte[] UShortBytes(int value)
        {
            ushort s = (ushort) value;
            byte[] bytes = BitConverter.GetBytes(s);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return bytes;
        }

        public static string Base64Encode(string mimeType, byte[] bytes)
        {
            var sb = new StringBuilder();
            sb.Append($"data:{mimeType};base64,");
            sb.Append(System.Convert.ToBase64String(bytes));
            return sb.ToString();
        }

        public static string ExtToMime(string fileOrExt)
        {
            if (fileOrExt.IndexOf('.') > 0)
            {
                fileOrExt = Path.GetExtension(fileOrExt);
            }
            switch (fileOrExt.ToLower().TrimStart('.'))
            {
                case "jpg": return JPG_MIME;
                case "png": return PNG_MIME;
                case "ppmz": return PPMZ_MIME;
                case "ppm": return PPM_MIME;
                default: throw new MeshSerializerException("unsupported format for gltf: " + fileOrExt);
            }
        }

        public static int Pad(int i)
        {
            int padding = 4 - (i % 4);
            return i + padding;
        }

        public static void PadBytes(List<byte> bytes)
        {
            while (bytes.Count % 4 != 0)
            {
                bytes.Add((byte)0);
            }
        }

        public static string PadString(string str)
        {
            int padding = 4 - (str.Length % 4);
            return padding > 0 ? (str + new string(' ', padding)) : str;
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
        public GLTFTextureIndex baseColorTexture = new GLTFTextureIndex(0);
        public GLTFTextureIndex indexTexture = new GLTFTextureIndex(1);
        public float metallicFactor = 0;
        public float roughnessFactor = 1;
    }

    public class GLTFTextureIndex
    {      
        public int index;
        public GLTFTextureIndex(int index)
        {
            this.index = index;
        }
    }
}
