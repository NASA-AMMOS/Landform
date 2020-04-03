using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    /// <summary>
    /// Class for reading RGB images 
    /// </summary>
    class PPMSerializer : ImageSerializer
    {
        public override IImageConverter DefaultReadConverter()
        {
            return ImageConverters.PassThrough;
        }

        public override IImageConverter DefaultWriteConverter()
        {
            return ImageConverters.PassThrough;
        }

        public override string[] GetExtensions()
        {
            return new string[] { ".ppm" };
        }

        public override Image Read(string filename, IImageConverter converter, float[] fillValue = null)
        {
            Image img;
            var sr = new StreamReader(filename);
            int processedHeaderLines = 0;
            int expectedHeaderLines = 3;
            string[] header = new string[3];

            while (processedHeaderLines < expectedHeaderLines)
            {
                var line = sr.ReadLine();
                if(line.Length == 0 || line[0] == '#') //skip empty lines/comments
                {
                    continue;
                } else
                {
                    header[processedHeaderLines] = line;
                    processedHeaderLines++;
                }
            }
            
            if(header[0] != "P6")
            {
                throw new ImageSerializationException(
                    String.Format("Unexpected file format signifier {0}", header[0]));
            }

            var split = header[1].Split(' ');
            if(split.Count() != 2 ||
               !Int32.TryParse(split[0], out int width) ||
               !Int32.TryParse(split[1], out int height))
            {
                throw new ImageSerializationException(
                    String.Format("Unexpected [width height]: {0}", header[1]));
            }

            if(!Int32.TryParse(header[2].Replace(" ", ""), out int maxVal))
            {
                throw new ImageSerializationException(
                    String.Format("Unexpected max pixel value {0}", header[2]));
            }
            if(maxVal <= 0 || maxVal > 65535)
            {
                throw new ImageSerializationException(
                    String.Format("Maximum pixel value {0} must be in range 1-65535", maxVal));
            }

            sr.Close();

            int bands = 3;
            int bytesPerVal = maxVal < 256 ? 1 : 2;
            FileInfo fi = new FileInfo(filename);
            long size = fi.Length;
            long dataSize = width * height * bands * bytesPerVal;
            int headerSize = (int)(size - dataSize);

            using (BinaryReader br = new BinaryReader(File.Open(filename, FileMode.Open)))
            {
                br.ReadBytes(headerSize);
                img = new Image(bands, width, height);
                for (int r = 0; r < height; r++)
                {
                    for (int c = 0; c < width; c++)
                    {
                        for (int b = 0; b < bands; b++)
                        {
                            img[b, r, c] = BitConverter.ToUInt16(br.ReadBytes(bytesPerVal), 0);
                        }
                    }
                }
            }
            return img;
        }

        public override void Write<T>(string filename, Image image, IImageConverter converter, float[] fillValue = null)
        {
            if (fillValue != null)
            {
                throw new NotImplementedException();
            }

            if (image.Bands != 3)
            {
                throw new NotSupportedException(".ppm serializer only supports 3 band images");
            }

            if (File.Exists(filename))
            {
                File.Delete(filename);
            }

            //Header
            var sw = new StreamWriter(new FileStream(filename, FileMode.CreateNew));
            sw.WriteLine("P6");
            sw.WriteLine($"{image.Width} {image.Height}");
            sw.WriteLine("65535");
            sw.Close();
            //Data
            var bw = new BinaryWriter(new FileStream(filename, FileMode.Append));
            for (int r = 0; r < image.Height; r++)
            {
                for (int c = 0; c < image.Width; c++)
                {
                    for (int b = 0; b < image.Bands; ++b)
                    {
                        float val = image[b, r, c];
                        if (val < 0 || val > 65535)
                        {
                            throw new NotImplementedException(".ppm serializer only supports values 0-65535");
                        }
                        bw.Write((UInt16)val);
                    }
                }
            }
            bw.Close();
        }
    }
}
