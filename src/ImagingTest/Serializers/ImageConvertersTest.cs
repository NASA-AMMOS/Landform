using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Imaging;


namespace ImagingTest
{
    [TestClass]
    public class ImageConvertersTest
    {

        void ConversionTestHelper<T>(IImageConverter converter,  float[] input, float[] output)
        {
            for (int i = 0; i < input.Length; i++)
            {
                Image img = new Image(1, 1, 1);
                img[0, 0, 0] = input[i];
                var r = converter.Convert<T>(img);
                var expected = output[i];
                var actual = r[0, 0, 0];
                Assert.AreEqual(expected, actual);
            }
        }

        [TestMethod]
        public void TestValueRange2NormalizedImage()
        {
            // byte 
            {
                float[] input = { -1, 0, 1, 127, 255, 256 };
                float[] output = { 0, 0, 1f / 255, 127f / 255, 1, 1 };
                ConversionTestHelper<byte>(ImageConverters.ValueRangeToNormalizedImage, input, output);                
            }
            // short 
            {
                float[] input = { -1, 0, 1, 2043, 32767, 62767 };
                float[] output = { 0, 0, 1f / 32767, 2043f / 32767, 1, 1 };
                ConversionTestHelper<short>(ImageConverters.ValueRangeToNormalizedImage, input, output);
            }
            // ushort 
            {
                float[] input = { -1, 0, 1, 2043, 65535, 95535 };
                float[] output = { 0, 0, 1f / 65535, 2043f / 65535, 1, 1 };
                ConversionTestHelper<ushort>(ImageConverters.ValueRangeToNormalizedImage, input, output);
            }
            // int 
            {
                float[] input = { -1, 0, 1, 2043, 2147483647, 2147483648 };
                float[] output = { 0, 0, 1f / 2147483647, 2043f / 2147483647, 1, 1 };
                ConversionTestHelper<int>(ImageConverters.ValueRangeToNormalizedImage, input, output);
            }
            // uint 
            {
                float[] input = { -1, 0, 1, 2043, 4294967295, 4294967296 };
                float[] output = { 0, 0, 1f / 4294967295, 2043f / 4294967295, 1, 1 };
                ConversionTestHelper<uint>(ImageConverters.ValueRangeToNormalizedImage, input, output);
            }
            // float 
            {
                float[] input = { -1, 0, 1, 2043, 65535, 95535 };
                float[] output = { -1, 0, 1, 2043, 65535, 95535 };
                ConversionTestHelper<float>(ImageConverters.ValueRangeToNormalizedImage, input, output);
            }
        }


        [TestMethod]
        public void TestNormalizedImage2ValueRange()
        {
            // byte 
            {
                float[] input = { -1, 0, 1f / 255, 127f / 255, 1, 1.1f };
                float[] output = { 0, 0, 1, 127, 255, 255 };
                ConversionTestHelper<byte>(ImageConverters.NormalizedImageToValueRange, input, output);
            }
            // short 
            {
                float[] input = { -1, 0, 1f / 32767, 2043f / 32767, 1, 1.1f };
                float[] output = { 0, 0, 1, 2043, 32767, 32767 };
                ConversionTestHelper<short>(ImageConverters.NormalizedImageToValueRange, input, output);
            }
            // ushort 
            {
                float[] input = { -1, 0, 1f / 65535, 2043f / 65535, 1, 2 };
                float[] output = { 0, 0, 1, 2043, 65535, 65535 };
                ConversionTestHelper<ushort>(ImageConverters.NormalizedImageToValueRange, input, output);
            }
            // int 
            {
                float[] input = { -1, 0, 1f / 2147483647, 2043f / 2147483647, 1, 2 };
                float[] output = { 0, 0, 1, 2043, 2147483647, 2147483647 };
                ConversionTestHelper<int>(ImageConverters.NormalizedImageToValueRange, input, output);
            }
            // uint 
            {
                float[] input = { -1, 0, 1f / 4294967295, 2043f / 4294967295, 1, 2 };
                float[] output = { 0, 0, 1, 2043, 4294967295, 4294967295 };
                ConversionTestHelper<uint>(ImageConverters.NormalizedImageToValueRange, input, output);
            }
            // float 
            {
                float[] input = { -1, 0, 1, 2043, 65535, 95535 };
                float[] output = { -1, 0, 1, 2043, 65535, 95535 };
                ConversionTestHelper<float>(ImageConverters.NormalizedImageToValueRange, input, output);
            }


        }


        [TestMethod]
        public void TestValuePassThrough()
        {
            // byte 
            {
                float[] input = { -1, 0, 1, 2043, 65535, 95535 };
                float[] output = { -1, 0, 1, 2043, 65535, 95535 };
                ConversionTestHelper<byte>(ImageConverters.PassThrough, input, output);
            }

        }

    }
}
