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
            if (res.NumberOfChannels != img.Bands)
            {
                throw new Exception("Wrong number of channels in result type");
            }

            for (int band = 0; band < img.Bands; band++)
            {
                for (int row = 0; row < img.Height; row++)
                {
                    for (int col = 0; col < img.Width; col++)
                    {
                        res.Data[row, col, band] = (byte)(img[band, row, col] * 255);
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

        public static float GetPixelBilinearInterpolation(this Image image, float col, float row)
        {
            int irow, icol;
            float rfrac, cfrac;
            float row1 = 0, row2 = 0;

            irow = (int)row;
            icol = (int)col;

            if (irow < 0 || irow >= image.Height || icol < 0 || icol >= image.Width) { return 0; }

            row = Math.Min(row, image.Height - 1);
            col = Math.Min(col, image.Width - 1);

            rfrac = (float)1.0 - (row - irow); // casting may be in wrong area
            cfrac = (float)1.0 - (col - icol); // same problem as above

            if (cfrac < 1)
            {
                row1 = cfrac * image.Data[irow][icol] + (float)(1.0 - cfrac) * image.Data[irow][icol + 1];
            }
            else
            {
                row1 = image.Data[irow][icol];
            }

            if (rfrac < 1)
            {
                if (cfrac < 1)
                {
                    row2 = cfrac * image.Data[irow + 1][icol] + (float)(1.0 - cfrac) * image.Data[irow + 1][icol + 1];
                }
                else
                {
                    row2 = image.Data[irow + 1][icol];
                }
            }


            return rfrac * row1 + (1 - rfrac) * row2;
        }
    }
}
