using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json;
using JPLOPS.Geometry.GLTF;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Reads and writes 3DTiles batched 3D model files, version 1.
    /// Only supports b3dm files containing a single binary GLTF (GLB) file in the subset supported by GLBSerializer.
    /// https://github.com/AnalyticalGraphicsInc/3d-tiles/tree/master/specification/TileFormats/Batched3DModel
    /// </summary>
    public class B3DMSerializer : MeshSerializer
    {
        public override string GetExtension()
        {
            return ".b3dm";
        }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            Save(m, filename, imageFilename, null);
        }

        public static void Save(Mesh m, string filename, string imageFilename, string indexFilename)
        {
            byte[] glbData = null;
            using (var ms = new MemoryStream())
            {
                GLBSerializer.WriteToStream(ms, m, imageFilename, indexFilename);
                glbData = ms.ToArray();
            }

            byte[] featureTableJson = Encoding.ASCII.GetBytes(@"{""BATCH_LENGTH"":0}  ");

            if (featureTableJson.Length % 4 != 0 || glbData.Length % 4 != 0)
            {
                throw new Exception("unexpected byte alignment");
            }

            using (var fs = new FileStream(filename, FileMode.Create))
            {
                using (var bw = new BinaryWriter(fs))
                {
                    // b3dm magic number
                    //bw.Write(GLTFFile.UIntBytes(0x6D643362));
                    bw.Write(Encoding.ASCII.GetBytes("b3dm"));

                    // version
                    bw.Write(GLTFFile.UIntBytes(1));                                                         

                    // total byte length including header
                    int headerBytes = 28;
                    int totalLength = headerBytes + featureTableJson.Length + glbData.Length;
                    bw.Write(GLTFFile.UIntBytes(totalLength));     

                    // feature table header
                    bw.Write(GLTFFile.UIntBytes(featureTableJson.Length)); // json length
                    bw.Write(GLTFFile.UIntBytes(0)); // binary length

                    // batch table header
                    bw.Write(GLTFFile.UIntBytes(0)); // json length
                    bw.Write(GLTFFile.UIntBytes(0)); // binary length

                    // --- end of header ---

                    // feature table
                    bw.Write(featureTableJson);

                    // skip batch table since its empty

                    // binary gltf data
                    bw.Write(glbData);
                }
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
                using (var br = new BinaryReader(fs)) //always reads little endian
                {
                    if (br.ReadByte() != 'b' || br.ReadByte() != '3' || br.ReadByte() != 'd' || br.ReadByte() != 'm')
                    {
                        throw new MeshSerializerException("invalid b3dm magic");
                    }
                    UInt32 ver = br.ReadUInt32();
                    if (ver != 1)
                    {
                        throw new MeshSerializerException("invalid b3dm version: " + ver);
                    }
                    UInt32 totalLength = br.ReadUInt32();
                    UInt32 featureTableJsonLength = br.ReadUInt32();
                    UInt32 featureTableBinaryLength = br.ReadUInt32();
                    UInt32 batchTableJsonLength = br.ReadUInt32();
                    UInt32 batchTableBinaryLength = br.ReadUInt32();
                    if (featureTableBinaryLength > 0 || batchTableJsonLength > 0 || batchTableBinaryLength > 0)
                    {
                        throw new MeshSerializerException("unsupported b3dm file");
                    }
                    if (featureTableJsonLength > int.MaxValue || totalLength < 3 * 4 + featureTableJsonLength)
                    {
                        throw new MeshSerializerException("invalid b3dm length");
                    }
                    string featureTableJson = Encoding.ASCII.GetString(br.ReadBytes((int)featureTableJsonLength));
                    var featureTable = JsonConvert.DeserializeObject<Dictionary<string, string>>(featureTableJson);
                    if (featureTable.Count != 1 || !featureTable.ContainsKey("BATCH_LENGTH") ||
                        !int.TryParse(featureTable["BATCH_LENGTH"], out int bl) || bl != 0)
                    {
                        throw new MeshSerializerException("invalid b3dm batch table");
                    }
                    return GLBSerializer.ReadFromStream(fs, imageHandler, indexHandler);
                }
            }
        }
    }
}
