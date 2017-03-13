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
            if (res.NumberOfChannels < img.Bands)
            {
                throw new Exception("Not enough channels to fit result");
            }

            for (int band = 0; band < img.Bands; band++)
            {
                for (int row = 0; row < img.Width; row++)
                {
                    for (int col = 0; col < img.Height; col++)
                    {
                        res.Data[band, row, col] = (byte)(img[band, row, col] * 255);
                    }
                }
            }
            return res;
        }

        public static Image<Gray, byte> ToEmguGrayscale(this Image img)
        {
            Image<Gray, byte> res = new Image<Gray, byte>(img.Width, img.Height);
            for (int row = 0; row < img.Width; row++)
            {
                for (int col = 0; col < img.Height; col++)
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
                for (int row = 0; row < img.Width; row++)
                {
                    for (int col = 0; col < img.Height; col++)
                    {
                        res[band, row, col] = img.Data[band, row, col] / 255.0f;
                    }
                }
            }
            return res;
        }
    }
}
