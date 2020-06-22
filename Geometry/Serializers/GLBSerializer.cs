using System;
using System.IO;
using System.Text;
using System.Linq;
using OPS.Geometry.GLTF;

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
                UInt32 totalLength = (UInt32) (headerBytes + jsonBytes.Length + gltf.Data.Length);
                bw.Write(GLTFFile.UIntBytes(totalLength)); // total length of file

                // json chunk
                bw.Write(GLTFFile.UIntBytes(jsonBytes.Length)); // length of json in bytes
                //bw.Write(GLTFFile.UIntBytes(0x4E4F534A)); // json chunk type
                bw.Write(Encoding.ASCII.GetBytes("JSON"));
                bw.Write(jsonBytes); // json data

                // binary chunk                     
                bw.Write(GLTFFile.UIntBytes(gltf.Data.Length)); // length of binary data in bytes
                //bw.Write(GLTFFile.UIntBytes(0x004E4942)); // binary chunk type
                bw.Write(Encoding.ASCII.GetBytes("BIN").Concat(new byte[] { 0 }).ToArray());
                bw.Write(gltf.Data); // binary data
            }
        }
    }
}
