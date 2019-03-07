using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OPS.Imaging;
using OPS.MathExtensions;
using System.IO;

namespace OPS.Imaging
{
    /// <summary>
    /// This is the primary image class.  It stores data in a floating point format
    /// to enable generalized operations on a large variety of image types.
    /// 
    /// Common image operations should be implemented here
    /// 
    /// Normalized forms:
    /// RGB values are represented in normalized 0-1 form
    /// LAB values are represented in their own wierd range
    /// Position values are represented as XYZ coordinates
    /// Grayscale values are represented 0-1 and may optionally be replicated between bands
    /// 
    /// </summary>
    public class Image : GenericImage<float>
    {

        /// <summary>
        /// Creates a new blank image with the specified resolution and bands
        /// </summary>
        /// <param name="bands"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public Image(int bands, int width, int height) : base(bands, width, height) { }


        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="that"></param>
        public Image(Image that) : base(that) { }

        /// <summary>
        /// Load an image using default serializer and converter
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static Image Load(string filename)
        {
            string ext = Path.GetExtension(filename);
            ImageSerializer s = ImageSerializers.Instance.GetSerializer(ext);
            if (s == null)
            {
                throw new ImageSerializationException("Image format not supported");
            }
            return s.Read(filename);
        }

        /// <summary>
        /// Load an image using with the given converter
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static Image Load(string filename, IImageConverter converter)
        {
            string ext = Path.GetExtension(filename);
            ImageSerializer s = ImageSerializers.Instance.GetSerializer(ext);
            if (s == null)
            {
                throw new ImageSerializationException("Image format not supported");
            }
            return s.Read(filename, converter);
        }

        /// <summary>
        /// Loads a new image with the given serializer and converter
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="serializer"></param>
        /// <param name="converter"></param>
        public static Image Load(string filename, ImageSerializer serializer, IImageConverter converter)
        {
            return serializer.Read(filename, converter);
        }

        /// <summary>
        /// Saves image to disk using gdal and convert from normalzied values to value range
        /// </summary>
        /// <param name="filename"></param>
        public Image Save<T>(string filename)
        {
            string ext = Path.GetExtension(filename);
            ImageSerializer s = ImageSerializers.Instance.GetSerializer(ext);
            if (s == null)
            {
                throw new ImageSerializationException("Image format not supported");
            }
            s.Write<T>(filename, this);
            return this;
        }

        /// <summary>
        /// Saves image to disk with the provided converter
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="serializer"></param>
        /// <param name="converter"></param>
        public Image Save<T>(string filename, IImageConverter converter)
        {
            string ext = Path.GetExtension(filename);
            ImageSerializer s = ImageSerializers.Instance.GetSerializer(ext);
            if (s == null)
            {
                throw new ImageSerializationException("Image format not supported");
            }
            s.Write<T>(filename, this, converter);
            return this;
        }

        /// <summary>
        /// Saves image to disk
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="serializer"></param>
        /// <param name="converter"></param>
        public Image Save<T>(string filename, ImageSerializer serializer, IImageConverter converter)
        {
            serializer.Write<T>(filename, this, converter);
            return this;
        }

        /// <summary>
        /// Reflects an image vertically in place
        /// </summary>
        public Image FlipVertical()
        {
            int swapRow = this.Height - 1;
            for (int r = 0; r < swapRow; r++, swapRow--)
            {
                for (int b = 0; b < this.Bands; b++)
                {
                    for (int c = 0; c < this.Width; c++)
                    {
                        float curV = this[b, r, c];
                        this[b, r, c] = this[b, swapRow, c];
                        this[b, swapRow, c] = curV;
                    }
                }
            }
            return this;
        }

