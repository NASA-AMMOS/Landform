//#define ENABLE_GDAL_JPG_PNG_BMP

using System;
using System.IO;
using OPS.Imaging;
using OPS.Test;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ImagingTest
{
    [TestClass]
    [DeploymentItem("TestData", "TestData")]
    public class ImageSharpSerializerTest
    {
        void TestColor(Image img, int row, int col, double r, double g, double b, double eps)
        {            
            AssertE.AreSimilar(img[0, row, col], r, eps);
            AssertE.AreSimilar(img[1, row, col], g, eps);
            AssertE.AreSimilar(img[2, row, col], b, eps);
        }

        void TestPatternColors(Image pattern, bool scaleToZeroOne, double eps = 0.00001)
        {
            float d = scaleToZeroOne ? 255 : 1;
            TestColor(pattern, 10, 10, 255/d, 255/d, 255/d, eps);
            TestColor(pattern, 4, 135, 0, 0, 0, eps);
            TestColor(pattern, 220, 190, 191/d, 191/d, 0, eps);
            TestColor(pattern, 220, 500, 191/d, 0, 0, eps);
            TestColor(pattern, 539, 700, 210/d, 218/d, 173/d, eps);
        }

#if !ENABLE_GDAL_JPG_PNG_BMP
        [TestMethod]
        public void ImageSharpTestReadWritePNG()
        {
            Image pattern = new ImageSharpSerializer().Read(Path.Combine("TestData", "img", "testPattern.png"),
                                                            ImageConverters.ValueRangeToNormalizedImage);
            new ImageSharpSerializer().Write<byte>("imageSharpTestWriteByte.png", pattern,
                                                   ImageConverters.NormalizedImageToValueRange);
            TestPatternColors(new ImageSharpSerializer().Read("imageSharpTestWriteByte.png",
                                                              ImageConverters.ValueRangeToNormalizedImage), true);

            new ImageSharpSerializer().Write<ushort>("imageSharpTestWriteUshort.png", pattern,
                                                     ImageConverters.NormalizedImageToValueRange);
            TestPatternColors(new ImageSharpSerializer().Read("imageSharpTestWriteUshort.png",
                                                              ImageConverters.ValueRangeToNormalizedImage), true);
        }

        [TestMethod]
        public void ImageSharpTestReadWriteJPG()
        {
            Image pattern = new ImageSharpSerializer().Read(Path.Combine("TestData", "img", "testPattern.png"),
                                                            ImageConverters.ValueRangeToNormalizedImage);

            new ImageSharpSerializer().Write<byte>("imageSharpTestWriteByte.jpg", pattern,
                                                               ImageConverters.NormalizedImageToValueRange);
            TestPatternColors(new ImageSharpSerializer().Read("imageSharpTestWriteByte.jpg",
                                                              ImageConverters.ValueRangeToNormalizedImage), true, 0.15);
        }

        [TestMethod]
        public void ImageSharpTestReadWriteBMP()
        {
            Image pattern = new ImageSharpSerializer().Read(Path.Combine("TestData", "img", "testPattern.png"),
                                                            ImageConverters.ValueRangeToNormalizedImage);

            new ImageSharpSerializer().Write<byte>("imageSharpTestWriteByte.bmp", pattern,
                                                   ImageConverters.NormalizedImageToValueRange);
            TestPatternColors(new ImageSharpSerializer().Read("imageSharpTestWriteByte.bmp",
                                                              ImageConverters.ValueRangeToNormalizedImage), true);
        }
#endif
    }
}
