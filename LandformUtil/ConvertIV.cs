using System;
using System.IO;
using System.Linq;
using CommandLine;
using OPS.Geometry;
using OPS.Imaging;

namespace OPS.LandformUtil
{
    [Verb("convert-iv", HelpText = "Convert IV meshes to different format")]
    public class ConvertIVOptions
    {
        [Value(0, Required = true, HelpText = "Path to file or directory to be converted")]
        public string InputPath { get; set; }

        [Option("texture", Required = false, HelpText = "Texture image file")]
        public string TextureFile { get; set; }

        [Option("all-lods", Required = false, HelpText = "Convert all LODs")]
        public bool AllLODs { get; set; }

        [Option("output", Required = false, HelpText = "Output path, omit to use same directory as input")]
        public string OutputPath { get; set; }

        [Option("type", Required = false, Default = "ply", HelpText = "Output file type (ply, obj)")]
        public string OutputType { get; set; }
    }

    public class ConvertIV
    {
        private ConvertIVOptions options;

        public ConvertIV(ConvertIVOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            string[] allowedFormats = new string[] { "ply", "obj" };

            if (!allowedFormats.Any(f => f == options.OutputType))
            {
                Console.Error.WriteLine("unrecognized output type \"{0}\"", options.OutputType);
                return 1;
            }

            string[] files = null;
            string destPath = null;

            if (Directory.Exists(options.InputPath))
            {
                files = Directory.GetFiles(options.InputPath, "*.iv");
                destPath = options.InputPath;
            }
            else
            {
                files = new string[] {  options.InputPath };
                destPath = Path.GetDirectoryName(options.InputPath); //destPath="" if InputPath was a bare filename
            }

            if (options.OutputPath != null)
            {
                destPath = options.OutputPath;
            }

            if (files != null && files.Length > 0)
            {

                if (!string.IsNullOrEmpty(destPath))
                {
                    Directory.CreateDirectory(destPath);
                }

                string ext = "." + options.OutputType;
                string tf = options.TextureFile;
                for (int i = 0; i < files.Length; i++)
                {
                    string bn = Path.GetFileNameWithoutExtension(files[i]);
                    if (options.AllLODs)
                    {
                        var lodMeshes = Mesh.LoadAllLODs(files[i]);
                        for (int lod = 0; lod < lodMeshes.Count; lod++)
                        {
                            string dest = string.Format("{0}_LOD{1}{2}", bn, lod, ext);
                            lodMeshes[lod].Save(Path.Combine(destPath, dest), tf); //destPath="" ok
                        }
                    }
                    else
                    {
                        Mesh.Load(files[i]).Save(Path.Combine(destPath, bn + ext), tf); //destPath="" ok
                    }
                }          
            }

            return 0;
        }
    }
}
