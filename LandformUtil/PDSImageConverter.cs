using System.IO;
using System.Diagnostics;
using System.Linq;
using CommandLine;
using OPS.Imaging;

namespace OPS.LandformUtil
{

    [Verb("convert-pds", HelpText = "Convert PDS images to different format")]
    public class PDSImageConverterOptions
    {
        [Option('t', "type", Required = false, HelpText = "Output file type, available types: jpg, tif, default is png")]
        public string OutputType { get; set; }

        [Value(0, Required = true, HelpText = "Path to file or directory to be converted")]
        public string ImagePath { get; set; }

        [Option('o', "output", Required = false, HelpText = "Output path of converted images")]
        public string OutputPath { get; set; }
    }

    public class PDSImageConverter
    {
        public PDSImageConverterOptions options;
        public PDSImageConverter(PDSImageConverterOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            FileAttributes attr = File.GetAttributes(options.ImagePath);
            bool directory = (attr & FileAttributes.Directory) == FileAttributes.Directory;
            string[] images = new string[0];
            string[] allowedFormats = new string[] { "jpg", "png", "tif" };
            string outputType = options.OutputType != null ? options.OutputType : "png";
            string destPath = null;

            if (directory)
            {
                images = Directory.GetFiles(options.ImagePath, "*.IMG");
                destPath = options.ImagePath;
            }
            else if(options.ImagePath.EndsWith(".IMG"))
            {
                images = new string[] {  options.ImagePath };
                destPath = Path.GetDirectoryName(options.ImagePath); //destPath="" if ImagePath was a bare filename
            }
            if(options.OutputPath != null)
            {
                destPath = options.OutputPath;
            }

            if (images.Length == 0) { return 0; }

            if (!string.IsNullOrEmpty(destPath))
            {
                Directory.CreateDirectory(destPath);
            }

            for (int i = 0; i < images.Length; i++)
            {
                string imagePath = images[i];
                Image newImage = Image.Load(imagePath);
                string imageName = Path.GetFileNameWithoutExtension(imagePath); 
                string newImageName = imageName + '.' + outputType;
                newImage.Save<byte>(Path.Combine(destPath, newImageName)); //destPath="" ok
            }          
            return 0;
        }
    }
}
