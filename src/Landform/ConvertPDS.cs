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
/// Also see ConvertIV.  If you have a directory of pairs *RAS*.iv / *RAS*.IMG you can run convert-pds first to
/// convert the IMG files to png, and then convert-iv will use those to texture the converted meshes.
///
/// Currently the only output datatype supported is byte.  Integer input data will be downconverted to byte and
/// optionally converted from linear to sRGB.  Floating point input data will be normalized and converted to byte, see
/// --floatrange.  Support for other output types, such as float TIFF and 16 bit PNG, is TODO in this utility.
///
/// Examples:
///
///  LandformUtil.exe convert-pds out/ras/foo.IMG
///  LandformUtil.exe convert-pds out/ras/foo.VIC
///  LandformUtil.exe convert-pds out/ras/
///  LandformUtil.exe convert-pds out/xyz/ --invalidpixel="0,0,0"
///  LandformUtil.exe convert-pds out/uvw/ --invalidpixel="0,0,0" --floatrange="-1,1"
///
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

        [Option(Required = false, Default = null, HelpText = "For input images with floating point data, normalize all bands from this range to [0, 1].  Must be a comma separated list of two numbers.  E.g. if the input image is a UVW normals product use --floatrange=\"-1,1\".  Default is to normalize each band independently based on the min/max in the input data for that band.")]
        public string FloatRange { get; set; }

        [Option(Required = false, Default = null, HelpText = "Invalid pixel value to set image mask, comma separated list of floats.  E.g. if the input image is an XYZ or UVW product use --invalidpixel=\"0,0,0\"")]
        public string InvalidPixel { get; set; }
    }

    public class ConvertPDS
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(ConvertPDS));

        private ConvertPDSOptions options;

        private float[] floatRange, invalidPixel;

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

                if (!string.IsNullOrEmpty(options.FloatRange))
                {
                    floatRange = StringHelper.ParseFloatListSafe(options.FloatRange);
                    if (floatRange == null || floatRange.Length != 2)
                    {
                        throw new Exception($"error parsing --floatrange=\"{options.FloatRange}\"");
                    }
                }

                if (!string.IsNullOrEmpty(options.InvalidPixel))
                {
                    invalidPixel = StringHelper.ParseFloatListSafe(options.InvalidPixel);
                    if (invalidPixel == null)
                    {
                        throw new Exception($"error parsing --invalidpixel=\"{options.InvalidPixel}\"");
                    }
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
                            logger.InfoFormat("converting {0} to {1} in {2}", files[i], ext,
                                              string.IsNullOrEmpty(destDir) ? "current directory" : destDir);
                            //default read conversion will normalize integer data to 0.0-1.0
                            //but will pass through float data
                            Image img = Image.Load(files[i]);
                            if (invalidPixel != null)
                            {
                                if (invalidPixel.Length == img.Bands)
                                {
                                    logger.InfoFormat("masking invalid pixels with value {0} in {1}",
                                                      options.InvalidPixel, files[i]);
                                    img.CreateMask(invalidPixel);
                                }
                                else
                                {
                                    logger.WarnFormat("not masking invalid pixels, {0} has {1} bands, expected {2}",
                                                      files[i], img.Bands, invalidPixel.Length);
                                }
                            }
                            var md = img.Metadata as PDSMetadata;
                            if (md != null)
                            {
                                if (md.SampleType == typeof(float) || md.SampleType == typeof(double))
                                {
                                    if (floatRange != null)
                                    {
                                        logger.InfoFormat("normalizing float image {0} from [{1}, {2}] to [0, 1]",
                                                          files[i], floatRange[0], floatRange[1]);
                                        img.Normalize(floatRange[0], floatRange[1]);
                                    }
                                    else
                                    {
                                        logger.InfoFormat("normalizing float image {0} to [0, 1]", files[i]);
                                        img.Normalize();
                                    }
                                }
                            }
                            else
                            {
                                logger.WarnFormat("{0} does not seem to be a PDS format image", files[i]);
                            }
                            string dst = Path.Combine(destDir, bn + ext); //destDir="" ok
                            if (options.NoConvertLinearRGBTosRGB)
                            {
                                img.Save<byte>(dst);
                            }
                            else
                            {
                                img.Save<byte>(dst, ImageConverters.NormalizedImageLinearRGBToValueRangeSRGB);
                            }
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
