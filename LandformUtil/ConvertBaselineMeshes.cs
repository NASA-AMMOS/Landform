using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using CommandLine;
using Microsoft.Xna.Framework;
using log4net;
using OPS.Util;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline;

namespace OPS.LandformUtil
{
    [Verb("convertbaselinemeshes", HelpText = "Converts a directory of basline meshes in open inventor format into something else")]
    public class ConvertBaselineMeshesOptions
    {
        [Value(0, Required = true, HelpText = "Directory of input meshes")]
        public string InputDir { get; set; }

        [Value(1, Required = true, HelpText = "Output directory")]
        public string OutputDir { get; set; }

        [Option(Required = true, HelpText = "Extension to use for output meshes", Default = "ply")]
        public string MeshExt { get; set; }

        [Option(Required = true, HelpText = "Extension to use for output images", Default = "jpg")]
        public string ImageExt { get; set; }

        [Option(HelpText = "Convert meshes to unity frame", Default = false)]
        public bool UseUnityFrame { get; set; }

        [Option(HelpText = "Decimate mesh to reduce number of faces by this ratio.  Default of 1 means no decimation.", Default = 1)]
        public float Decimate { get; set; }
    }

    public class ConvertBaselineMeshes
    {
        static ILog logger = LogManager.GetLogger(typeof(ConvertBaselineMeshes));

        ConvertBaselineMeshesOptions options;

        public ConvertBaselineMeshes(ConvertBaselineMeshesOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            PathHelper.EnsureExists(options.OutputDir);
            // Loop through input directory and find open inventor files
            foreach (string inputMesh in Directory.EnumerateFiles(options.InputDir, "*.iv"))
            {
                if (!File.Exists(inputMesh))
                {
                    logger.Debug(Path.GetFileName(inputMesh) + " skip");
                    continue;
                }
                else
                {
                    logger.Debug(Path.GetFileName(inputMesh) + " convert");
                }
                // Construct options to call conversion for each mesh
                ConvertBaselineMeshOptions opts = new ConvertBaselineMeshOptions();
                opts.InputMesh = inputMesh;
                opts.OutputMesh = PathHelper.ChangeDirectory(inputMesh, options.OutputDir, options.MeshExt);
                string inputImage = TileBaselineMeshes.FindHighestVersionImage(inputMesh.Replace(".iv", ".IMG"));
                if (File.Exists(inputImage))
                {
                    logger.Debug(Path.GetFileName(inputImage) + " found image");
                    opts.InputImage = inputImage;
                    opts.OutputImage = PathHelper.ChangeDirectory(inputImage, options.OutputDir, options.ImageExt);
                }
                string armImage = TileBaselineMeshes.FindHighestVersionImage(inputMesh.Replace(".iv", ".IMG").Replace("RASL", "ARML"));
                if (File.Exists(armImage))
                {
                    logger.Debug(Path.GetFileName(inputImage) + " found reach");
                    opts.ReachImage = armImage;
                }
                opts.Decimate = options.Decimate;
                opts.UseUnityFrame = options.UseUnityFrame;
                int r = new ConvertBaselineMesh(opts).Run();
                // If we didn't succeed exit with the failed return code
                if (r != 0)
                {
                    return r;
                }
            }
            return 0;
        }
    }
}
