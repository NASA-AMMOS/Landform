using System;
using System.Collections.Generic;
using JPLOPS.MathExtensions;
using JPLOPS.Util;

namespace JPLOPS.Imaging
{
    public static class ResizeOps
    {
        public static readonly FilterDelegate CatmullRomFilter = MakeCubicFilter(0, 0.5);
        public static readonly FilterDelegate MitchellFilter = MakeCubicFilter(1 / 3.0, 1 / 3.0);
        public static readonly FilterDelegate BSplineFilter = MakeCubicFilter(1, 0);

        /// <summary>
        /// Function type for resize filters
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        public delegate double FilterDelegate(double x);

        /// <summary>
        /// Image resizing based on 
        /// http://entropymine.com/imageworsener/resample/
        /// TODO this does not respect the image mask, if any
        /// </summary>
        public static Image Resize(this Image img, int targetWidth, int targetHeight, FilterDelegate filter = null)
        {
            if (filter == null)
            {
                // Default to Catmull-Rom for downsampling, Mitchell for upsampling
                if (targetWidth <= img.Width && targetHeight <= img.Height)
                {
                    filter = CatmullRomFilter;
                }
                else
                {
                    filter = MitchellFilter;

                }
            }

            Image horizontalResult = img.Instantiate(img.Bands, targetWidth, img.Height);

            List<Weight> weights = GetResizeWeights(targetWidth, img.Width, 2, filter);

            for (int band = 0; band < img.Bands; band++)
            {
                for (int row = 0; row < img.Height; row++)
                {
                    foreach (Weight w in weights)
                    {
                        float source = img.ReadClampedToBounds(band, w.inPixel, row);
                        horizontalResult[band, row, w.outPixel] += source * (float)w.weight;
                    }
                }
            }

            //resize vertically 
            Image result = img.Instantiate(img.Bands, targetWidth, targetHeight);

            weights = GetResizeWeights(targetHeight, img.Height, 2, filter);

            for (int band = 0; band < img.Bands; band++)
            {
                for (int col = 0; col < horizontalResult.Width; col++)
                {
                    foreach (Weight w in weights)
                    {
                        float source = horizontalResult.ReadClampedToBounds(band, col, w.inPixel);
                        result[band, w.outPixel, col] += source * (float)w.weight;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Resize an image using a simple bicubic function
        /// Consider using Resize() instead
        /// TODO this does not respect the image mask, if any
        /// </summary>
        public static Image ResizeBicubic(this Image img, int targetWidth, int targetHeight)
        {
            float wRatio = (img.Width - 1) / ((float)targetWidth - 1);
            float hRatio = (img.Height - 1) / ((float)targetHeight - 1);

            Image result = img.Instantiate(img.Bands, targetWidth, targetHeight);

            for (int b = 0; b < result.Bands; b++)
            {
                for (int r = 0; r < result.Height; r++)
                {
                    for (int c = 0; c < result.Width; c++)
                    {
                        result[b, r, c] = img.BicubicSample(b, r * hRatio, c * wRatio);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Resize an image using nearest neighbor sampling
        /// TODO this does not respect the image mask, if any
        /// </summary>
        public static Image ResizeNearest(this Image img, int targetWidth, int targetHeight)
        {
            float wRatio = (img.Width - 1) / ((float)targetWidth - 1);
            float hRatio = (img.Height - 1) / ((float)targetHeight - 1);

            Image result = img.Instantiate(img.Bands, targetWidth, targetHeight);

            for (int b = 0; b < result.Bands; b++)
            {
                for (int r = 0; r < result.Height; r++)
                {
                    for (int c = 0; c < result.Width; c++)
                    {
                        result[b, r, c] = img.NearestSample(b, r * hRatio, c * wRatio);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Resize so that both width and height are less than or equal to maxSize.
        /// Does nothing if maxSize is non-positive.
        /// Maintains aspect ratio.
        /// </summary>
        public static Image ResizeMax(this Image img, int maxSize, bool nearestNeighborSampling = false)
        {
            if (maxSize <= 0 || (img.Width <= maxSize && img.Height <= maxSize))
            {
                return img;
            }
            int newWidth = maxSize, newHeight = maxSize;
            if (img.Height > img.Width)
            {
                newHeight = maxSize;
                newWidth = (int)(img.Width * ((double)(newHeight) / img.Height));
            }
            else if (img.Width > img.Height)
            {
                newWidth = maxSize;
                newHeight = (int)(img.Height * ((double)(newWidth) / img.Width));
            }
            return nearestNeighborSampling ? ResizeNearest(img, newWidth, newHeight) : Resize(img, newWidth, newHeight);
        }

        /// <summary>
        /// Resize so that both width and height powers of two.
        /// Does not necessarily maintain aspect ratio.
        /// </summary>
        public static Image ResizePowerOfTwo(this Image img)
        {
            int newWidth = NumberHelper.IsPowerOfTwo(img.Width) ? img.Width : MathE.FloorPowerOf2(img.Width);
            int newHeight = NumberHelper.IsPowerOfTwo(img.Height) ? img.Height : MathE.FloorPowerOf2(img.Height);
            return (newWidth != img.Width || newHeight != img.Height) ? img.Resize(newWidth, newHeight) : img;
        }

        /// <summary>
        /// Helper class containing data for each mapping between input and output pixels
        /// </summary>
        private class Weight
        {
            public int inPixel;
            public int outPixel;
            public double weight;
        }

        /// <summary>
        /// Gets weights for resizing in one dimension. 
        /// Each row (for horizontal resizing) or column (vertical resizing) will have the same weights. 
        /// This way, output values for each row/col can be computed without recomputing the filter for each pixel. 
        /// </summary>
        /// <param name="targetWidth"></param>
        /// <param name="targetHeight"></param>
        /// <param name="radius"></param>
        /// <param name="f"></param>
        /// <returns></returns>
        private static List<Weight> GetResizeWeights(int target, int current, int radius, FilterDelegate f)
        {
            List<Weight> weights = new List<Weight>();

            //If we are enlarging, the filter is scaled off the source pixels; for shrinking, off the target pixels 
            //This causes differences in which source pixels are sampled and what the weight for each source pixel is 
            bool enlarging = target > current;

            double ratio = (target - 1) / ((double)current - 1); // old/new

            for (int targetPixel = 0; targetPixel < target; targetPixel++)
            {
                int startingIndex = weights.Count; //start of weights for this target pixel

                //consider all source pixels for this target pixel 
                double zero = targetPixel / ratio; //current position in old coordinate system is filter's zero point
                int firstpix = enlarging ? (int)(zero - radius) + 1 : (int)((targetPixel - 2) / ratio) + 1; //casting truncates, but we want to round up 
                int lastpix = enlarging ? (int)(zero + radius) : lastpix = (int)((targetPixel + 2) / ratio);
                double norm = 0;
                for (int pixel = firstpix; pixel <= lastpix; pixel++) //iterate through x pixels of old picture 
                {
                    double filterx = enlarging ? zero - pixel : (zero - pixel) * ratio; //x in the filter coordinate system 
                    norm += f(filterx);
                    weights.Add(new Weight { inPixel = pixel, outPixel = targetPixel, weight = f(filterx) });
                }

                //normalize weights for this target pixel
                if (norm != 1)
                {
                    if (norm != 0)
                    {
                        for (int i = startingIndex; i < weights.Count; i++)
                        {
                            weights[i].weight /= norm;
                        }
                    }
                    else //weights sum to zero, so set all to 0
                    {
                        for (int i = startingIndex; i < weights.Count; i++)
                        {
                            weights[i].weight = 0;
                        }
                    }
                }

            }

            return weights;
        }

        public static double QuadraticFilter(double x)
        {
            x = Math.Abs(x);
            if (x < 0.5) return 0.75 - x * x;
            if (x < 1.5) return 0.50 * (x - 1.5) * (x - 1.5);
            return 0.0;
        }

        public static double BoxFilter(double x)
        {
            x = Math.Abs(x);
            if (x <= 0.5) return 1;
            return 0;
        }

        public static double TriangleFilter(double x)
        {
            x = Math.Abs(x);
            if (x <= 1)
            {
                return 1 - x;
            }
            return 0;
        }

        public static FilterDelegate MakeCubicFilter(double B, double C)
        {
            FilterDelegate res = (x) =>
            {
                x = Math.Abs(x);
                double x2 = x * x;
                double x3 = x2 * x;
                if (x < 1)
                {
                    return ((12 - 9 * B - 6 * C) * x3 + (-18 + 12 * B + 6 * C) * x2 + (6 - 2 * B)) / 6;
                }
                else if (x < 2)
                {
                    return ((-B - 6 * C) * x3 + (6 * B + 30 * C) * x2 + (-12 * B - 48 * C) * x + (8 * B + 24 * C)) / 6;
                }
                else
                {
                    return 0;
                }
            };
            return res;
        }
    }
}

