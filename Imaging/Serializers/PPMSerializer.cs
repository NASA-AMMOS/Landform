using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
            return new string[] { ".ppm", ".ppmz" };
        }

        private bool ShouldCompress(string fn)
        {
            return Path.GetExtension(fn).ToLower() == ".ppmz";
        }

        private Stream OpenWriteStream(string fn)
        {
            Stream s = new FileStream(fn, FileMode.CreateNew);
            if (ShouldCompress(fn))
            {
                s = new GZipStream(s, CompressionMode.Compress);
            }
            return s;
        }

        private Stream OpenReadStream(string fn)
        {
            Stream s = new FileStream(fn, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (ShouldCompress(fn))
            {
                s = new GZipStream(s, CompressionMode.Decompress);
            }
            return s;
        }

        public override Image Read(string filename, IImageConverter converter, float[] fillValue = null, bool useFillValueFromFile = false)
        {
            Image img;
            var sr = new StreamReader(OpenReadStream(filename));
            int processedHeaderLines = 0;
            int expectedHeaderLines = 3;
            int headerSize = 0;
            string[] header = new string[3];

            while (processedHeaderLines < expectedHeaderLines)
            {
                var line = sr.ReadLine();
                headerSize += line.Length + 1; //2 byte new line
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

            using (BinaryReader br = new BinaryReader(OpenReadStream(filename)))
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
            if(typeof(T) != typeof(UInt16))
            {
                throw new NotImplementedException();
            }

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
            
            using (var bw = new BinaryWriter(OpenWriteStream(filename)))
            {
                //Header
                string header = $"P6\n{image.Width} {image.Height}\n65535\n";
                foreach (char c in header)
                {
                    bw.Write(c);
                }

                //Data
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
            }
        }
    }
}
