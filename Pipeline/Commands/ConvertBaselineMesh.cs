using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using OPS.Geometry;
using OPS.Imaging;
using System.IO;
using Microsoft.Xna.Framework;
using System.Diagnostics;
using OPS.Util;
using log4net;

namespace OPS.Pipeline
{
    [Verb("convertbaselinemesh", HelpText = "Converts a basline mesh in open inventor format into obj or ply")]
    public class ConvertBaselineMeshOptions
    {
        [Value(0, Required = true, HelpText = "Filename of baseline mesh")]
        public string InputMesh { get; set; }

        [Value(1, Required = true, HelpText = "Filename of output mesh")]
        public string OutputMesh { get; set; }

        [Option(HelpText = "Input image to use for this mesh")]
        public string InputImage { get; set; }

        [Option(HelpText = "Reachability image file.  If specified this will be used to generate alpha values in the output texture.  Output texture format must be PNG or other that supports alpha.")]
        public string ReachImage { get; set; }

        [Option(HelpText = "Image output filename")]
        public string OutputImage { get; set; }

        [Option(HelpText = "Convert this mesh to unity frame.  Requires an input image with metadat for site drive coordinate conversion", Default = false)]
        public bool UseUnityFrame { get; set; }

        [Option(HelpText = "Decimate mesh to reduce number of faces by this ratio.  Default of 1 means no decimation", Default = 1)]
        public float Decimate { get; set; }
    }

    public class ConvertBaselineMesh
    {
        static ILog logger = LogManager.GetLogger(typeof(ConvertBaselineMesh));

        ConvertBaselineMeshOptions options;

        public ConvertBaselineMesh(ConvertBaselineMeshOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            bool hasPDSInputImage = options.InputImage != null && Path.GetExtension(options.InputImage).ToUpper() == ".IMG";
            if (options.InputImage == null && options.OutputImage != null)
            {
                logger.Error("Cannot create output image without input image");
                return -1;
            }
            if (!hasPDSInputImage && options.UseUnityFrame)
            {
                logger.Error("Cannot convert to unity frame without an IMG file");
                return -1;
            }
            if (!hasPDSInputImage && (options.Decimate != 1 && options.OutputImage != null))
            {
                logger.Error("Cannot use both decimate and an output image without specifying an input IMG file");
                return -1;
            }

            // Read the mesh.  Baseline meshes are stored in site frame
            logger.Info("Loading mesh");
            Mesh m = Mesh.Load(options.InputMesh);
            m.Clean();
            if (!m.HasNormals)
            {
                logger.Info("Computing nomrals");
                m = MeshLab.ComputeNormals(m);
                m.Clean();
            }           
            Image img = null;
            PDSParser parser = null;
            if (options.InputImage != null)
            {
                logger.Info("Loading input image");
                img = Image.Load(options.InputImage);
                parser = new PDSParser(new PDSMetadata(options.InputImage));
            }
            if (options.Decimate != 1)
            {
                int targetFaces = (int)(m.Faces.Count * options.Decimate);
                logger.Info("Decimating with target of " + targetFaces + " faces");
                // Decimate the mesh, this will loose uvs
                m = MeshLab.Decimate(m, targetFaces);
                m.Clean();
                // If we have an input PDS image regenerate the uvs
                if (hasPDSInputImage)
                {
                    logger.Info("Recalculating UVs");
                    // Convert mesh into local level
                    RoverCoordinateSystem.SiteToLocalLevelMesh(m, parser.OriginOffset);
                    // Create an atlaser so that we can uv our decimated mesh.  Camera model is in rover frame.
                    Matrix localLevelToRover = RoverCoordinateSystem.LocalLevelToRover(parser.RoverOriginRotation);
                    BaselineAtlaser atlaser = new BaselineAtlaser(localLevelToRover, img);
                    m = atlaser.GenerateAtlas(m);
                    // Convert the mesh back to site frame
                    RoverCoordinateSystem.LocalLevelToSiteMesh(m, parser.OriginOffset);
                }
            }
            if (options.UseUnityFrame)
            {
                logger.Info("Converting to unity frame");
                // Convert mesh into local level
                RoverCoordinateSystem.SiteToLocalLevelMesh(m, parser.OriginOffset);
                // Convert the mesh to site frame
                RoverCoordinateSystem.LocalLevelToUnityMesh(m);
                m.Clean();
            }

            if (options.OutputImage != null)
            {

                if (options.ReachImage != null)
                {
                    logger.Info("Reading reachability");
                    Image arm = Image.Load(options.ReachImage);
                    img = RoverReachability.GenerateAlphaFromArmImage(img, arm);
                }
                logger.Info("Saving output image");
                img.Save<byte>(options.OutputImage);
            }        
            logger.Info("Saving mesh");
            m.Save(options.OutputMesh, options.OutputImage);
            return 0;
        }
    }

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
