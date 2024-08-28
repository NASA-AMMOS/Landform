using System;
using JPLOPS.MathExtensions;

namespace JPLOPS.Imaging
{
    public enum StretchMode { None, StandardDeviation, HistogramPercent };

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

        ///<summary>
        ///Apply a "histogram percent" stretch to an image.
        ///
        ///This is a common operation in Mars mission ground data processing.
        ///
        ///It originally derives from the "ASTRETCH" mode of the VICAR stretch program
        ///
        ///https://raw.githubusercontent.com/NASA-AMMOS/VICAR/master/vos/p3/prog/stretch/stretch.pdf
        ///
        ///with some further tweaks deriving from MarsViewer and MarsViewer web.
        ///
        ///At a high level the idea is to saturate some percentage of the darkest pixels to become black, and separately
        ///some percentage of the brightest pixels to become white.  The mechanics of how this is done involve first
        ///computing a histogram for each band, and then saturating pixels that fall into some fractions of the low and
        ///high bins.
        ///</summary>
        public static Image HistogramPercentStretch(this Image img, out float min, out float max,
                                                    float lowPercent = 0.5f, float highPercent = 0.5f,
                                                    int histogramBins = 4096)
        {
            min = 0.0f; max = 1.0f;

            if (histogramBins < 2)
            {
                return img;
            }

            var histogram = new ImageHistogram(img, histogramBins);

            float lowFrac = Math.Max(Math.Min(lowPercent, 100), 0) / 100.0f;
            float highFrac = Math.Max(Math.Min(highPercent, 100), 0) / 100.0f;
            
            //the number of nonzero pixels in each band should all be about the same...
            int numPixels = img.Width * img.Height;
            long[] numNonzeroPixels = new long[img.Bands];
            for (int j = 0; j < img.Bands; j++)
            {
                numNonzeroPixels[j] = numPixels - histogram[j,0];
            }

            float newMin = 0.0f, newMax = 1.0f;
            while (lowFrac != 0 || highFrac != 0)
            {
                long maxMinOutliers = 0, maxMaxOutliers = 0;
                long[] numMinOutliers = new long[img.Bands];
                long[] numMaxOutliers = new long[img.Bands];
                for (int j = 0; j < img.Bands; j++)
                {
                    numMinOutliers[j] = (long)(lowFrac * numNonzeroPixels[j]);
                    numMaxOutliers[j] = (long)(highFrac * numNonzeroPixels[j]);
                    maxMinOutliers = Math.Max(maxMinOutliers, numMinOutliers[j]);
                    maxMaxOutliers = Math.Max(maxMaxOutliers, numMaxOutliers[j]);
                }
                
                if (maxMinOutliers > 0)
                {
                    int[] count = new int[img.Bands];
                    for (int i = 1; i < histogramBins; ++i) //skip bin 0
                    {
                        bool done = false;
                        for (int j = 0; j < img.Bands; ++j)
                        {
                            count[j] += histogram[j,i];             
                            if (count[j] >= numMinOutliers[j])
                            {
                                done = true;
                                break;
                            }
                        }
                        if (done || i == (histogramBins - 1))
                        {
                            newMin = 0.0f + (i * histogram.BinWidth);
                            break;
                        }
                    }
                }
            
                if (maxMaxOutliers > 0)
                {
                    int[] count = new int[img.Bands];
                    for (int i = histogramBins - 1; i > 0; --i) //skip bin 0
                    {
                        bool done = false;
                        for (int j = 0; j < img.Bands; ++j)
                        {
                            count[j] += histogram[j,i];
                            if (count[j] >= numMaxOutliers[j])
                            {
                                done = true;
                                break;
                            }
                        }
                        if (done || i == 1)
                        {
                            newMax = 0.0f + ((i + 1) * histogram.BinWidth);
                            break;
                        }
                    }
                }
                
                float eps = .000001f;
                if (newMin == newMax) //single color images
                {
                    if (newMin >= eps)
                    {
                        newMin = Math.Max(0, newMin - eps); //max should be redundant but accounts for numerical error
                    }
                    else
                    {
                        newMax += eps;
                    }
                }
                
                if (newMax > newMin)
                {
                    break;
                }

                //try again with smaller outlier counts
                lowFrac = lowFrac > 0.0005f ? (lowFrac * 0.5f) : 0;
                highFrac = highFrac > 0.0005f ? (highFrac * 0.5f) : 0;
                newMin = 0.0f; newMax = 1.0f;
            }

            if (newMin > 0.0f || newMax < 1.0f)
            {
                min = newMin; max = newMax;
                img.ScaleValues(newMin, newMax, 0, 1);
            }

            return img;
        }

        public static Image HistogramPercentStretch(this Image img, float lowPercent = 0.5f, float highPercent = 0.5f,
                                                    int histogramBins = 4096)
        {
            return HistogramPercentStretch(img, out float min, out float max, lowPercent, highPercent, histogramBins);
        }

        /// <summary>
        /// Stretch the color channles of an image based the standard deviation of its values in place
        /// The resulting image will have its values normalzied between 0 and 1
        /// NOTE bands with no variance (ie all the same value) will not be scaled and could be outside the 0-1 range
        /// NOTE masked values are also not scaled and could remain outside the 0-1 range
        /// </summary>
        /// <param name="nStdev">Number of standard deviations from the mean to place the upper and lower values of the stretch</param>
        public static Image ApplyStdDevStretch(this Image img, double nStdev = 3,
                                               bool applySameStretchToAllbands = true)
        {
            double minAcrossAllBands = Double.PositiveInfinity;
            double maxAcrossAllBands = Double.NegativeInfinity;
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
                if (applySameStretchToAllbands)
                {
                    minAcrossAllBands = Math.Min(minAcrossAllBands, min);
                    maxAcrossAllBands = Math.Max(maxAcrossAllBands, max);
                }
                else if (min != max) // Scaling values is invalid if min and max are the same
                {
                    img.ScaleValues(b, (float)min, (float)max, 0, 1);
                }
            }
            if (applySameStretchToAllbands)
            {
                img.ScaleValues((float)minAcrossAllBands, (float)maxAcrossAllBands, 0, 1);
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

        public static Image Normalize(this Image img, float min, float max)
        {
            return img.ScaleValues(min, max, 0, 1);
        }
    }
}

