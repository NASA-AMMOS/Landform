using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using CommandLine;
using log4net;
using Newtonsoft.Json;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.MathExtensions;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline;

namespace OPS.LandformUtil
{
    [Verb("tilebaselinemeshes", HelpText = "Converts a directory of basline meshs into 3d tiles")]
    public class TileBaselineMeshesOptions
    {
        [Value(0, Required = true, HelpText = "Input Directory")]
        public string InputDir { get; set; }

        [Value(1, Required = true, HelpText = "Output Directory")]
        public string OutputDir { get; set; }

        [Option(HelpText = "Convert from local level to unity frame")]
        public bool UseUnityFrame { get; set; }
    }

    /// <summary>
    /// Command for batch processing baseline meshes to tiles
    /// </summary>
    public class TileBaselineMeshes
    {
        TileBaselineMeshesOptions options;
        private static readonly ILog logger = LogManager.GetLogger(typeof(TileBaselineMeshes));

        public TileBaselineMeshes(TileBaselineMeshesOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            // Search for all RASL iv files in the direcotry
            foreach (string meshFile in Directory.EnumerateFiles(options.InputDir, "*RASL*.iv"))
            {
                TileBaselineMeshOptions opts = new TileBaselineMeshOptions();
                opts.InputMesh = meshFile;
                opts.OutputDir = Path.Combine(options.OutputDir, Path.GetFileNameWithoutExtension(meshFile));
                // Skip if we have already processed
                if (File.Exists(Path.Combine(opts.OutputDir, "tileset.json")))
                {
                    logger.Debug(meshFile + " skip");
                    continue;
                }
                logger.Debug(meshFile + " process");
                // Use latest image version available
                string imageFile = FindHighestVersionImage(meshFile.Replace(".iv", ".IMG"));
                if (File.Exists(imageFile))
                {
                    logger.Debug(imageFile + " found image");
                    opts.InputImage = imageFile;
                }
                // Use latest reachability version available
                string reachFile = FindHighestVersionImage(meshFile.Replace("RASL", "ARML").Replace(".iv", ".IMG"));
                if (File.Exists(reachFile))
                {
                    logger.Debug(reachFile + " found reach");
                    opts.ReachImage = reachFile;
                }
                opts.UseUnityFrame = options.UseUnityFrame;
                int r = new TileBaselineMesh(opts).Run();
                if (r != 0)
                {
                    return r;
                }
            }
            return 0;
        }

        /// <summary>
        /// Returns the most recent file version that can be found
        /// Only works for versions 0-9
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static string FindHighestVersionImage(string filename)
        {
            string template = filename.Replace(".IMG", "");
            template = template.Substring(0, template.Length - 1);
            string r = null;
            for (int version = 0; version < 9; version++)
            {
                string curname = template + version + ".IMG";
                if (File.Exists(curname))
                {
                    r = curname;
                }
            }
            return r;
        }
    }
}
