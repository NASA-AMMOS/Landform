using System;
using System.IO;
using System.Text;
using System.Linq;
using JPLOPS.Geometry.GLTF;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Writes and reads binary GLTF files, version 2.
    /// Only supports subset of GLTF supported by GLTFSerializer.
    /// https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#binary-gltf-layout
    /// </summary>
    public class GLBSerializer : MeshSerializer
    {
        public override string GetExtension()
        {
            return ".glb";
        }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            Save(m, filename, imageFilename, null);
        }

        public static void Save(Mesh m, string filename, string imageFilename, string indexFilename)
        {
            using (var fs = new FileStream(filename, FileMode.Create))
            {
                WriteToStream(fs, m, imageFilename, indexFilename);
            }
        }

        public static void WriteToStream(Stream s, Mesh m, string imageFilename, string indexFilename)
        {
            using (var bw = new BinaryWriter(s))
            {
                var gltf = new GLTFFile(m, imageFilename, indexFilename, embedData: false);
                string json = GLTFFile.PadString(gltf.ToJson());
                byte[] jsonBytes = Encoding.ASCII.GetBytes(json);

                // header
                //bw.Write(GLTFFile.UIntBytes(0x46546C67)); // gltf magic number
                bw.Write(Encoding.ASCII.GetBytes("glTF"));
                bw.Write(GLTFFile.UIntBytes(2)); // version 2

                int headerBytes = 3 * 4 + 2 * 4 + 2 * 4;
                int totalLength = headerBytes + jsonBytes.Length + gltf.Data.Length;
                bw.Write(GLTFFile.UIntBytes(totalLength)); // total length of file

                // json chunk
                bw.Write(GLTFFile.UIntBytes(jsonBytes.Length)); // length of json in bytes
                //bw.Write(GLTFFile.UIntBytes(0x4E4F534A)); // json chunk type
                bw.Write(Encoding.ASCII.GetBytes("JSON"));
                bw.Write(jsonBytes); // json data

                // binary chunk                     
                bw.Write(GLTFFile.UIntBytes(gltf.Data.Length)); // length of binary data in bytes
                //bw.Write(GLTFFile.UIntBytes(0x004E4942)); // binary chunk type
                bw.Write(Encoding.ASCII.GetBytes("BIN\0").ToArray());
                bw.Write(gltf.Data); // binary data
            }
        }

        public override Mesh Load(string filename)
        {
            return Load(filename, null);
        }

        public static Mesh Load(string filename, GLTFFile.ImageHandler imageHandler,
                                GLTFFile.ImageHandler indexHandler = null)
        {
            using (var fs = new FileStream(filename, FileMode.Open))
            {
                return ReadFromStream(fs, imageHandler, indexHandler);
            }
        }

        public static Mesh ReadFromStream(Stream s, GLTFFile.ImageHandler imageHandler = null,
                                          GLTFFile.ImageHandler indexHandler = null)
        {
            using (var br = new BinaryReader(s)) //always reads little endian
            {
                var startPos = br.BaseStream.Position;
                if (br.ReadByte() != 'g' || br.ReadByte() != 'l' || br.ReadByte() != 'T' || br.ReadByte() != 'F')
                {
                    throw new MeshSerializerException("invalid glb magic");
                }
                UInt32 ver = br.ReadUInt32();
                if (ver != 2)
                {
                    throw new MeshSerializerException("invalid glb version: " + ver);
                }
                UInt32 len = br.ReadUInt32();
                byte[] jsonChunk = null, binChunk = null;
                while (br.BaseStream.Position - startPos < len)
                {
                    UInt32 chunkLen = br.ReadUInt32();
                    if (chunkLen > int.MaxValue)
                    {
                        throw new MeshSerializerException("unsupported glb chunk length: " + chunkLen);
                    }
                    string chunkType = Encoding.ASCII.GetString(br.ReadBytes(4));
                    switch (chunkType)
                    {
                        case "JSON":
                        {
                            if (jsonChunk != null)
                            {
                                throw new MeshSerializerException("more than one JSON chunk in glb");
                            }
                            jsonChunk = br.ReadBytes((int)chunkLen);
                            break;
                        }
                        case "BIN\0":
                        {
                            if (binChunk != null)
                            {
                                throw new MeshSerializerException("more than one BIN chunk in glb");
                            }
                            binChunk = br.ReadBytes((int)chunkLen);
                            break;
                        }
                        default: throw new MeshSerializerException("invalid glb chunk type: " + chunkType);
                    }
                }
                var gltf = GLTFFile.FromJson(Encoding.ASCII.GetString(jsonChunk));
                if (binChunk != null)
                {
                    gltf.Data = binChunk;
                }
                return gltf.Decode(imageHandler, indexHandler);
            }
        }
    }
}
