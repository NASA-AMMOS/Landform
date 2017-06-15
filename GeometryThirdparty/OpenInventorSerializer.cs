using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Util;
using System.IO;

namespace OPS.Geometry
{
    /// <summary>
    /// A minimalist open inventor reader that supports reading simple rover meshes
    /// </summary>
    public class OpenInventorSerializer : MeshSerializer
    {
        /// <summary>
        /// Read all LODs out of an iv file
        /// LODs are sorted from heighest quality to lowest quality
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static List<Mesh> ReadAllLODs(string filename)
        {
            List<Mesh> result = null;
            TemporaryFile.GetAndDelete("tmp.iva", tmpFile =>
            {
                ConvertToAscii(filename, tmpFile);
                var parser = new OpenInventorParser(tmpFile);
                result = parser.Parse();
            });
            return result;
        }

        /// <summary>
        /// Return only the highest resolution LOD in the file
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static Mesh ReadBestLOD(string filename)
        {
            var lods = ReadAllLODs(filename);
            return lods[0];
        }

        static void ConvertToAscii(string binFilename, string asciiFilename)
        {
            string exe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", "ivcat.exe");
            string args = string.Format("-o \"{0}\" \"{1}\"", asciiFilename, binFilename);
            new ProgramRunner(exe, args).Run();
        }

        public override Mesh Load(string filename)
        {
            return OpenInventorSerializer.ReadBestLOD(filename);
        }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            throw new NotImplementedException();
        }

        public override string GetExtension()
        {
            return ".iv";
        }
    }


}
