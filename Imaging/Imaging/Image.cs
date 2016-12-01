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
            float beforeRange = beforeMax - beforeMin;
            float afterRange = afterMax - afterMin;
            ApplyInPlace(x =>
            {
                float amount = (x - beforeMin) / beforeRange;
                float result = MathE.Clamp(afterMin + afterRange * amount, afterMin, afterMax);
                return result;
            });
        }

        /// <summary>
        /// Performs a deep copy of the image and all associated objects
        /// </summary>
        /// <returns></returns>
        public new object Clone()
        {
            return new Image(this);
        }


    }
}
