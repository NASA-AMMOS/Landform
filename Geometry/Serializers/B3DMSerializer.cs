using System;
using System.IO;
using System.Text;
using OPS.Geometry.GLTF;

namespace OPS.Geometry
{
    /// <summary>
    /// Writes b3dm files for use with 3D Tiles specification
    /// https://github.com/AnalyticalGraphicsInc/3d-tiles/tree/master/specification/TileFormats/Batched3DModel
    /// </summary>
    public class B3DMSerializer : MeshSerializer
    {
        public override string GetExtension()
        {
            return ".b3dm";
        }

        public override Mesh Load(string filename)
        {
            throw new NotImplementedException();
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
                    UInt32 totalLength = (UInt32)(headerBytes + featureTableJson.Length + glbData.Length);
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
    }
}
