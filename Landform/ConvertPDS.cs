using System;
using System.IO;
using System.Linq;
using CommandLine;
using log4net;
using OPS.Imaging;

namespace OPS.Landform
{

    [Verb("convert-pds", HelpText = "Convert PDS images to different format")]
    public class ConvertPDSOptions
    {
        [Value(0, Required = true, HelpText = "Path to IMG file or directory to be converted")]
        public string Inputpath { get; set; }

        [Option("output", Required = false, HelpText = "Output directory, omit to use same directory as input")]
        public string OutputDir { get; set; }

        [Option("type", Required = false, Default = "png", HelpText = "Output file type (jpg, png, tif)")]
        public string OutputType { get; set; }
    }

    public class ConvertPDS
    {
        private ConvertPDSOptions options;

        private static readonly ILog logger = LogManager.GetLogger(typeof(ConvertPDS));

        public ConvertPDS(ConvertPDSOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            string[] allowedFormats = new string[] { "jpg", "png", "tif" };

            if (!allowedFormats.Any(f => f == options.OutputType))
            {
                logger.ErrorFormat("unrecognized output type \"{0}\"", options.OutputType);
                return 1;
            }

            string[] files = null;
            string destDir = null;

            if (Directory.Exists(options.Inputpath))
            {
                files = Directory.GetFiles(options.Inputpath, "*.IMG");
                destDir = options.Inputpath;
            }
            else
            {
                files = new string[] {  options.Inputpath };
                destDir = Path.GetDirectoryName(options.Inputpath); //destDir="" if Inputpath was a bare filename
            }

            if (options.OutputDir != null)
            {
                destDir = options.OutputDir;
            }

            if (files != null && files.Length > 0)
            {

                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                string ext = "." + options.OutputType;
                for (int i = 0; i < files.Length; i++)
                {
                    logger.InfoFormat("converting {0} to {1} in {2}", files[i], ext, destDir);
                    string bn = Path.GetFileNameWithoutExtension(files[i]);
                    Image.Load(files[i]).Save<byte>(Path.Combine(destDir, bn + ext)); //destDir="" ok
                }          
            }

            return 0;
        }
    }
}
