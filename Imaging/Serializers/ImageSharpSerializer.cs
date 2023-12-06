//#define ENABLE_GDAL_JPG_PNG_BMP

using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;

namespace JPLOPS.Imaging
{
    /// <summary>
    /// ImageSharp is modern, fast, thread safe, and free of global locks.
    /// It is recommended for use instead of System.Drawing in new code.
    ///
    /// https://www.hanselman.com/blog/HowDoYouUseSystemDrawingInNETCore.aspx
    /// https://github.com/SixLabors/ImageSharp
    ///
    /// This interface supports reading and writing images in PNG, JPG, BMP, GIF, and TGA formats for 3 band images with
    /// 8 bits per band.
    ///
    /// PNG additionally supports
    /// * reading and writing 16 bit per band images
    /// * reading and writing 1 band grayscale images
    /// * writing 2 band grayscale + alpha images
    /// * writing 4 band RGBA images
    ///
    /// TODO
    /// * read PNG alpha channel, including into mask
    /// * more combinations of bands and bits for more formats (not all formats can do all combinations)
    ///
    /// Why do we have this in addition to GDAL?
    /// GDAL is supposed to be at least mostly threadsafe
    /// however we are still using a global lock for it
    /// largely because without it we get a lot of "TemporaryFile failed to delete"
    /// this slows down a lot of our main workflows like blending and tiling
    /// ImageSharp is fast and doesn't involve any global locks which is a major factor in parallel workflows
    /// we still use GDAL for tiff which supports extended data types like float, GeoTIFF metadata, and sparse reads
    /// </summary>
    public class ImageSharpSerializer: ImageSerializer
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
            return new string[] {
#if !ENABLE_GDAL_JPG_PNG_BMP
                ".png", ".jpg", ".bmp",
#endif
                ".gif", ".tga"
            };
        }

        public override Image Read(string filename, IImageConverter converter, float[] fillValue = null,
                                   bool useFillValueFromFile = false)
        {
            if (filename.EndsWith("png", StringComparison.OrdinalIgnoreCase))
            {
                PngMetadata md = null;
                using (var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    md = SixLabors.ImageSharp.Image.Identify(fs).Metadata.GetFormatMetadata(PngFormat.Instance);
                }
                if (md != null &&
                    (md.ColorType == PngColorType.Grayscale || md.ColorType == PngColorType.GrayscaleWithAlpha))
                {
                    if (md.BitDepth == PngBitDepth.Bit8)
                    {
                        using (var raw = SixLabors.ImageSharp.Image.Load<L8>(filename))
                        {
                            var img = new JPLOPS.Imaging.Image(1, raw.Width, raw.Height);
                            for (int r = 0; r < raw.Height; r++)
                            {
                                for (int c = 0; c < raw.Width; c++)
                                {
                                    img[0, r, c] = raw[c, r].PackedValue;
                                }
                            }
                            return converter.Convert<byte>(img);
                        }
                    }
                    else if (md.BitDepth == PngBitDepth.Bit16)
                    {
                        using (var raw = SixLabors.ImageSharp.Image.Load<L16>(filename))
                        {
                            var img = new JPLOPS.Imaging.Image(1, raw.Width, raw.Height);
                            for (int r = 0; r < raw.Height; r++)
                            {
                                for (int c = 0; c < raw.Width; c++)
                                {
                                    img[0, r, c] = raw[c, r].PackedValue;
                                }
                            }
                            return converter.Convert<ushort>(img);
                        }
                    }
                }
                else if (md != null && md.BitDepth == PngBitDepth.Bit16 &&
                         (md.ColorType == PngColorType.Rgb || md.ColorType == PngColorType.RgbWithAlpha))
                {
                    using (var raw = SixLabors.ImageSharp.Image.Load<Rgba64>(filename))
                    {
                        var img = new JPLOPS.Imaging.Image(3, raw.Width, raw.Height);
                        for (int r = 0; r < raw.Height; r++)
                        {
                            for (int c = 0; c < raw.Width; c++)
                            {
                                var pixel = raw[c, r];
                                img[0, r, c] = pixel.R;
                                img[1, r, c] = pixel.G;
                                img[2, r, c] = pixel.B;
                            }
                        }
                        return converter.Convert<ushort>(img);
                    }
                }
            }

            using (var raw = SixLabors.ImageSharp.Image.Load<Rgba32>(filename))
            {
                var img = new JPLOPS.Imaging.Image(3, raw.Width, raw.Height);
                for (int r = 0; r < raw.Height; r++)
                {
                    for (int c = 0; c < raw.Width; c++)
                    {
                        var pixel = raw[c, r];
                        img[0, r, c] = pixel.R;
                        img[1, r, c] = pixel.G;
                        img[2, r, c] = pixel.B;
                    }
                }
                return converter.Convert<byte>(img);
            }
        }

        public override void Write<T>(string filename, JPLOPS.Imaging.Image image, IImageConverter converter,
                                      float[] fillValue = null)
        {
            image = converter.Convert<T>(image);

            if (filename.EndsWith("png", StringComparison.OrdinalIgnoreCase))
            {
                if (image.Bands == 1)
                {
                    if (typeof(T) == typeof(byte))
                    {
                        using (var raw = new SixLabors.ImageSharp.Image<L8>(image.Width, image.Height))
                        {
                            for (int r = 0; r < raw.Height; r++)
                            {
                                for (int c = 0; c < raw.Width; c++)
                                {
                                    raw[c, r] = new L8((byte)image[0, r, c]);
                                }
                            }
                            raw.Save(filename, new PngEncoder()
                                     { BitDepth = PngBitDepth.Bit8, ColorType = PngColorType.Grayscale });
                            return;
                        }
                    }
                    else if (typeof(T) == typeof(ushort))
                    {
                        using (var raw = new SixLabors.ImageSharp.Image<L16>(image.Width, image.Height))
                        {
                            for (int r = 0; r < raw.Height; r++)
                            {
                                for (int c = 0; c < raw.Width; c++)
                                {
                                    raw[c, r] = new L16((ushort)image[0, r, c]);
                                }
                            }
                            raw.Save(filename, new PngEncoder()
                                     { BitDepth = PngBitDepth.Bit16, ColorType = PngColorType.Grayscale });
                            return;
                        }
                    }
                    else
                    {
                        throw new NotImplementedException("ImageSharpSerializer.Write() only supports 8 or 16 bit PNG");
                    }
                }
                else if ((image.Bands == 3 || image.Bands == 4) && typeof(T) == typeof(ushort))
                {
                    using (var raw = new SixLabors.ImageSharp.Image<Rgba64>(image.Width, image.Height))
                    {
                        for (int r = 0; r < raw.Height; r++)
                        {
                            for (int c = 0; c < raw.Width; c++)
                            {
                                float red = image[0, r, c];
                                float green = image[1, r, c];
                                float blue = image[2, r, c];
                                float alpha = image.Bands > 3 ? image[3, r, c] : ushort.MaxValue;
                                raw[c, r] = new Rgba64((ushort)red, (ushort)green, (ushort)blue, (ushort)alpha);
                            }
                        }
                        raw.Save(filename, new PngEncoder()
                                 { BitDepth = PngBitDepth.Bit16, ColorType = PngColorType.Rgb });
                        return;
                    }
                }
            }

            if (typeof(T) != typeof(byte))
            {
                throw new NotImplementedException("ImageSharpSerializer.Write() only supports byte for " +
                                                  $"{image.Bands} band " + Path.GetExtension(filename));
            }

            using (var raw = new SixLabors.ImageSharp.Image<Rgba32>(image.Width, image.Height))
            {
                for (int r = 0; r < raw.Height; r++)
                {
                    for (int c = 0; c < raw.Width; c++)
                    {
                        float red = image[0, r, c];
                        float green = image.Bands > 2 ? image[1, r, c] : red;
                        float blue = image.Bands > 2 ? image[2, r, c] : red;
                        float alpha = image.Bands > 3 ? image[3, r, c] : image.Bands == 2 ? image[1, r, c] : 255;
                        raw[c, r] = new Rgba32((byte)red, (byte)green, (byte)blue, (byte)alpha);
                    }
                }
                raw.Save(filename);
            }
        }
    }
}
