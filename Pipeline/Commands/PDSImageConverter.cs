using CommandLine;
using System.IO;
using System.Diagnostics;
using System.Linq;
using OPS.Imaging;

namespace OPS.Pipeline
{

    [Verb("convertpds", HelpText = "Convert PDS images to different format")]
    public class PDSImageConverterOptions
    {
        [Option('t', "type", Required = false, HelpText = "Output file type, available types: jpeg, tiff, default is png")]
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
            string[] allowedFormats = new string[] { "jpeg", "png", "tiff" };
            string outputType = options.OutputType != null ? options.OutputType : "png";
            string destPath = "";

            if (directory)
            {
                images = Directory.GetFiles(options.ImagePath, "*.IMG");
                destPath = options.ImagePath + " Output";
            }
            else if(options.ImagePath.EndsWith(".IMG"))
            {
                images = new string[] {  options.ImagePath };
                destPath = Directory.GetParent(options.ImagePath).FullName + " Output";
            }

            if (images.Length == 0) { return 0; }

            Directory.CreateDirectory(destPath);
            for (int i = 0; i < images.Length; i++)
            {
                string imagePath = images[i];
                Image newImage = Image.Load(imagePath);
                string imageName = Path.GetFileName(imagePath); // remove .IMG extension
                string newImageName = imageName.Substring(0, imageName.Length - 4) + '.' + outputType;
                newImage.Save<byte>(destPath + '\\' + newImageName);
                Debug.WriteLine("destPath: " + destPath + '\\' + newImageName);
                Debug.WriteLine("processed image: " + imagePath);
            }
           
            return 1;
        }
    }
}
