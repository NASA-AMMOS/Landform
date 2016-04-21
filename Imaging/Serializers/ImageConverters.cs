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
        public static IImageConverter MinMaxToNormalizedImage = new MinMax2NormalizedImage();
        public static IImageConverter MinMaxToValueRange = new MinMax2ValueRange();

        private class ValueRange2NormalizedImage : IImageConverter
        {
            public Image Convert<T>(Image image)
            {
                throw new NotImplementedException();
            }
        }

        private class NormalizedImage2ValueRange : IImageConverter
        {
            public Image Convert<T>(Image image)
            {
                throw new NotImplementedException();
            }
        }

    }
}
