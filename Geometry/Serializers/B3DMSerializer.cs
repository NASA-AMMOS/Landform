using System;
using System.IO;
using System.Text;

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

        class BatchTable
        {
            public int BATCH_LENGTH = 0;
        }

        private const int HEADER_SIZE = 28;

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            byte[] glbData = null;
            using (MemoryStream ms = new MemoryStream())
            {
                GLBSerializer.WriteToStream(ms, m, imageFilename);
                glbData = ms.ToArray();
            }

            byte[] featureTableJson = Encoding.ASCII.GetBytes(@"{""BATCH_LENGTH"":0}  ");
            if (featureTableJson.Length % 4 != 0 || glbData.Length % 4 != 0)
            {
                throw new Exception("Unexpected byte alignment");
            }

            using (FileStream fs = new FileStream(filename, FileMode.Create))
            {
                using (BinaryWriter br = new BinaryWriter(fs))
                {
                    // Write Magic b3dm character string
                    br.Write((UInt32)0x6D643362);                                                   
                    // Write version
                    br.Write((UInt32) 1);                                                         
                    // Write total byte length including header
                    UInt32 totalLength = (UInt32)(HEADER_SIZE + featureTableJson.Length + glbData.Length);
                    br.Write((UInt32)totalLength);     
                    // Write feature table 
                    br.Write((UInt32) featureTableJson.Length); // json length
                    br.Write((UInt32) 0);                       // binary length
                    // Write batch table 
                    br.Write((UInt32)0);                        // json length
                    br.Write((UInt32)0);                        // binary length
                    // Write feature table
                    br.Write(featureTableJson);
                    // Skip batch table since its empty
                    // Write binary gltf data
                    br.Write(glbData);
                }
            }
        }
    }
}
