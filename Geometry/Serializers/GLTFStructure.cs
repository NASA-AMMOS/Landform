using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using JPLOPS.Util;
using JPLOPS.MathExtensions;

namespace JPLOPS.Geometry.GLTF
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

        public delegate void ImageHandler(string mimeType, byte[] data);

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
        /// Default constructor for JSON deserialization.
        /// </summary>
        public GLTFFile() { }

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

            int indexBytes = m.Vertices.Count > 65535 ? 4 : 2;

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
                numBytes += Pad(3 * indexBytes * m.Faces.Count);
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
                    byteLength = m.Faces.Count * 3 * indexBytes,
                    byteOffset = bytes.Count
                };                
                bufferViews.Add(bufferView);

                //unsigned 32 bit int indices are supported by GLTF2.0, recent versions of Unity, and all major browsers
                //however, only use them when required
                //https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#primitiveindices
                //https://developer.mozilla.org/en-US/docs/Web/API/OES_element_index_uint

                int minIndex = int.MaxValue, maxIndex = int.MinValue;
                for (int i = 0; i < m.Faces.Count; i++)
                {
                    var face = m.Faces[i];
                    byte[] b0 = null, b1 = null, b2 = null;
                    if (indexBytes == 2)
                    {
                        b0 = UShortBytes(face.P0);
                        b1 = UShortBytes(face.P1);
                        b2 = UShortBytes(face.P2);
                    }
                    else
                    {
                        b0 = UIntBytes(face.P0);
                        b1 = UIntBytes(face.P1);
                        b2 = UIntBytes(face.P2);
                    }
                    bytes.AddRange(b0);
                    bytes.AddRange(b1);
                    bytes.AddRange(b2);
                    minIndex = MathE.Min(minIndex, face.P0, face.P1, face.P2);
                    maxIndex = MathE.Max(maxIndex, face.P0, face.P1, face.P2);
                }
                PadBytes(bytes);

                var accessor = new GLTFAccessor()
                {
                    bufferView = bufferViews.Count - 1,
                    byteOffset = 0,
                    componentType = indexBytes == 2 ? GLTFAccessor.USHORT_COMPONENT : GLTFAccessor.UINT_COMPONENT,
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

        public static GLTFFile FromJson(string json)
        {
            var ignoreNulls = new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore };
            var gltf = JsonConvert.DeserializeObject<GLTFFile>(json, ignoreNulls);
            if (gltf.buffers.Count > 0 && !string.IsNullOrEmpty(gltf.buffers[0].uri) &&
                gltf.buffers[0].uri.StartsWith("data:" + BIN_MIME))
            {
                gltf.Data = Base64Decode(gltf.buffers[0].uri, out string mimeType);
                gltf.buffers[0].uri = null;
            }
            return gltf;
        }

        public Mesh Decode(ImageHandler imageHandler = null, ImageHandler indexHandler = null)
        {
            if (meshes.Count < 1)
            {
                throw new MeshSerializerException("glTF has no meshes");
            }
            if (imageHandler != null && images.Count > 0)
            {
                DecodeImage(0, imageHandler);
            }
            if (indexHandler != null && images.Count > 1)
            {
                DecodeImage(1, indexHandler);
            }
            return DecodeMesh(0);
        }

        public Mesh DecodeMesh(int index)
        {
            if (index >= meshes.Count)
            {
                throw new MeshSerializerException("no glTF mesh at index " + index);
            }
            var mesh = meshes[index];
            if (mesh.primitives.Count != 1)
            {
                throw new MeshSerializerException("unsupported number of glTF mesh primitives: " +
                                                  mesh.primitives.Count);
            }
            var primitive = mesh.primitives[0];
            if (primitive.mode != GLTFPrimitive.POINTS && primitive.mode != GLTFPrimitive.TRIANGLES)
            {
                throw new MeshSerializerException("unsupported glTF primitive mode: " + primitive.mode);
            }
            if (!primitive.indices.HasValue)
            {
                throw new MeshSerializerException("glTF primitive without indices not supported");
            }

            var vertices = new List<Vertex>();

            {
                if (!primitive.attributes.ContainsKey("POSITION"))
                {
                    throw new MeshSerializerException("glTF primitive without POSITION attribute not supported");
                }
                var accessor = GetAccessor(primitive.attributes["POSITION"], a =>
                                           (a.componentType == GLTFAccessor.FLOAT_COMPONENT &&
                                            a.type == GLTFAccessor.VEC3_TYPE));
                var bufferView = GetBufferView(accessor.bufferView, accessor.byteOffset, accessor.count * 3 * 4);
                vertices.Capacity = accessor.count;
                int pos = bufferView.byteOffset + accessor.byteOffset;
                for (int i = 0; i < accessor.count; i++)
                {
                    vertices.Add(new Vertex(DecodeFloat(ref pos), DecodeFloat(ref pos), DecodeFloat(ref pos)));
                }
            }

            bool hasNormals = primitive.attributes.ContainsKey("NORMAL");
            if (hasNormals)
            {
                var accessor = GetAccessor(primitive.attributes["NORMAL"], a =>
                                           (a.componentType == GLTFAccessor.FLOAT_COMPONENT &&
                                            a.type == GLTFAccessor.VEC3_TYPE &&
                                            a.count == vertices.Count));
                var bufferView = GetBufferView(accessor.bufferView, accessor.byteOffset, accessor.count * 3 * 4);
                int pos = bufferView.byteOffset + accessor.byteOffset;
                for (int i = 0; i < accessor.count; i++)
                {
                    vertices[i].Normal =
                        new Vector3(DecodeFloat(ref pos), DecodeFloat(ref pos), DecodeFloat(ref pos));
                }
            }

            bool hasUVs = primitive.attributes.ContainsKey("TEXCOORD_0");
            if (hasUVs)
            {
                var accessor = GetAccessor(primitive.attributes["TEXCOORD_0"], a =>
                                           (a.componentType == GLTFAccessor.FLOAT_COMPONENT &&
                                            a.type == GLTFAccessor.VEC2_TYPE &&
                                            a.count == vertices.Count));
                var bufferView = GetBufferView(accessor.bufferView, accessor.byteOffset, accessor.count * 2 * 4);
                int pos = bufferView.byteOffset + accessor.byteOffset;
                for (int i = 0; i < accessor.count; i++)
                {
                    //GLTF texture coordinates are Y down, Landform texture coordinates are Y up
                    //https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#images
                    vertices[i].UV = new Vector2(DecodeFloat(ref pos), 1 - DecodeFloat(ref pos));
                }
            }

            var faces = new List<Face>();
            if (primitive.mode == GLTFPrimitive.TRIANGLES)
            {
                int accessorIndex = primitive.indices.Value;
                if (accessorIndex < accessors.Count)
                {
                    var accessor = accessors[accessorIndex];
                    int indexBytes = accessor.componentType == GLTFAccessor.USHORT_COMPONENT ? 2 :
                        accessor.componentType == GLTFAccessor.UINT_COMPONENT ? 4 : -1;

                    if (indexBytes < 0 || accessor.type != GLTFAccessor.SCALAR_TYPE || accessor.count % 3 != 0)
                    {
                        throw new MeshSerializerException("invalid glTF indices accessor");
                    }
                    int numFaces = accessor.count / 3;
                    faces.Capacity = numFaces;
                    var bufferView = GetBufferView(accessor.bufferView, accessor.byteOffset, numFaces * 3 * indexBytes);
                    int pos = bufferView.byteOffset + accessor.byteOffset;
                    for (int i = 0; i < numFaces; i++)
                    {
                        int p0 = -1, p1 = -1, p2 = -1;
                        if (indexBytes == 2)
                        {
                            p0 = DecodeUShort(ref pos);
                            p1 = DecodeUShort(ref pos);
                            p2 = DecodeUShort(ref pos);
                        }
                        else
                        {
                            p0 = (int)DecodeUInt(ref pos);
                            p1 = (int)DecodeUInt(ref pos);
                            p2 = (int)DecodeUInt(ref pos);
                        }
                        faces.Add(new Face(p0, p1, p2));
                    }
                }
                else
                {
                    throw new MeshSerializerException("no glTF accessor at index: " + accessorIndex);
                }
            }

            var ret = new Mesh(hasNormals, hasUVs);
            ret.Vertices = vertices;
            ret.Faces = faces;
            return ret;
        }

        public void DecodeImage(int index, ImageHandler handler)
        {
            if (index >= images.Count)
            {
                throw new MeshSerializerException("no glTF image at index " + index);
            }
            var image = images[index];
            if (image.bufferView.HasValue && !string.IsNullOrEmpty(image.mimeType))
            {
                handler(image.mimeType, GetDataSlice(image.bufferView.Value));
            }
            else if (image.uri.StartsWith("data:"))
            {
                var bytes = Base64Decode(image.uri, out string mimeType);
                handler(mimeType, bytes);
            }
            else
            {
                throw new MeshSerializerException("unhandled glTF image URI: " + StringHelper.Abbreviate(image.uri));
            }
        }

        public GLTFAccessor GetAccessor(int index, Func<GLTFAccessor, bool> validator = null)
        {
            if (index >= accessors.Count)
            {
                throw new MeshSerializerException("no glTF accessor at index: " + index);
            }
            var accessor = accessors[index];
            if (validator != null && !validator(accessor))
            {
                throw new MeshSerializerException("invalid glTF accessor");
            }
            return accessor;
        }

        public GLTFBufferView GetBufferView(int index, int extraOffset = 0, int minBytes = 0)
        {
            if (index >= bufferViews.Count)
            {
                throw new MeshSerializerException("no glTF buffer view at index " + index);
            }
            var bufferView = bufferViews[index];
            if (bufferView.buffer >= buffers.Count)
            {
                throw new MeshSerializerException("no glTF buffer at index " + bufferView.buffer);
            }
            if (!string.IsNullOrEmpty(buffers[bufferView.buffer].uri))
            {
                throw new MeshSerializerException("glTF buffer uri not supported " +
                                                  StringHelper.Abbreviate(buffers[bufferView.buffer].uri));
            }
            if (bufferView.buffer > 0)
            {
                throw new MeshSerializerException("glTF buffer index not supported " + bufferView.buffer);
            }
            if (bufferView.byteLength < minBytes)
            {
                throw new MeshSerializerException("glTF buffer view too small");
            }
            if (Data == null || Data.Length < extraOffset + bufferView.byteOffset + bufferView.byteLength)
            {
                throw new MeshSerializerException("glTF buffer view exceeds available data");
            }
            if (bufferView.byteStride.HasValue && bufferView.byteStride > 1)
            {
                throw new MeshSerializerException("glTF byte stride not supported: " + bufferView.byteStride.Value);
            }
            return bufferView;
        }

        public byte[] GetDataSlice(int index)
        {
            var bufferView = GetBufferView(index);
            byte[] slice = new byte[bufferView.byteLength];
            Array.Copy(Data, bufferView.byteOffset, slice, 0, bufferView.byteLength);
            return slice;
        }

        private static ThreadLocal<byte[]> tmp2 = new ThreadLocal<byte[]>(() => (new byte[2]));
        private static ThreadLocal<byte[]> tmp4 = new ThreadLocal<byte[]>(() => (new byte[4]));

        public float DecodeFloat(ref int pos)
        {
            float ret = DecodeFloat(Data, pos);
            pos += 4;
            return ret;
        }

        public static float DecodeFloat(byte[] bytes, int index)
        {
            if (!BitConverter.IsLittleEndian)
            {
                Array.Copy(bytes, index, tmp4.Value, 0, 4);
                Array.Reverse(tmp4.Value);
                bytes = tmp4.Value;
                index = 0;
            }
            return BitConverter.ToSingle(bytes, index);
        }

        public static float DecodeFloat(byte[] bytes)
        {
            return DecodeFloat(bytes, 0);
        }

        public uint DecodeUInt(ref int pos)
        {
            uint ret = DecodeUInt(Data, pos);
            pos += 4;
            return ret;
        }

        public static uint DecodeUInt(byte[] bytes, int index)
        {
            if (!BitConverter.IsLittleEndian)
            {
                Array.Copy(bytes, index, tmp4.Value, 0, 4);
                Array.Reverse(tmp4.Value);
                bytes = tmp4.Value;
                index = 0;
            }
            return BitConverter.ToUInt32(bytes, index);
        }

        public static uint DecodeUInt(byte[] bytes)
        {
            return DecodeUInt(bytes, 0);
        }

        public ushort DecodeUShort(ref int pos)
        {
            ushort ret = DecodeUShort(Data, pos);
            pos += 2;
            return ret;
        }

        public static ushort DecodeUShort(byte[] bytes, int index)
        {
            if (!BitConverter.IsLittleEndian)
            {
                Array.Copy(bytes, index, tmp2.Value, 0, 2);
                Array.Reverse(tmp2.Value);
                bytes = tmp2.Value;
                index = 0;
            }
            return BitConverter.ToUInt16(bytes, index);
        }

        public static ushort DecodeUShort(byte[] bytes)
        {
            return DecodeUShort(bytes, 0);
        }
        
        public static byte[] FloatBytes(double value)
        {
            byte[] bytes = BitConverter.GetBytes((float)value);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return bytes;
        }

        public static byte[] UIntBytes(int value)
        {
            byte[] bytes = BitConverter.GetBytes((uint)value);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return bytes;
        }

        public static byte[] UShortBytes(int value)
        {
            byte[] bytes = BitConverter.GetBytes((ushort)value);
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

        public static byte[] Base64Decode(string str, out string mimeType)
        {
            mimeType = null;
            foreach (var mt in new string[] { BIN_MIME, JPG_MIME, PNG_MIME, PPMZ_MIME, PPM_MIME })
            {
                string pfx = $"data:{mt};base64,";
                if (str.StartsWith(pfx))
                {
                    mimeType = mt;
                    str = str.Substring(pfx.Length);
                }
            }
            if (mimeType == null)
            {
                throw new MeshSerializerException("unsupported format for gltf: " + StringHelper.Abbreviate(str));
            }
            return System.Convert.FromBase64String(str);
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

        public static string MimeToExt(string mimeType)
        {
            switch (mimeType.ToLower().Trim())
            {
                case JPG_MIME: return "jpg";
                case PNG_MIME: return "png";
                case PPMZ_MIME: return "ppmz";
                case PPM_MIME: return "ppm";
                default: throw new MeshSerializerException("unsupported format for gltf: " + mimeType);
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
        public const int UINT_COMPONENT = 5125;
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
