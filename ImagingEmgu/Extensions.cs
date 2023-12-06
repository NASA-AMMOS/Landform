using System;
using Emgu.CV;
using Emgu.CV.Structure;

namespace JPLOPS.Imaging.Emgu
{
    public static class Extensions
    {
        /// <summary>
        /// converts a 3 float color in R,G,B order with an expected range of 0 to 1 to an emgu color
        /// </summary>
        public static Rgb ToEmguColor(float[] color)
        {
            return new Rgb(color[0] * 255, color[1] * 255, color[2] * 255);
        }

        public static Image<TColor, byte> ToEmgu<TColor>(this Image img) where TColor : struct, IColor
        {
            Image<TColor, byte> res = new Image<TColor, byte>(img.Width, img.Height);
            if (img.Bands != 1 && res.NumberOfChannels != img.Bands)
            {
                throw new Exception("Wrong number of channels in result type");
            }

            for (int band = 0; band < res.NumberOfChannels; band++)
            {
                int srcBand = (img.Bands > 1) ? band : 0;
                for (int row = 0; row < img.Height; row++)
                {
                    for (int col = 0; col < img.Width; col++)
                    {
                        res.Data[row, col, band] = (byte)(img[srcBand, row, col] * 255);
                    }
                }
            }
            return res;
        }

        /// <summary>
        /// Convert image to a grayscale Emgu image by averaging all color channels.
        /// TODO: for feature detection we may want to use LuminanceMode.GREEN
        /// </summary>
        public static Image<Gray, byte> ToEmguGrayscale(this Image img, LuminanceMode mode = LuminanceMode.Average)
        {
            if (img.Bands == 1)
            {
                return img.ToEmgu<Gray>();
            }

            if (mode != LuminanceMode.Average && img.Bands != 3)
            {
                throw new ArgumentException(string.Format("luminance mode {0} requires 3 band image, got {1}",
                                                          mode, img.Bands));
            }

            Image<Gray, byte> res = new Image<Gray, byte>(img.Width, img.Height);
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    float value = 0;
                    if (img.Bands == 3)
                    {
                        //NOTE: implies RGB ordering
                        value = Colorize.ColorToMono(img[0, row, col], img[1, row, col], img[2, row, col], mode);
                    }
                    else
                    {
                        for (int band = 0; band < img.Bands; band++)
                        {
                            value += img[0, row, col] / img.Bands;
                        }
                    }

                    res[row, col] = new Gray(value * 255);
                }
            }
            return res;
        }

        public static Image ToOPSImage<TColor>(this Image<TColor, byte> img) where TColor : struct, IColor
        {
            Image res = new Image(img.NumberOfChannels, img.Width, img.Height);
            for (int band = 0; band < img.NumberOfChannels; band++)
            {
                for (int row = 0; row < img.Height; row++)
                {
                    for (int col = 0; col < img.Width; col++)
                    {
                        res[band, row, col] = img.Data[row, col, band] / 255.0f;
                    }
                }
            }
            return res;
        }
    }
}
