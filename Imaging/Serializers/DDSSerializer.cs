using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using log4net;

namespace OPS.Imaging
{
    /// <summary>
    /// Note possible compressor libraries
    /// Crunch https://github.com/BinomialLLC/crunch
    /// TeximpNet https://bitbucket.org/Starnick/teximpnet
    /// Managed Squish https://www.nuget.org/packages/ManagedSquish/
    /// DirectXTexNet https://www.nuget.org/packages/DirectXTexNet/1.0.0-rc2
    /// Texconv https://github.com/Microsoft/DirectXTex/wiki/Texconv
    /// </summary>
    public class DDSSerializer : ImageSerializer
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(DDSSerializer));


        public override Image Read(string filename, IImageConverter converter, float[] fillValue = null)
        {
            Image img = null;
            TemporaryFile.GetAndDelete(".png", f =>
            {
                ProgramRunner pr = RunCruch(string.Format("-file \"{0}\" -out \"{1}\"", filename, f));
                if (!File.Exists(filename))
                {
                    logger.Error(pr.OutputText);
                    logger.Error(pr.ErrorText);
                    throw new Exception("Crunch failed to read DDS");
                }
                GDALSerializer ser = new GDALSerializer();
                img = ser.Read(f, converter, fillValue);
            });
            return img;
        }

        public override void Write<T>(string filename, Image image, IImageConverter converter, float[] fillValue = null)
        {            
            if (File.Exists(filename))
            {
                File.Delete(filename);
            }
            if (typeof(T) != typeof(byte))
            {
                throw new Exception("Unsuported type for DDS conversion");
            }
            string fileFormat = Path.GetExtension(filename).ToLower().Replace(".", "");
            if(fileFormat != "dds" && fileFormat != "crn")
            {
                throw new Exception("Unsupported DDS export extension");
            }
            string encoding = null;
            if(image.Bands == 1 || image.Bands == 3)
            {
                encoding = "dxt1";      // no alpha
            }
            else if(image.Bands ==2 || image.Bands == 4)
            {
                encoding = "dxt5";      // with alpha
            }
            else
            {
                throw new Exception("Unsuported number of bands for DDS conversion");
            }
            
            TemporaryFile.GetAndDelete(".png", inImage =>
            {
                GDALSerializer ser = new GDALSerializer();
                ser.Write<T>(inImage, image, converter, fillValue);
                ProgramRunner pr = RunCruch(string.Format("-file \"{0}\" -fileformat {1} -{2} -out {3}", inImage, fileFormat, encoding, filename));
                if (!File.Exists(filename))
                {
                    logger.Error(pr.OutputText);
                    logger.Error(pr.ErrorText);
                    throw new Exception("Crunch DDS output file not found");
                }               
            });
        }

        ProgramRunner RunCruch(string parameters)
        {
            string crunchExeName = "crunch.exe";     // 32 bit version
            if (IntPtr.Size == 8)
            {
                crunchExeName = "crunch_x64.exe";
            }
            string crunchExe = Path.Combine(PathHelper.GetApplicationPath(), "ExternalApps", crunchExeName);
            ProgramRunner pr = new ProgramRunner(crunchExe, parameters);
            pr.Run();
            return pr;
        }

        public override IImageConverter DefaultReadConverter()
        {
            return ImageConverters.ValueRangeToNormalizedImage;
        }

        public override IImageConverter DefaultWriteConverter()
        {
            return ImageConverters.NormalizedImageToValueRange;
        }

        public override string[] GetExtensions()
        {
            return new string[] { ".dds", ".crn" };
        }
    }
}
