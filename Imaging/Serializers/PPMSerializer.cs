using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    class PPMSerializer : ImageSerializer
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
            return new string[] { ".ppm", ".ppmz" };
        }

        private bool ShouldCompress(string fn)
        {
            return Path.GetExtension(fn).ToLower() == ".ppmz";
        }

        public override Image Read(string filename, IImageConverter converter, float[] fillValue = null,
                                   bool useFillValueFromFile = false)
        {
            Stream open(string fn)
            {
                Stream s = new FileStream(fn, FileMode.Open, FileAccess.Read, FileShare.Read);
                return ShouldCompress(fn) ? new GZipStream(s, CompressionMode.Decompress) : s;
            }

            using (var br = new BinaryReader(open(filename)))
            {
                char readChar()
                {
                    int b = br.Read();
                    if (b < 0)
                    {
                        throw new Exception("unexpected EOF parsing PPM header");
                    }
                    return (char)b;
                }

                char ch = readChar();

                string readToken(bool eatWhitespace = true)
                {
                    var sb = new StringBuilder();
                    bool ignoring = false;
                    string tok = null;
                    for (int i = 0; i < 1000; i++)
                    {
                        if ((!ignoring && char.IsWhiteSpace(ch)) || (ignoring && (ch == '\r' || ch == '\n')))
                        {
                            tok = sb.ToString();
                            break;
                        }
                        else if (ch == '#')
                        {
                            ignoring = true;
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        ch = readChar();
                    }
                    for (int i = 0; eatWhitespace && char.IsWhiteSpace(ch) && i < 1000; i++)
                    {
                        ch = readChar();
                    }
                    return tok;
                }

                string f = readToken();
                if (f != "P6")
                {
                    throw new Exception("unexpected PPM magic: " + f);
                }

                int width = 0, height = 0, maxVal = 0;

                string w = readToken();
                if (w != null && !int.TryParse(w, out width))
                {
                    throw new Exception("error parsing PPM width: " + w);
                }

                string h = readToken();
                if (h != null && !int.TryParse(h, out height))
                {
                    throw new Exception("error parsing PPM height: " + h);
                }

                string m = readToken(eatWhitespace: false);
                if (m != null && !int.TryParse(m, out maxVal))
                {
                    throw new Exception("unexpected PPM max val: " + m);
                }
                
                if (maxVal <= 0 || maxVal > 65535)
                {
                    throw new Exception("max value must be in range 1-65535, got: " + maxVal);
                }

                int bytesPerVal = maxVal < 256 ? 1 : 2;

                Image img = new Image(3, width, height);

                for (int r = 0; r < height; r++)
                {
                    for (int c = 0; c < width; c++)
                    {
                        for (int b = 0; b < 3; b++)
                        {
                            byte[] bytes = br.ReadBytes(bytesPerVal); //PPM data is in network byte order (big endian)
                            if (BitConverter.IsLittleEndian)
                            {
                                Array.Reverse(bytes);
                            }
                            if (bytes.Length < bytesPerVal)
                            {
                                throw new Exception($"unexpected EOF at PPM row={r} of {height}, col={c} of {width}");
                            }
                            img[b, r, c] = bytesPerVal == 2 ? BitConverter.ToUInt16(bytes, 0) : (ushort)bytes[0];
                        }
                    }
                }
                return img;
            }
        }

        public override void Write<T>(string filename, Image image, IImageConverter converter, float[] fillValue = null)
        {
            if (typeof(T) != typeof(UInt16))
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

            Stream open(string fn)
            {
                Stream s = new FileStream(fn, FileMode.Create);
                return ShouldCompress(fn) ? new GZipStream(s, CompressionMode.Compress) : s;
            }

            using (var bw = new BinaryWriter(open(filename)))
            {
                string header = $"P6\n{image.Width} {image.Height}\n65535\n";
                foreach (char c in header)
                {
                    bw.Write(c);
                }
                for (int r = 0; r < image.Height; r++)
                {
                    for (int c = 0; c < image.Width; c++)
                    {
                        for (int b = 0; b < image.Bands; ++b)
                        {
                            float val = image[b, r, c];
                            if (val < 0 || val > 65535)
                            {
                                var bv = string.Join(", ", image.GetBandValues(r, c).Select(v => v.ToString("F3")));
                                throw new NotImplementedException(".ppm serializer only supports 0-65535, " +
                                                                  $"got ({bv}) at r={r}, c={c} in {filename}");
                            }
                            ushort s = (ushort)val;
                            //PPM data is in network byte order (big endian)
                            if (BitConverter.IsLittleEndian)
                            {
                                bw.Write(s>>8); //MSB
                                bw.Write(s&0xff); //LSB
                            }
                            else
                            {
                                bw.Write(s&0xff); //MSB
                                bw.Write(s>>8); //LSB
                            }
                        }
                    }
                }
            }
        }
    }
}
