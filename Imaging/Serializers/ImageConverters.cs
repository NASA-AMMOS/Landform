using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{


    public class ImageConverters
    {
        public static IImageConverter ValueRangeToNormalizedImage = new ValueRange2NormalizedImage();
        public static IImageConverter NormalizedImageToValueRange = new NormalizedImage2ValueRange();
        public static IImageConverter PassThrough = new ValuePassThrough();


        private class ValueRange2NormalizedImage : IImageConverter
        {
            /// <summary>
            /// Returns a copy of an image normaized between 0-1
            /// Assumes input values range from 0-MaxValue for most types
            /// No scaling is performed on float or double types
            /// </summary>
            /// <typeparam name="T">Type used to determine the input value range</typeparam>
            /// <param name="image"></param>
            /// <returns></returns>
            public Image Convert<T>(Image image)
            {
                Image converted = (Image)image.Clone();
                float maxValue = 0;
                if (typeof(T) == typeof(byte))
                {
                    maxValue = byte.MaxValue;
                }
                else if (typeof(T) == typeof(short))
                {
                    maxValue = short.MaxValue;
                }
                else if (typeof(T) == typeof(ushort))
                {
                    maxValue = ushort.MaxValue;
                }
                else if (typeof(T) == typeof(int))
                {
                    maxValue = int.MaxValue;
                }
                else if (typeof(T) == typeof(uint))
                {
                    maxValue = uint.MaxValue;
                }
                if (typeof(T) != typeof(float) && typeof(T) != typeof(double))
                {
                    converted.ScaleValues(0, maxValue, 0, 1);
                }
                return converted;
            }
        }

        private class NormalizedImage2ValueRange : IImageConverter
        {
            /// <summary>
            /// Returns a copy of an image with values ranging from 0 to T.MaxValue
            /// Assumes input image values are normalized 0-1 for most types
            /// No scaling is performed on float or double types
            /// </summary>
            /// <typeparam name="T">Type used to deetermine the output value range</typeparam>
            /// <param name="image"></param>
            /// <returns></returns>
            public Image Convert<T>(Image image)
            {
                Image converted = (Image)image.Clone();
                float maxValue = 0;
                if (typeof(T) == typeof(byte))
                {
                    maxValue = byte.MaxValue;
                }
                else if (typeof(T) == typeof(short))
                {
                    maxValue = short.MaxValue;
                }
                else if (typeof(T) == typeof(ushort))
                {
                    maxValue = ushort.MaxValue;
                }
                else if (typeof(T) == typeof(int))
                {
                    maxValue = int.MaxValue;
                }
                else if (typeof(T) == typeof(uint))
                {
                    maxValue = uint.MaxValue;
                }
                if (typeof(T) != typeof(float) && typeof(T) != typeof(double))
                {
                    converted.ScaleValues(0, 1, 0, maxValue);
                }
                return converted;
            }
        }

        private class ValuePassThrough : IImageConverter
        {
            /// <summary>
            /// Simply copies the image and returns it without modifying the values
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="image"></param>
            /// <returns></returns>
            public Image Convert<T>(Image image)
            {
                Image converted = (Image)image.Clone();
                return converted;
            }
        }
    }
}
