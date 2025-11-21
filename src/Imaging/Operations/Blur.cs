using System;

namespace JPLOPS.Imaging
{
    public static class Blur
    {
        /// <summary>
        /// Simulates a gaussian blur using 3 box blurs
        /// Reference:
        /// http://blog.ivank.net/fastest-gaussian-blur.html
        /// http://elynxsdk.free.fr/ext-docs/Blur/Fast_box_blur.pdf
        /// </summary>
        /// <param name="img">image to blur</param>
        /// <param name="r">radius of blur</param>
        public static Image GaussianBoxBlur(this Image img, int r, bool blendMasked = false)
        {
            int[] boxes = BoxesForGauss(r, 3);
            Image tmp = img.Instantiate(img.Bands, img.Width, img.Height);
            BoxBlur(img, tmp, (boxes[0] - 1) / 2, blendMasked);
            BoxBlur(img, tmp, (boxes[1] - 1) / 2, blendMasked);
            BoxBlur(img, tmp, (boxes[2] - 1) / 2, blendMasked);
            return img;
        }

        /// <summary>
        /// Computes a set of radius parameters to use in a box blur to simulate a gaussian blur
        /// </summary>
        /// <param name="sigma"></param>
        /// <param name="n"></param>
        /// <returns></returns>
        static int[] BoxesForGauss(double sigma, int n)  // standard deviation, number of boxes
        {
            var wIdeal = Math.Sqrt((12 * sigma * sigma / n) + 1);  // Ideal averaging filter width 
            int wl = (int)Math.Floor(wIdeal);
            if (wl % 2 == 0)
            {
                wl--;
            }
            int wu = wl + 2;

            var mIdeal = (12 * sigma * sigma - n * wl * wl - 4 * n * wl - 3 * n) / (-4 * wl - 4);
            int m = (int)Math.Round(mIdeal);
            int[] sizes = new int[n];
            for (var i = 0; i < n; i++)
            {
                sizes[i] = (i < m ? wl : wu);
            }
            return sizes;
        }

        /// <summary>
        /// Performs a box blur of radius r on the src image
        /// Takes a tmp image of the same dimensions as src
        /// </summary>
        /// <param name="src">The image to blur</param>
        /// <param name="tmp">Temporary image same size as src used to store intermediate computations</param>
        /// <param name="r">radius of blur</param>
        public static void BoxBlur(this Image src, Image tmp, int r, bool blendMasked = false)
        {
            // First compute the horizontal blur 
            for(int row = 0; row < src.Height; row++)
            {
                double[] curSum = new double[src.Bands];
                int curCount = 0;
                // We are going to move horizonally along the row and take an evenly weighted box average of pixels within r
                // At each step we only need to add the next value and remove the previous
                // First initiate cur and sum
                for (int col = 0; col < Math.Min(r, src.Width); col++)
                {
                    if (blendMasked || src.IsValid(row, col))
                    {
                        for (int b = 0; b < src.Bands; b++)
                        {
                            curSum[b] += src[b, row, col];
                        }
                        curCount++;
                    }
                }
                // Now loop through all columns.  At each iteration add the value that is r ahead and subtract the one that is r+1 behiend
                for (int col = 0; col < src.Width; col++)
                {
                    // If radius - 1 is in bounds and not masked out, remove it from our running sum of values
                    int colToRemove = col - r - 1;
                    if(colToRemove >= 0 && (blendMasked || src.IsValid(row, colToRemove)))
                    {
                        for (int b = 0; b < src.Bands; b++)
                        {
                            curSum[b] -= src[b, row, colToRemove];
                        }
                        curCount--;
                    }
                    // If radius ahead is in bounds and not masked out, add it to our running sum of values
                    int colToAdd = col + r;
                    if(colToAdd < src.Width && (blendMasked || src.IsValid(row, colToAdd)))
                    {
                        for (int b = 0; b < src.Bands; b++)
                        {
                            curSum[b] += src[b, row, colToAdd];
                        }
                        curCount++;
                    }
                    // Set tmp to the average
                    for (int b = 0; b < src.Bands; b++)
                    {
                        // Don't change this pixel if it is masked out or if we had zero values to include in the average
                        if (curCount > 0 && (blendMasked || src.IsValid(row, col)))
                        {
                            tmp[b, row, col] = (float)(curSum[b] / curCount);
                        }
                        else
                        {
                            tmp[b, row, col] = src[b, row, col];
                        }
                    }
                }
            }
            // Next compute the total blur by bluring vertically
            for (int col = 0; col < src.Width; col++)
            {
                double[] curSum = new double[src.Bands];
                int curCount = 0;
                // We are going to move vertically down the column but need to pre-fill curSum and curCount
                // Note that we continue to use src for validity checks since it has a mask but we are now reading
                // from tmp since it contains the results of the horizontal blur. 
                for (int row = 0; row < Math.Min(r, src.Height); row++)
                {
                    if (blendMasked || src.IsValid(row, col))
                    {
                        for (int b = 0; b < src.Bands; b++)
                        {
                            curSum[b] += tmp[b, row, col];
                        }
                        curCount++;
                    }
                }
                // Now loop through all rows.  At each iteration add the value that is r ahead and subtract the one that is r+1 behiend
                for (int row = 0; row < src.Height; row++)
                {
                    // If radius - 1 is in bounds and not masked out, remove it from our running sum of values
                    int rowToRemove = row - r - 1;
                    if (rowToRemove >= 0 && (blendMasked || src.IsValid(rowToRemove, col)))
                    {
                        for (int b = 0; b < src.Bands; b++)
                        {
                            curSum[b] -= tmp[b, rowToRemove, col];
                        }
                        curCount--;
                    }
                    // If radius ahead is in bounds and not masked out, add it to our running sum of values
                    int rowToAdd = row + r;
                    if (rowToAdd < src.Height && (blendMasked || src.IsValid(rowToAdd, col)))
                    {
                        for (int b = 0; b < src.Bands; b++)
                        {
                            curSum[b] += tmp[b, rowToAdd, col];
                        }
                        curCount++;
                    }
                    // Set src to the average.  We write back to the source
                    // Don't modify the value if this pixel is masked out or if we didn't have any valid values in the average
                    for (int b = 0; b < src.Bands; b++)
                    {
                        if (curCount > 0 && (blendMasked || src.IsValid(row, col)))
                        {
                            src[b, row, col] = (float)(curSum[b] / curCount);
                            if (blendMasked && src.HasMask)
                            {
                                src.SetMaskValue(row, col, false);
                            }
                        }
                        else
                        {
                            src[b, row, col] = tmp[b, row, col];
                        }
                    }
                }
            }

        }
    }
}
