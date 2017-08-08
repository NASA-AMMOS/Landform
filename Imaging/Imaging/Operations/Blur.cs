using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    class Blur
    {
        static public void GuassianBoxBlur(Image img, int r)
        {
            int[] boxes = BoxesForGauss(r, 3);
            Image tmp = new Image(img.Bands, img.Width, img.Height);
            BoxBlur(img, tmp, (boxes[0] - 1) / 2);
            BoxBlur(img, tmp, (boxes[1] - 1) / 2);
            BoxBlur(img, tmp, (boxes[2] - 1) / 2);
        }

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
            // var sigmaActual = Math.sqrt( (m*wl*wl + (n-m)*wu*wu - n)/12 );

            int[] sizes = new int[n];
            for (var i = 0; i < n; i++)
            {
                sizes[i] = (i < m ? wl : wu);
            }
            return sizes;
        }

        static void BoxBlur(Image src, Image tmp, int r)
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
                    if(src.IsValid(row, col))
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
                    if(colToRemove >= 0 && src.IsValid(row, colToRemove))
                    {
                        for (int b = 0; b < src.Bands; b++)
                        {
                            curSum[b] -= src[b, row, colToRemove];
                        }
                        curCount--;
                    }
                    // If radius ahead is in bounds and not masked out, add it to our running sum of values
                    int colToAdd = col + r;
                    if(colToAdd < src.Width && src.IsValid(row, colToAdd))
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
                        if (curCount > 0)
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
                    if (src.IsValid(row, col))
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
                    if (rowToRemove >= 0 && src.IsValid(rowToRemove, col))
                    {
                        for (int b = 0; b < src.Bands; b++)
                        {
                            curSum[b] -= tmp[b, rowToRemove, col];
                        }
                        curCount--;
                    }
                    // If radius ahead is in bounds and not masked out, add it to our running sum of values
                    int rowToAdd = row + r;
                    if (rowToAdd < src.Height && src.IsValid(rowToAdd, col))
                    {
                        for (int b = 0; b < src.Bands; b++)
                        {
                            curSum[b] += tmp[b, rowToAdd, col];
                        }
                        curCount++;
                    }
                    // Set src to the average.  We write back to the source

                    for (int b = 0; b < src.Bands; b++)
                    {
                        if (curCount > 0)
                        {
                            src[b, row, col] = (float)(curSum[b] / curCount);
                        }
                        else
                        {
                            src[b, row, col] = tmp[b, row, col];
                        }
                    }
                }
            }

        }

        static public void GuassianBlur(Image img, int radius)
        {
            // Make a copy to read from while we write back to img
            Image tmp = (Image)img.Clone();
            int n = radius * 2 + 1;
            double sigma = 20;// 0.3 * (n / 2 - 1) + 0.8;
            double[,] kernel = new double[n, n];

            double scalar = 1 / (2 * Math.PI * sigma * sigma);
            for (int x = 0; x < n; x++)
            {
                double xTop = x - radius;
                for (int y = 0; y < n; y++)
                {
                    double yTop = y - radius;
                    double co = scalar * Math.Pow(Math.E, -(xTop * xTop + yTop * yTop) / (2 * sigma * sigma));
                    kernel[x, y] = co;
                }
            }
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    double totalWeight = 0;
                    double[] weightedSum = new double[img.Bands];
                    for(int r = 0;  r < n; r++)
                    {
                        for (int c = 0; c < n; c++)
                        {
                            int curRow = row - radius + r;
                            int curCol = col - radius + c;
                            if(curRow < 0 || curRow >= img.Height || curCol < 0 || curCol >= img.Width || img.IsInvalid(curRow, curCol))
                            {
                                continue;
                            }
                            double co = kernel[r, c];
                            totalWeight += co;
                            for (int b = 0; b < img.Bands; b++)
                            {
                                weightedSum[b] += tmp[b, curRow, curCol] * co;
                            }
                        }
                    }
                    if (totalWeight != 0)
                    {
                        double normalizeRatio = 1 / totalWeight;
                        for (int b = 0; b < img.Bands; b++)
                        {
                            img[b, row, col] = (float)(weightedSum[b] * normalizeRatio);
                        }
                    }
                }
            }

        }
    }
}
