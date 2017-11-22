using log4net;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    /// <summary>
    /// Class for saving files using the draco compressed mesh format.  Uses the cli draco encoder and decoder compiled from here
    /// https://github.com/google/draco
    /// </summary>
    public class DracoSerializer : MeshSerializer
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(DracoSerializer));

        public override string GetExtension()
        {
            return ".drc";
        }

        public override Mesh Load(string filename)
        {
            Mesh m = null;
            string dracoExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", "draco_decoder.exe");
            TemporaryFile.GetAndDelete(".obj", outputMesh =>
            {
                ProgramRunner pr = new ProgramRunner(dracoExe, string.Format("-i \"{0}\" -o \"{1}\"", filename, outputMesh));
                pr.Run();
                if (!File.Exists(outputMesh))
                {
                    logger.Error(pr.OutputText);
                    logger.Error(pr.ErrorText);
                }
                m = Mesh.Load(outputMesh);
            });
            return m;
        }

        public override void Save(Mesh m, string filename, string imageFilename)
        {
            SaveMesh(m, filename);
        }

        public static void SaveMesh(Mesh m, string filename, int positionBits = 14, int textureBits = 12, int normalBits = 10, int genericBits  = 8, int compressionLevel = 7)
        {
            string dracoExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", "draco_encoder.exe");
            TemporaryFile.GetAndDelete(".obj", inputMesh =>
            {
                string arguments = " ";
                if (m.Faces.Count == 0)
                {
                    arguments += "-point_cloud ";
                }
                arguments += string.Format("-qp {0} -qt {1} -qn {2} -qg {3} -cl {4}", positionBits, textureBits,normalBits, genericBits, compressionLevel);


                m.Save(inputMesh);
                ProgramRunner pr = new ProgramRunner(dracoExe, string.Format("-i \"{0}\" -o \"{1}\" {2}", inputMesh, filename, arguments));
                pr.Run();
                if (!File.Exists(filename))
                {
                    logger.Error(pr.OutputText);
                    logger.Error(pr.ErrorText);
                }
            });
        }
    }
}
