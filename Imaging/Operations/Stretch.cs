using System;
using JPLOPS.MathExtensions;

namespace JPLOPS.Imaging
{
    public static class Stretch
    {
        /// <summary>
        /// Linearly scale values in a band in place
        /// </summary>
        /// <param name="band">band to scale</param>
        /// <param name="beforeMin">any pixles currently at this value will be mapped to afterMin</param>
        /// <param name="beforeMax">any pixels currently at this value will be mapped to afterMax</param>
        /// <param name="afterMin">the new min value for this band</param>
        /// <param name="afterMax">the new max value for this band</param>
        public static Image ScaleValues(this Image img, int band, float beforeMin, float beforeMax,
                                        float afterMin, float afterMax)
        {
            if (beforeMax == beforeMin)
            {
                throw new Exception("Cannot ScaleValues when beforeMin and beforeMax are the same");
            }

            float beforeRange = beforeMax - beforeMin;
            float afterRange = afterMax - afterMin;

            img.ApplyInPlace(band, x =>
            {
                float amount = (x - beforeMin) / beforeRange;
                float result = MathE.Clamp(afterMin + afterRange * amount, afterMin, afterMax);
                return result;
            });

            return img;
        }

        public static void ScaleValues(this Image img, float scalar, bool applyToMaskedValues = true)
        {
            img.ApplyInPlace(v => { return v * scalar; }, applyToMaskedValues);           
        }

        /// <summary>
        /// Linearly scales values in the image from [beforeMin, beforeMax] to [afterMin, afterMax] in place
        /// Scaling is applied uniformly to all bands of the image.
        /// Result values are clamped to afterMin and afterMax in the case that input values are outside
        /// beforeMin and beforeMax
        /// 
        /// For example, you might do the following to convert RGB values from 16-bit to normalzied 0-1 form
        /// ScaleValues(0, ushort.MaxValue, 0, 1);
        /// </summary>
        /// <param name="beforeMin">min value in original imge</param>
        /// <param name="beforeMax">max value in original image</param>
        /// <param name="afterMin">min value in result image</param>
        /// <param name="afterMax">max value in result image</param>
        public static Image ScaleValues(this Image img, float beforeMin, float beforeMax,
                                        float afterMin, float afterMax)
        {
            for (int b = 0; b < img.Bands; b++)
            {
                img.ScaleValues(b, beforeMin, beforeMax, afterMin, afterMax);
            }
            return img;
        }

        /// <summary>
        /// Stretch the color channles of an image based the standard deviation of its values in place
        /// The resulting image will have its values normalzied between 0 and 1
        /// NOTE bands with no variance (ie all the same value) will not be scaled and could be outside the 0-1 range
        /// NOTE masked values are also not scaled and could remain outside the 0-1 range
        /// </summary>
        /// <param name="nStdev">Number of standard deviations from the mean to place the upper and lower values of the stretch</param>
        public static Image ApplyStdDevStretch(this Image img, double nStdev = 3)
        {
            ImageStatistics stats = new ImageStatistics(img);
            for (int b = 0; b < img.Bands; b++)
            {
                // Cannot apply streatch with 1 or fewer values
                if (stats.Average(b).Count <= 1)
                {
                    continue;
                }
                double stdev = stats.Average(b).StandardDeviation;
                double mean = stats.Average(b).Mean;

                double min = Math.Max(mean - stdev * nStdev, stats.Average(b).Min);
                double max = Math.Min(mean + stdev * nStdev, stats.Average(b).Max);
                // Scaling values is invalid if min and max are the same
                if (min != max)
                {
                    img.ScaleValues(b, (float)min, (float)max, 0, 1);
                }
            }
            return img;
        }

        /// <summary>
        /// Normalize the color channels of this image to 0-1
        /// NOTE that bands with no variance (ie all the same value) will not be scaled and could be outside the 0-1 range
        /// NOTE masked values are also not scaled and could remain outside the 0-1 range
        /// </summary>
        public static Image Normalize(this Image img)
        {
            ImageStatistics stats = new ImageStatistics(img);
            for (int b = 0; b < img.Bands; b++)
            {
                if (stats.Average(b).Count <= 1)
                {
                    continue;
                }
                double min = stats.Average(b).Min;
                double max = stats.Average(b).Max;
                // Scaling values is invalid if min and max are the same
                if (min != max)
                {
                    img.ScaleValues(b, (float)min, (float)max, 0, 1);
                }
            }
            return img;
        }
    }
}

