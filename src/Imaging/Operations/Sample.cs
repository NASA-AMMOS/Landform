using System;
using JPLOPS.MathExtensions;

namespace JPLOPS.Imaging
{
    public static class Sample
    {
        public static float NearestSample(this Image img, int band, float row, float col)
        {
            return img.ReadClampedToBounds(band, x: (float)Math.Round(col), y: (float)Math.Round(row));
        }

        public static float BilinearSample(this Image img, int band, float row, float col)
        {
            int irow, icol;
            float rfrac, cfrac;
            float row1 = 0, row2 = 0;

            irow = (int)row;
            icol = (int)col;

            if (irow < 0 || irow >= img.Height || icol < 0 || icol >= img.Width) { return 0; }

            row = Math.Min(row, img.Height - 1);
            col = Math.Min(col, img.Width - 1);

            rfrac = (float)(1.0 - (row - irow));
            cfrac = (float)(1.0 - (col - icol));

            if (cfrac < 1)
            {
                row1 = cfrac * img[band, irow, icol] + (1.0f - cfrac) * img[band, irow, icol + 1];
            }
            else
            {
                row1 = img[band, irow, icol];
            }

            if (rfrac < 1)
            {
                if (cfrac < 1)
                {
                    row2 = cfrac * img[band, irow + 1, icol] + (1.0f - cfrac) * img[band, irow + 1, icol + 1];
                }
                else
                {
                    row2 = img[band, irow + 1, icol];
                }
            }
            return rfrac * row1 + (1f - rfrac) * row2;
        }

        /// <summary>
        /// Sample a pixel
        /// </summary>
        /// <param name="b"></param>
        /// <param name="row"></param>
        /// <param name="col"></param>
        /// <returns></returns>
        public static float BicubicSample(this Image img, int b, float row, float col)
        {
            var x = col;
            var y = row;

            var x1 = (int)x;
            var y1 = (int)y;
            var x2 = x1 + 1;
            var y2 = y1 + 1;

            var p00 = img.ReadClampedToBounds(b, x1 - 1, y1 - 1);
            var p01 = img.ReadClampedToBounds(b, x1 - 1, y1);
            var p02 = img.ReadClampedToBounds(b, x1 - 1, y2);
            var p03 = img.ReadClampedToBounds(b, x1 - 1, y2 + 1);

            var p10 = img.ReadClampedToBounds(b, x1, y1 - 1);
            var p11 = img.ReadClampedToBounds(b, x1, y1);
            var p12 = img.ReadClampedToBounds(b, x1, y2);
            var p13 = img.ReadClampedToBounds(b, x1, y2 + 1);

            var p20 = img.ReadClampedToBounds(b, x2, y1 - 1);
            var p21 = img.ReadClampedToBounds(b, x2, y1);
            var p22 = img.ReadClampedToBounds(b, x2, y2);
            var p23 = img.ReadClampedToBounds(b, x2, y2 + 1);

            var p30 = img.ReadClampedToBounds(b, x2 + 1, y1 - 1);
            var p31 = img.ReadClampedToBounds(b, x2 + 1, y1);
            var p32 = img.ReadClampedToBounds(b, x2 + 1, y2);
            var p33 = img.ReadClampedToBounds(b, x2 + 1, y2 + 1);

            return BicubicInterp(x - x1, y - y1,
                                 p00, p10, p20, p30,
                                 p01, p11, p21, p31,
                                 p02, p12, p22, p32,
                                 p03, p13, p23, p33);
        }

        /// <summary>
        /// Helper method for bicubic interpolation
        /// https://github.com/hughsk/bicubic
        /// https://github.com/hughsk/bicubic-sample/blob/master/index.js
        /// </summary>
        /// <param name="xf"></param>
        /// <param name="yf"></param>
        /// <param name="p00"></param>
        /// <param name="p01"></param>
        /// <param name="p02"></param>
        /// <param name="p03"></param>
        /// <param name="p10"></param>
        /// <param name="p11"></param>
        /// <param name="p12"></param>
        /// <param name="p13"></param>
        /// <param name="p20"></param>
        /// <param name="p21"></param>
        /// <param name="p22"></param>
        /// <param name="p23"></param>
        /// <param name="p30"></param>
        /// <param name="p31"></param>
        /// <param name="p32"></param>
        /// <param name="p33"></param>
        /// <returns></returns>
        private static float BicubicInterp(float xf, float yf,
                                           float p00, float p01, float p02, float p03,
                                           float p10, float p11, float p12, float p13,
                                           float p20, float p21, float p22, float p23,
                                           float p30, float p31, float p32, float p33)
        {
            var yf2 = yf * yf;
            var xf2 = xf * xf;
            var xf3 = xf * xf2;

            var x00 = p03 - p02 - p00 + p01;
            var x01 = p00 - p01 - x00;
            var x02 = p02 - p00;
            var x0 = x00 * xf3 + x01 * xf2 + x02 * xf + p01;

            var x10 = p13 - p12 - p10 + p11;
            var x11 = p10 - p11 - x10;
            var x12 = p12 - p10;
            var x1 = x10 * xf3 + x11 * xf2 + x12 * xf + p11;

            var x20 = p23 - p22 - p20 + p21;
            var x21 = p20 - p21 - x20;
            var x22 = p22 - p20;
            var x2 = x20 * xf3 + x21 * xf2 + x22 * xf + p21;

            var x30 = p33 - p32 - p30 + p31;
            var x31 = p30 - p31 - x30;
            var x32 = p32 - p30;
            var x3 = x30 * xf3 + x31 * xf2 + x32 * xf + p31;

            var y0 = x3 - x2 - x0 + x1;
            var y1 = x0 - x1 - y0;
            var y2 = x2 - x0;

            return y0 * yf * yf2 + y1 * yf2 + y2 * yf + x1;
        }

        /// <summary>
        /// Read a value but clamp x and y to valid bounds
        /// CAREFUL this function takes arguments in (x, y) order not (row, col)
        /// </summary>
        /// <param name="b"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public static float ReadClampedToBounds(this Image img, int b, float x, float y)
        {
            int row = (int)MathE.Clamp(y, 0, img.Height - 1);
            int col = (int)MathE.Clamp(x, 0, img.Width - 1);
            return img[b, row, col];
        }

    }
}

