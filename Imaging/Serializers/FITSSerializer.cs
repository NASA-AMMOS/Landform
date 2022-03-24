using log4net;
using System;
using nom.tam.fits;


namespace JPLOPS.Imaging
{
    /// <summary>
    /// Class for reading FITS astronomy images
    /// </summary>
    public class FITSSerializer : ImageSerializer
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(FITSSerializer));

        public override Image Read(string filename, IImageConverter converter, float[] fillValue = null, bool useFillValueFromFile = false)
        {
            if (useFillValueFromFile == true)
            {
                throw new NotImplementedException("add support for detecting file based invalid pixels");
            }

            FITSMetadata metadata = new FITSMetadata(filename);
            var f = new nom.tam.fits.Fits(filename, System.IO.FileAccess.Read);

            var hdu = (ImageHDU)f.GetHDU(0);
            var kernel = hdu.Kernel;

            if (kernel.GetType() != typeof(System.Array[]))
            {
                throw new ImageSerializationException("Unsupported FITS kernel type");
            }
            System.Array[] k = (System.Array[])kernel;
            Image img = new Image(1, GetWidth(k), GetHeight(k));
            img.Metadata = metadata;
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    int inverseRow = k.Length - row - 1;
                    img[0, row, col] = GetValue(k, inverseRow, col);
                }
            }
            if (GetImageType(k) == typeof(byte))
            {
                return converter.Convert<byte>(img);
            }
            else if (GetImageType(k) == typeof(short))
            {
                return converter.Convert<short>(img);
            }
            throw new ImageSerializationException("Unexpected FITS image type");
        }

        Type GetImageType(System.Array[] kernel)
        {
            if (kernel[0].GetType() == typeof(byte[]))
            {
                return typeof(byte);
            }
            else if (kernel[0].GetType() == typeof(short[]))
            {
                return typeof(short);
            }
            else
            {
                throw new ImageSerializationException("Unsupported FITs image type");
            }
        }

        float GetValue(System.Array[] kernel, int row, int col)
        {
            if (GetImageType(kernel) == typeof(byte))
            {
                return ((byte[])kernel[row])[col];
            }
            else if (GetImageType(kernel) == typeof(short))
            {
                return ((short[])kernel[row])[col];
            }
            else
            {
                throw new ImageSerializationException("Unsupported FITs image type");
            }
        }

        int GetWidth(System.Array[] kernel)
        {
            if (GetImageType(kernel) == typeof(byte))
            {
                byte[] data = (byte[])kernel[0];
                return data.Length;
            }
            else if (GetImageType(kernel) == typeof(short))
            {
                short[] data = (short[])kernel[0];
                return data.Length;
            }
            else
            {
                throw new ImageSerializationException("Unsupported FITs image type");
            }
        }
        int GetHeight(System.Array[] kernel)
        {
            return kernel.Length;
        }

        public override void Write<T>(string filename, Image image, IImageConverter converter, float[] fillValue = null)
        {
            throw new NotImplementedException("FITs Image write support not implemented");
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
            return new string[] { ".fit", ".fits" };
        }
    }
}
