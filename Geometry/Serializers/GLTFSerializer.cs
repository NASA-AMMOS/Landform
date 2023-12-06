using System.IO;
using System.Text;
using JPLOPS.Geometry.GLTF;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Writes and reads glTF files that consist of a single embedded mesh and optional texture with default material.
    /// Also supports an optional index image in addition to the texture image.
    /// https://github.com/KhronosGroup/glTF/tree/master/specification/2.0
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
            return Load(filename, null);
        }

        public static Mesh Load(string filename, GLTFFile.ImageHandler imageHandler,
                                GLTFFile.ImageHandler indexHandler = null)
        {
            return GLTFFile.FromJson(File.ReadAllText(filename)).Decode(imageHandler, indexHandler);
        }

        public override string GetExtension()
        {
            return ".gltf";
        }
    }
}
