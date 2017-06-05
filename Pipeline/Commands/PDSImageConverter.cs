using CommandLine;
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using Microsoft.Xna.Framework;
using Emgu.CV.XFeatures2D;
using Emgu.CV.Util;
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

            if (directory)
            {
                images = Directory.GetFiles(options.ImagePath, "*.IMG");
            }
            else if(options.ImagePath.EndsWith(".IMG"))
            {
                images = new string[] {  options.ImagePath };
            }

            if (images.Length == 0 || !allowedFormats.Contains(options.OutputType)) { return 0; }

            string destPath = Directory.GetParent(options.ImagePath).FullName + " Output";
            destPath = options.OutputPath != null ? options.OutputPath : destPath;
            string outputType = options.OutputType != null ? options.OutputType : "png";

            Directory.CreateDirectory(destPath);
            for (int i = 0; i < images.Length; i++)
            {
                string imagePath = images[i];
                Image newImage = Image.Load(imagePath);
                string imageName = Path.GetFileName(imagePath); // remove .IMG extension
                string newImageName = imageName.Substring(0, imageName.Length - 4) + '.' + outputType;
                newImage.Save<byte>(destPath + '\\' + newImageName);
                Debug.WriteLine("destPath: " + destPath + '\\' + Path.GetFileName(imagePath));
                Debug.WriteLine("processed image: " + imagePath);
            }
           
            return 1;
        }
    }
}
