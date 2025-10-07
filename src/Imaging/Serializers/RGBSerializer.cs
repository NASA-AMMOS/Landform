using System;
using System.IO;

namespace JPLOPS.Imaging
{
    /// <summary>
    /// Class for reading RGB images 
    /// </summary>
    class RGBSerializer : ImageSerializer
    {
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
            return new string[] { ".rgb" };
        }

        public override Image Read(string filename, IImageConverter converter, float[] fillValue = null, bool useFillValueFromFile = false)
        {
            if (useFillValueFromFile == true)
            {
                throw new NotImplementedException("add support for detecting file based invalid pixels");
            }

            Image img;
            using (BinaryReader br = new BinaryReader(File.Open(filename, FileMode.Open)))
            {
                byte[] magic = br.ReadBytes(2);
                Array.Reverse(magic);
                if (BitConverter.ToInt16(magic, 0) != 474)
                {
                    throw new Exception("RGB magic field mismatch");
                }
                byte storage = br.ReadByte();
                if(storage == 1)
                {
                    throw new Exception("RLE format not supported");
                }
                byte bytesPerPixelChannel = br.ReadByte();
                if(bytesPerPixelChannel > 1)
                {
                    throw new Exception("Two bytes per pixel not supported");
                }
                br.ReadBytes(2);
                byte[] colArray = br.ReadBytes(2);
                Array.Reverse(colArray);
                UInt16 cols = BitConverter.ToUInt16(colArray, 0);
                byte[] rowArray = br.ReadBytes(2);
                Array.Reverse(rowArray);
                UInt16 rows = BitConverter.ToUInt16(rowArray, 0);
                byte[] bandArray = br.ReadBytes(2);
                Array.Reverse(bandArray);
                UInt16 bands = BitConverter.ToUInt16(bandArray, 0);

                img = new Image(bands, cols, rows);
                br.ReadBytes(500);
                for (int b = 0; b < bands; b++)
                {
                    for (int r = rows - 1; r >= 0; r--)
                    {
                        for (int c = 0; c < cols; c++)
                        {
                            img[b, r, c] = (float)br.ReadByte() / 255;
                        }
                    }
                }
            }
            return img;
        }

        public override void Write<T>(string filename, Image image, IImageConverter converter, float[] fillValue = null)
        {
            throw new NotImplementedException();
        }
    }
}
