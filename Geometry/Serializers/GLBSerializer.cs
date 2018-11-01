using Newtonsoft.Json;
using OPS.Geometry.GLTF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    /// <summary>
    /// Saves a binary gltf file
    /// https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#binary-gltf-layout
    /// </summary>
    public class GLBSerializer : MeshSerializer
    {
        public override string GetExtension()
        {
            return ".glb";
        }

        public override Mesh Load(string filename)
        {
            throw new NotImplementedException();
        }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Create))
            {
                WriteToStream(fs, m, imageFilename);
            }
        }

        public static void WriteToStream(Stream s, Mesh m, string imageFilename)
        {
            using (BinaryWriter bw = new BinaryWriter(s))
            {
                GLTFFile f = new GLTFFile(m, imageFilename, false);
                JsonSerializerSettings settings = new JsonSerializerSettings()
                {
                    NullValueHandling = NullValueHandling.Ignore
                };
                string jsonContent = JsonConvert.SerializeObject(f, Formatting.None, settings);
                // Must be 4 byte aligned so pad with spaces
                if(jsonContent.Length % 4 != 0)
                {
                    jsonContent += new string(' ', 4 - (jsonContent.Length % 4));
                }
                byte[] jsonBytes = Encoding.ASCII.GetBytes(jsonContent);

                // 7 UInt32's used in the header
                UInt32 length = (UInt32) (sizeof(UInt32) * 7 + jsonBytes.Length + f.Data.Length);

                // Header
                bw.Write((UInt32) 0x46546C67); // gltf
                bw.Write((UInt32) 2); // version 2
                bw.Write((UInt32) length); // total length of file

                // Json Chunk
                bw.Write((UInt32) jsonBytes.Length); // Length of json in bytes
                bw.Write((UInt32) 0x4E4F534A); // JSON chunk type
                bw.Write(jsonBytes); // json data

                // binary chunk                     
                bw.Write((UInt32) f.Data.Length); // Length of binary data in bytes
                bw.Write((UInt32) 0x004E4942); // Binary chunk type
                bw.Write(f.Data); // Write binary data
            }
        }

    }
}