        /// <summary>
        /// Linearly scale values in a band in place
        /// </summary>
        /// <param name="band">band to scale</param>
        /// <param name="beforeMin">any pixles currently at this value will be mapped to afterMin</param>
        /// <param name="beforeMax">any pixels currently at this value will be mapped to afterMax</param>
        /// <param name="afterMin">the new min value for this band</param>
        /// <param name="afterMax">the new max value for this band</param>
        public Image ScaleValues(int band, float beforeMin, float beforeMax, float afterMin, float afterMax)
        {
            if (beforeMax == beforeMin)
            {
                throw new Exception("Cannot ScaleValues when beforeMin and beforeMax are the same");
            }
            float beforeRange = beforeMax - beforeMin;
            float afterRange = afterMax - afterMin;
            ApplyInPlace(band, x =>
            {
                float amount = (x - beforeMin) / beforeRange;
                float result = MathE.Clamp(afterMin + afterRange * amount, afterMin, afterMax);
                return result;
            });
            return this;
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
        public Image ScaleValues(float beforeMin, float beforeMax, float afterMin, float afterMax)
        {
            for (int b = 0; b < this.Bands; b++)
            {
                ScaleValues(b, beforeMin, beforeMax, afterMin, afterMax);
            }
            return this;
        }

        /// <summary>
        /// Given an image with a mask, extend the image and the mask by border pixels in place
        /// If border is negative (the default) continue inpainting until there are no
        /// masked pixels left.  Inpainted pixels are an average of their non-masked neighbors
        /// </summary>
        /// <param name="border"></param>
        public Image Inpaint(int border = -1)
        {
            Inpainter.Apply(this, border);
            return this;
        }

        /// <summary>
        /// Performs a deep copy of the image and all associated objects
        /// </summary>
        /// <returns></returns>
        public new object Clone()
        {
            return new Image(this);
        }

        /// <summary>
        /// Stretch the color channles of an image based the standard deviation of its values in place
        /// The resulting image will have its values normalzied between 0 and 1
        /// NOTE that bands with no variance (ie all the same value) will not be scaled and could be outside the 0-1 range
        /// NOTE masked values are also not scaled and could remain outside the 0-1 range
        /// </summary>
        /// <param name="nStdev">Number of standard deviations from the mean to place the upper and lower values of the stretch</param>
        public Image ApplyStdDevStretch(double nStdev = 3)
        {
            ImageStatistics stats = new ImageStatistics(this);
            for (int b = 0; b < this.Bands; b++)
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
                    ScaleValues(b, (float)min, (float)max, 0, 1);
                }
            }
            return this;
        }

        /// <summary>
        /// Crop the source image to the specified dimensions.  Return a new image of the cropped area.
        /// This method does not retain metadata or camera model.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="startRow"></param>
        /// <param name="startCol"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public Image Crop(int startRow, int startCol, int width, int height)
        {
            Image result = new Image(this.Bands, width, height);
            if (this.HasMask)
            {
                result.CreateMask();
            }
            foreach (ImageCoordinate ic in result.Coordinates(true))
            {
                result[ic.Band, ic.Row, ic.Col] = this[ic.Band, ic.Row + startRow, ic.Col + startCol];
                if (this.HasMask)
                {
                    result.SetMaskValue(ic.Row, ic.Col, this.IsInvalid(ic.Row + startRow, ic.Col + startCol));
                }
            }
            return result;
        }

        /// <summary>
        /// Simulate a guassian blur with a series of box blurs in place
        /// </summary>
        /// <param name="r"></param>
        public Image GuassianBoxBlur(int r)
        {
            Blur.GuassianBoxBlur(this, r);
            return this;
        }

        public float BilinearSample(int band, float row, float col)
        {
            int irow, icol;
            float rfrac, cfrac;
            float row1 = 0, row2 = 0;

            irow = (int)row;
            icol = (int)col;

            if (irow < 0 || irow >= Height || icol < 0 || icol >= Width) { return 0; }

            row = Math.Min(row, Height - 1);
            col = Math.Min(col, Width - 1);

            rfrac = (float)(1.0 - (row - irow));
            cfrac = (float)(1.0 - (col - icol));

            if (cfrac < 1)
            {
                row1 = cfrac * this[band, irow, icol] + (1.0f - cfrac) * this[band, irow, icol + 1];
            }
            else
            {
                row1 = this[band, irow, icol];
            }

            if (rfrac < 1)
            {
                if (cfrac < 1)
                {
                    row2 = cfrac * this[band, irow + 1, icol] + (1.0f - cfrac) * this[band, irow + 1, icol + 1];
                }
                else
                {
                    row2 = this[band, irow + 1, icol];
                }
            }
            return rfrac * row1 + (1f - rfrac) * row2;
        }

