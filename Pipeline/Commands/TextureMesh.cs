using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using OPS.Plumbing;
using log4net;

namespace OPS.Pipeline
{
    [Verb("MSL.Texture", HelpText = "generate textures for a terrain mesh")]
    public class TextureMeshOptions
    {
    }

    class TextureMesh 
    {
        static ILog logger = LogManager.GetLogger(typeof(TextureMesh));

        public TextureMeshOptions options;

        public TextureMesh(TextureMeshOptions opts)
        {
            this.options = opts;
        }

        public int Run()
        {
            logger.Info("Texturing mesh...");
            return 0;
        }
    }
}
