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
            DDSContainer c = new DDSContainer();
            // TODO: loop to find largest
            var x = c.MipChains[0];
            MipData y = x[0];
            Surface s = new Surface(y.Data);
            Image img = null;
            TemporaryFile.GetAndDelete("png", f =>
            {
                s.SaveToFile(ImageFormat.PNG, f);
                GDALSeralizer ser = new GDALSeralizer();
                img = ser.Read(f, converter, fillValue);

            });
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
            TemporaryFile.GetAndDelete(".png", f =>
            {
                // This will also handle whatever conversion is necessary so we don't need to call converter.Convert
                ser.Write<T>(f, image, converter, fillValue);
                Surface surf = Surface.LoadFromFile(f);
                surf.FlipVertically();
                Compressor comp = new Compressor();
                if (image.Bands == 4) {
                    comp.Compression.Format = CompressionFormat.DXT5;   // With alpha
                } else
                {
                    comp.Compression.Format = CompressionFormat.DXT1;   // Without alpha
                }
                comp.Input.GenerateMipmaps = true;
                comp.Input.SetData(surf); 
                if (!comp.Process(filename))
                {
                    throw new Exception("Error compressing DDS");
                }
            });
        }
    }
}
