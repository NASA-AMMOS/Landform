using System;
using System.IO;
using System.Text;
using OPS.Geometry.GLTF;

namespace OPS.Geometry
{
    /// <summary>
    /// Class for writing gltf files that consist of a single mesh and texture with default material
    /// </summary>
    public class GLTFSerializer : MeshSerializer
    {
        public GLTFSerializer() { }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            Save(m, filename, imageFilename, null);
        }

        public static void Save(Mesh m, string filename, string imageFilename, string indexFilename)
        {
            var gltf = new GLTFFile(m, imageFilename, indexFilename, embedData: true);
            File.WriteAllText(filename, gltf.ToJson(indent: true), new UTF8Encoding());
        }

        public override Mesh Load(string filename)
        {
            throw new NotImplementedException();
        }

        public override string GetExtension()
        {
            return ".gltf";
        }
    }
}