        /// <summary>
        /// Image resizing based on 
        /// http://entropymine.com/imageworsener/resample/
        /// TODO this does not respect the image mask, if any
        /// https://github.jpl.nasa.gov/OnSight/Landform/issues/430
        /// </summary>
        public Image Resize(int targetWidth, int targetHeight, FilterDelegate filter = null)
        {
            if (filter == null)
            {
                // Default to Catmull-Rom for downsampling, Mitchell for upsampling
                if (targetWidth <= Width && targetHeight <= Height)
                {
                    filter = CatmullRomFilter;
                }
                else
                {
                    filter = MitchellFilter;

                }
            }

            Image horizontalResult = new Image(this.Bands, targetWidth, this.Height);

            List<Weight> weights = GetResizeWeights(targetWidth, this.Width, 2, filter);

            for (int band = 0; band < Bands; band++)
            {
                for (int row = 0; row < this.Height; row++)
                {
                    foreach (Weight w in weights)
                    {
                        float source = this.ReadClampedToBounds(band, w.inPixel, row);
                        horizontalResult[band, row, w.outPixel] += source * (float)w.weight;
                    }
                }
            }

            //resize vertically 
            Image result = new Image(this.Bands, targetWidth, targetHeight);

            weights = GetResizeWeights(targetHeight, this.Height, 2, filter);

            for (int band = 0; band < Bands; band++)
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
        /// Rotate an image 90 degrees clockwise
        /// </summary>
        /// <returns></returns>
        public Image Rotate90Clockwise()
        {
            Image result = new Image(this.Bands, this.Height, this.Width);
            for (int r = 0; r < this.Height; r++)
            {
                for (int c = 0; c < this.Width; c++)
                {
                    result.SetBandValues(c, this.Height - 1 - r, this.GetBandValues(r, c));
                }
            }
            return result;
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
        /// Function type for resize filters
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        public delegate double FilterDelegate(double x);

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
        List<Weight> GetResizeWeights(int target, int current, int radius, FilterDelegate f)
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

        #region Filters
        public double QuadraticFilter(double x)
        {
            x = Math.Abs(x);
            if (x < 0.5) return 0.75 - x * x;
            if (x < 1.5) return 0.50 * (x - 1.5) * (x - 1.5);
            return 0.0;
        }

        public double BoxFilter(double x)
        {
            x = Math.Abs(x);
            if (x <= 0.5) return 1;
            return 0;
        }

        double TriangleFilter(double xval)
        {
            xval = Math.Abs(xval);
            if (xval <= 1)
            {
                return 1 - xval;
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
        public static readonly FilterDelegate CatmullRomFilter = MakeCubicFilter(0, 0.5);
        public static readonly FilterDelegate MitchellFilter = MakeCubicFilter(1 / 3.0, 1 / 3.0);
        public static readonly FilterDelegate BSplineFilter = MakeCubicFilter(1, 0);

        #endregion Filters

        /// <summary>
        /// Resize an image to the target width using a simple bicubic function
        /// Considering using Resize() instead
        /// TODO this does not respect the image mask, if any
        /// https://github.jpl.nasa.gov/OnSight/Landform/issues/430
        /// </summary>
        /// <param name="targetWidth"></param>
        /// <param name="targetHeight"></param>
        /// <returns></returns>
        public Image ResizeSimpleBicubic(int targetWidth, int targetHeight)
        {
            Image result = new Image(this.Bands, targetWidth, targetHeight);
            float wRatio = (this.Width - 1) / ((float)result.Width - 1);
            float hRatio = (this.Height - 1) / ((float)result.Height - 1);
            foreach (ImageCoordinate ic in result.Coordinates(true))
            {
                result[ic.Band, ic.Row, ic.Col] = BicubicSample(ic.Band, ic.Row * hRatio, ic.Col * wRatio);
            }
            return result;
        }

        /// <summary>
        /// Sample a pixel
        /// </summary>
        /// <param name="b"></param>
        /// <param name="row"></param>
        /// <param name="col"></param>
        /// <returns></returns>
        public float BicubicSample(int b, float row, float col)
        {
            var x = col;
            var y = row;

            var x1 = (int)x;
            var y1 = (int)y;
            var x2 = x1 + 1;
            var y2 = y1 + 1;

            var p00 = ReadClampedToBounds(b, x1 - 1, y1 - 1);
            var p01 = ReadClampedToBounds(b, x1 - 1, y1);
            var p02 = ReadClampedToBounds(b, x1 - 1, y2);
            var p03 = ReadClampedToBounds(b, x1 - 1, y2 + 1);

            var p10 = ReadClampedToBounds(b, x1, y1 - 1);
            var p11 = ReadClampedToBounds(b, x1, y1);
            var p12 = ReadClampedToBounds(b, x1, y2);
            var p13 = ReadClampedToBounds(b, x1, y2 + 1);

            var p20 = ReadClampedToBounds(b, x2, y1 - 1);
            var p21 = ReadClampedToBounds(b, x2, y1);
            var p22 = ReadClampedToBounds(b, x2, y2);
            var p23 = ReadClampedToBounds(b, x2, y2 + 1);

            var p30 = ReadClampedToBounds(b, x2 + 1, y1 - 1);
            var p31 = ReadClampedToBounds(b, x2 + 1, y1);
            var p32 = ReadClampedToBounds(b, x2 + 1, y2);
            var p33 = ReadClampedToBounds(b, x2 + 1, y2 + 1);

            return Bicubic(
                x - x1
              , y - y1
              , p00, p10, p20, p30
              , p01, p11, p21, p31
              , p02, p12, p22, p32
              , p03, p13, p23, p33
            );
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
        float Bicubic(float xf, float yf,
                      float p00, float p01, float p02, float p03,
                      float p10, float p11, float p12, float p13,
                      float p20, float p21, float p22, float p23,
                      float p30, float p31, float p32, float p33
)
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
        /// </summary>
        /// <param name="b"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        float ReadClampedToBounds(int b, float x, float y)
        {
            int row = (int)MathE.Clamp(y, 0, this.Height - 1);
            int col = (int)MathE.Clamp(x, 0, this.Width - 1);
            return this[b, row, col];
        }

        /// <summary>
        /// decimate by averaging square blocks
        /// respects image mask, if any
        /// resulting image will have mask set for any source block that had no valid pixels
        /// does not mutate source image
        /// </summary>
        public Image Decimated(int blocksize)
        {
            if (blocksize == 1)
            {
                return (Image)Clone();
            }

            int targetWidth = Width / blocksize; //integer math
            int targetHeight = Height / blocksize; //integer math

            Image result = new Image(this.Bands, targetWidth, targetHeight);
            result.CreateMask(false);

            for (int band = 0; band < Bands; band++)
            {
                for (int dstRow = 0; dstRow < targetHeight; dstRow++)
                {
                    for (int dstCol = 0; dstCol < targetWidth; dstCol++)
                    {
                        int n = 0;
                        float sum = 0;
                        for (int srcRow = dstRow*blocksize; srcRow < (dstRow + 1)*blocksize; srcRow++)
                        {
                            if (srcRow >= 0 && srcRow < this.Height)
                            {
                                for (int srcCol = dstCol*blocksize; srcCol < (dstCol + 1)*blocksize; srcCol++)
                                {
                                    if (srcCol >= 0 && srcCol < this.Width)
                                    {
                                        if (!IsInvalid(srcRow, srcCol))
                                        {
                                            sum += this[band, srcRow, srcCol];
                                            n++;
                                        }
                                    }
                                }
                            }
                        }
                        if (n > 0)
                        {
                            result[band, dstRow, dstCol] = sum / n;
                        }
                        else
                        {
                            result.SetMaskValue(dstRow, dstCol, true);
                        }
                    }
                }
            }
            return result;
        }
    }
}
