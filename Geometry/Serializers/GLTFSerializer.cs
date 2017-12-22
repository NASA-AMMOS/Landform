using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry.GLTF;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;
using Newtonsoft.Json;
using System.IO;
using OPS.Util;
using OPS.Imaging;

namespace OPS.Geometry
{
    /// <summary>
    /// Class for writing gltf files that consit of a single mesh and texture with default material
    /// </summary>
    public class GLTFSerializer : MeshSerializer
    {
        public GLTFSerializer() { }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            GLTFSerializer.Write(m, filename, imageFilename);
        }

        public override Mesh Load(string filename)
        {
            throw new NotImplementedException();
        }

        public override string GetExtension()
        {
            return ".gltf";
        }

        public static void Write(Mesh m, string filename, string imageFilename = null)
        {
            GLTFFile f = new GLTFFile(m, imageFilename);
            JsonSerializerSettings settings = new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Ignore
            };
            File.WriteAllText(filename, JsonConvert.SerializeObject(f, Formatting.Indented, settings),new UTF8Encoding(false));
        }
    }

    

}
