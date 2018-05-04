using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using Emgu.CV;
using Emgu.CV.Structure;

namespace OPS.Imaging.Emgu
{
    public static class Extensions
    {
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
        /// </summary>
        public static Image<Gray, byte> ToEmguGrayscale(this Image img)
        {
            Image<Gray, byte> res = new Image<Gray, byte>(img.Width, img.Height);
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    float value = 0;
                    for (int band = 0; band < img.Bands; band++)
                    {
                        value += img[0, row, col] / img.Bands;
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
