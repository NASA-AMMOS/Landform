using System;
using System.IO;
using System.Linq;
using CommandLine;
using OPS.Imaging;

namespace OPS.LandformUtil
{

    [Verb("convert-pds", HelpText = "Convert PDS images to different format")]
    public class ConvertPDSOptions
    {
        [Value(0, Required = true, HelpText = "Path to IMG file or directory to be converted")]
        public string Inputpath { get; set; }

        [Option("output", Required = false, HelpText = "Output path, omit to use same directory as input")]
        public string OutputPath { get; set; }

        [Option("type", Required = false, Default = "png", HelpText = "Output file type (jpg, png, tif)")]
        public string OutputType { get; set; }
    }

    public class ConvertPDS
    {
        private ConvertPDSOptions options;

        public ConvertPDS(ConvertPDSOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            string[] allowedFormats = new string[] { "jpg", "png", "tif" };

            if (!allowedFormats.Any(f => f == options.OutputType))
            {
                Console.Error.WriteLine("unrecognized output type \"{0}\"", options.OutputType);
                return 1;
            }

            string[] files = null;
            string destPath = null;

            if (Directory.Exists(options.Inputpath))
            {
                files = Directory.GetFiles(options.Inputpath, "*.IMG");
                destPath = options.Inputpath;
            }
            else
            {
                files = new string[] {  options.Inputpath };
                destPath = Path.GetDirectoryName(options.Inputpath); //destPath="" if Inputpath was a bare filename
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
                for (int i = 0; i < files.Length; i++)
                {
                    string bn = Path.GetFileNameWithoutExtension(files[i]);
                    Image.Load(files[i]).Save<byte>(Path.Combine(destPath, bn + ext)); //destPath="" ok
                }          
            }

            return 0;
        }
    }
}
