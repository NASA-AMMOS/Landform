using System;
using System.IO;
using System.Linq;
using CommandLine;
using Microsoft.Xna.Framework;
using log4net;
using JPLOPS.Util;
using JPLOPS.Imaging;

/// <summary>
/// Utility to convert PDS images to other formats.
///
/// Can operate on a single image or a directory containing multiple images.
///
/// Also see ConvertIV.  If you have a directory of pairs *RASL*.iv / *RASL*.IMG you can run convert-pds first to
/// convert the IMG files to png, and then convert-iv will use those to texture the converted meshes.
///
/// Example:
///
///  LandformUtil.exe convert-pds out/windjana/meshes
/// </summary>
namespace JPLOPS.Landform
{

    [Verb("convert-pds", HelpText = "Convert PDS images to different format")]
    public class ConvertPDSOptions
    {
        [Value(0, Required = true, HelpText = "Path to IMG file or directory to be converted")]
        public string Inputpath { get; set; }

        [Option(Required = false, HelpText = "Output directory, omit to use same directory as input")]
        public string OutputPath { get; set; }

        [Option(Required = false, Default = "png", HelpText = "Output file type (jpg, png, tif)")]
        public string OutputType { get; set; }

        [Option(Required = false, Default = null, HelpText = "Just sample one pixel, format ROW,COL")]
        public string Sample { get; set; }

        [Option(Required = false, Default = false, HelpText = "Disable RGB -> sRGB (gamma) conversion (conversion is never performed for single pixel samples)")]
        public bool NoConvertLinearRGBTosRGB { get; set; }
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
            try
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
                    var pdsFiles = Directory.GetFiles(options.Inputpath, "*.IMG").ToList();
                    pdsFiles.AddRange(Directory.GetFiles(options.Inputpath, "*.VIC"));
                    files = pdsFiles.ToArray();
                    destDir = options.Inputpath;
                }
                else
                {
                    files = new string[] {  options.Inputpath };
                    destDir = Path.GetDirectoryName(options.Inputpath); //destDir="" if Inputpath was a bare filename
                }
                
                if (options.OutputPath != null)
                {
                    destDir = options.OutputPath;
                }

                Vector2? sample = null;
                if (options.Sample != null)
                {
                    string[] coords = options.Sample.Split(',');
                    sample = new Vector2(float.Parse(coords[1].Trim()), float.Parse(coords[0].Trim()));
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
                        string bn = Path.GetFileNameWithoutExtension(files[i]);
                        if (sample.HasValue)
                        {
                            Image img = Image.Load(files[i], ImageConverters.PassThrough);
                            float r = (float)(sample.Value.Y);
                            float c = (float)(sample.Value.X);
                            float[] val = new float[img.Bands];
                            for (int b = 0; b < img.Bands; b++)
                            {
                                val[b] = img.BilinearSample(b, r, c);
                            }
                            Console.WriteLine("pixel at row={0}, col={1} has value [{2}]",
                                              r, c, string.Join(", ", val));
                        }
                        else
                        {
                            logger.InfoFormat("converting {0} to {1} in {2}", files[i], ext, destDir);
                            Image img = Image.Load(files[i]);
                            if (!options.NoConvertLinearRGBTosRGB)
                            {
                                img = img.LinearRGBToSRGB();
                            }
                            img.Save<byte>(Path.Combine(destDir, bn + ext)); //destDir="" ok
                        }
                    }          
                }
            }
            catch (Exception ex)
            {
                Logging.LogException(logger, ex);
                return 1;
            }

            return 0;
        }
    }
}
