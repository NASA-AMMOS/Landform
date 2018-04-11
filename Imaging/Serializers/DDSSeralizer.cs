using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeximpNet;
using TeximpNet.DDS;
using TeximpNet.Compression;
using OPS.Util;

namespace OPS.Imaging
{
    /// <summary>
    /// Note possible compressor libraries
    /// Crunch https://github.com/BinomialLLC/crunch
    /// TeximpNet https://bitbucket.org/Starnick/teximpnet
    /// Managed Squish https://www.nuget.org/packages/ManagedSquish/
    /// </summary>
    public class DDSSeralizer : IImageSeralizer
    {
        public Image Read(string filename, IImageConverter converter, float[] fillValue = null)
        {
            Image img = null;
            using (Surface surf = Surface.LoadFromFile(filename))
            {
                TemporaryFile.GetAndDelete(".tif", f =>
                {
                    surf.SaveToFile(ImageFormat.TIFF, f);
                    GDALSeralizer ser = new GDALSeralizer();
                    img = ser.Read(f, converter, fillValue);

                });
            }
            return img;
        }

        public void Write<T>(string filename, Image image, IImageConverter converter, float[] fillValue = null)
        {
            
            if (File.Exists(filename))
            {
                File.Delete(filename);
            }
            if (typeof(T) != typeof(byte))
            {
                throw new Exception("Unsuported type for DDS conversion");
            }
            GDALSeralizer ser = new GDALSeralizer();
            TemporaryFile.GetAndDelete(".tif", f =>
            {
                // This will also handle whatever conversion is necessary so we don't need to call converter.Convert
                ser.Write<T>(f, image, converter, fillValue);
                using (Surface surf = Surface.LoadFromFile(f))
                {
                    surf.FlipVertically();
                    using (Compressor comp = new Compressor())
                    {
                        if (image.Bands == 4)
                        {
                            comp.Compression.Format = CompressionFormat.DXT5;   // With alpha
                        }
                        else
                        {
                            comp.Compression.Format = CompressionFormat.DXT1;   // Without alpha
                        }
                        comp.Input.GenerateMipmaps = true;
                        comp.Input.SetData(surf);
                        using (Stream outstream = new FileStream(filename, FileMode.Create, FileAccess.Write))
                        {

                            if (!comp.Process(outstream))
                            {
                                throw new Exception("Error compressing DDS");
                            }
                        }
                    }
                }
            });
        }
    }
}
