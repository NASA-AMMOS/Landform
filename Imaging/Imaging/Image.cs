using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OPS.Imaging;
using OPS.MathExtensions;

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
        /// Load an image using gdal and normalize values based on type value range
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static Image Load(string filename)
        {
            if(filename.ToUpper().EndsWith(".IMG"))
            {
                return new PDSSeralizer().Read(filename, ImageConverters.PDSBitMaskValueRangeToNormalizedImage);
            }
            return new GDALSeralizer().Read(filename, ImageConverters.ValueRangeToNormalizedImage);
        }

        /// <summary>
        /// Loads a new image from a file
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="serializer"></param>
        /// <param name="converter"></param>
        public static Image Load(string filename, IImageSeralizer serializer, IImageConverter converter)
        {
            return serializer.Read(filename, converter);
        }
        
        /// <summary>
        /// Saves image to disk using gdal and convert from normalzied values to value range
        /// </summary>
        /// <param name="filename"></param>
        public void Save<T>(string filename)
        {
            if (filename.ToUpper().EndsWith(".IMG"))
            {
                new PDSSeralizer().Write<T>(filename, this, ImageConverters.NormalizedImageToValueRange);
                return;
            }
            new GDALSeralizer().Write<T>(filename, this, ImageConverters.NormalizedImageToValueRange);
        }

        /// <summary>
        /// Saves image to disk
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="serializer"></param>
        /// <param name="converter"></param>
        public void Save<T>(string filename, IImageSeralizer serializer, IImageConverter converter)
        {
            serializer.Write<T>(filename, this, converter);
        }
        
        /// <summary>
        /// Linearly scale values in this band
        /// </summary>
        /// <param name="band">band to scale</param>
        /// <param name="beforeMin">any pixles currently at this value will be mapped to afterMin</param>
        /// <param name="beforeMax">any pixels currently at this value will be mapped to afterMax</param>
        /// <param name="afterMin">the new min value for this band</param>
        /// <param name="afterMax">the new max value for this band</param>
        public void ScaleValues(int band, float beforeMin, float beforeMax, float afterMin, float afterMax)
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
        }

        /// <summary>
        /// Linearly scales values in the image from [beforeMin, beforeMax] to [afterMin, afterMax]
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
        public void ScaleValues(float beforeMin, float beforeMax, float afterMin, float afterMax)
        {
            for(int b = 0; b < this.Bands; b++)
            {
                ScaleValues(b, beforeMin, beforeMax, afterMin, afterMax);
            }
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
        /// Stretch the color channles of an image based the standard deviation of its values
        /// The resulting image will have its values normalzied between 0 and 1
        /// NOTE that bands with no variance (ie all the same value) will not be scaled and could be outside the 0-1 range
        /// NOTE masked values are also not scaled and could remain outside the 0-1 range
        /// </summary>
        /// <param name="nStdev">Number of standard deviations from the mean to place the upper and lower values of the stretch</param>
        public void ApplyStdDevStretch(double nStdev = 3)
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
            if(this.HasMask)
            {
                result.CreateMask();
            }
            foreach (ImageCoordinate ic in result.Coordinates(true))
            {
                result[ic.b, ic.r, ic.c] = this[ic.b, ic.r + startRow, ic.c + startCol];
                if(this.HasMask)
                {
                    result.SetMaskValue(ic.r, ic.c, this.IsMasked(ic.r + startRow, ic.c + startCol));
                }
            }
            return result;
        }

        /// <summary>
        /// Resize an image to the target width using a simple bicubic function
        /// A better option would be to do something closer to photoshop
        /// http://entropymine.com/resamplescope/notes/photoshop/
        /// </summary>
        /// <param name="targetWidth"></param>
        /// <param name="targetHeight"></param>
        /// <returns></returns>
        public Image ResizeSimpleBicubic(int targetWidth, int targetHeight)
        {
            Image result = new Image(this.Bands, targetWidth, targetHeight);
            float wRatio = (this.Width-1) / ((float)result.Width-1);
            float hRatio = (this.Height-1) / ((float)result.Height-1);
            foreach (ImageCoordinate ic in result.Coordinates(true))
            {
                result[ic.b, ic.r, ic.c] = BicubicSample(ic.b, ic.r * hRatio, ic.c * wRatio);        
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
    }
}
