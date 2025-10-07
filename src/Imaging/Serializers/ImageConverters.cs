using System;

namespace JPLOPS.Imaging
{
    public class ImageConverters
    {
        public static IImageConverter ValueRangeToNormalizedImage = new ValueRange2NormalizedImage();
        public static IImageConverter NormalizedImageToValueRange = new NormalizedImage2ValueRange();
        public static IImageConverter ValueRangeSRGBToNormalizedImageLinearRGB = new ValueRange2NormalizedImage(true);
        public static IImageConverter NormalizedImageLinearRGBToValueRangeSRGB = new NormalizedImage2ValueRange(true);
        public static IImageConverter AbsNormalizedImageToValueRange = new AbsNormalizedImage2ValueRange();
        public static IImageConverter PassThrough = new ValuePassThrough();
        public static IImageConverter PDSBitMaskValueRangeToNormalizedImage = new BitMaskValueRangeToNormalizedImage();

        private class ValueRange2NormalizedImage : IImageConverter
        {
            private readonly bool byteDataIssRGB;

            public ValueRange2NormalizedImage(bool byteDataIssRGB = false)
            {
                this.byteDataIssRGB = byteDataIssRGB;
            }

            /// <summary>
            /// Returns a copy of an image normalized between 0-1
            /// Assumes input values range from 0-MaxValue for most types
            /// No scaling is performed on float or double types
            /// </summary>
            /// <typeparam name="T">Type used to determine the input value range</typeparam>
            /// <param name="image"></param>
            /// <returns></returns>
            public Image Convert<T>(Image image)
            {
                Image converted = (Image)image.Clone();
                float max = MaxValueForType<T>();
                bool sRGBToLinear = typeof(T) == typeof(byte) && byteDataIssRGB;
                if (typeof(T) != typeof(float) && typeof(T) != typeof(double))
                {
                    converted.ScaleValues(0, max, 0, 1);
                    if (sRGBToLinear)
                    {
                        Colorspace.SRGBToLinearRGB(converted);
                    }
                }
                return converted;
            }
        }

        private class NormalizedImage2ValueRange : IImageConverter
        {
            private readonly bool byteDataIssRGB;

            public NormalizedImage2ValueRange(bool byteDataIssRGB = false)
            {
                this.byteDataIssRGB = byteDataIssRGB;
            }

            /// <summary>
            /// Returns a copy of an image with values ranging from 0 to T.MaxValue
            /// Assumes input image values are normalized 0-1 for most types
            /// No scaling is performed on float or double types
            /// </summary>
            /// <typeparam name="T">Type used to determine the output value range</typeparam>
            /// <param name="image"></param>
            /// <returns></returns>
            public Image Convert<T>(Image image)
            {
                Image converted = (Image)image.Clone();
                float max = MaxValueForType<T>();
                bool linearTosRGB = typeof(T) == typeof(byte) && byteDataIssRGB;
                if (typeof(T) != typeof(float) && typeof(T) != typeof(double))
                {
                    if (linearTosRGB)
                    {
                        Colorspace.LinearRGBToSRGB(converted);
                    }
                    converted.ScaleValues(0, 1, 0, max);
                }
                return converted;
            }
        }

        private class AbsNormalizedImage2ValueRange : IImageConverter
        {
            /// <summary>
            /// Returns a copy of an image with values ranging from 0 to T.MaxValue
            /// Assumes input image values are normalized -1 to 1 for most types
            /// No scaling is performed on float or double types
            /// </summary>
            /// <typeparam name="T">Type used to determine the output value range</typeparam>
            /// <param name="image"></param>
            /// <returns></returns>
            public Image Convert<T>(Image image)
            {
                Image converted = (Image)image.Clone();
                if (typeof(T) != typeof(float) && typeof(T) != typeof(double))
                {
                    for (int band = 0; band < converted.Bands; band++)
                    {
                        converted.ApplyInPlace(band, v => (float)Math.Abs(v));
                    }
                    converted.ScaleValues(0, 1, 0, MaxValueForType<T>());
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

        private class BitMaskValueRangeToNormalizedImage : IImageConverter
        {
            /// <summary>
            /// Returns a copy of an image normalized between 0-1
            /// Assumes input image has PDSMetadata with a valid BitMask value
            /// Assumes input values range from 0-BitMaskValue
            /// Does not scale values when reading float or double images
            /// </summary>
            /// <typeparam name="T">Type used to determine the input value range</typeparam>
            /// <param name="image"></param>
            /// <returns></returns>
            public Image Convert<T>(Image image)
            {
                Image converted = (Image)image.Clone();
                PDSMetadata metadata = (PDSMetadata)converted.Metadata;
                if (typeof(T) != typeof(float) && typeof(T) != typeof(double))
                {
                    converted.ScaleValues(0, (float)metadata.BitMask, 0, 1);
                }
                return converted;
            }
        }

        private static float MaxValueForType<T>()
        {
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
            else if (typeof(T) == typeof(float))
            {
                maxValue = float.MaxValue;
            }
            return maxValue;
        }
    }
}
