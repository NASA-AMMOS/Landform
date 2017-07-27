using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;
using System.IO;
using OPS.Test;

namespace ImagingTest
{
    [TestClass]
    [DeploymentItem("gdal", "gdal")]
    [DeploymentItem("TestData", "TestData")]
    public class GDALSerializerTest
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

        [TestMethod]       
        public void GDALTestReadPattern()
        {
            Image pattern = new GDALSeralizer().Read(Path.Combine("TestData", "img", "testPattern.png"), ImageConverters.PassThrough);
            TestPatternColors(pattern, false);
        }

        [TestMethod]
        public void GDALTestReadPatternScaled()
        {
            Image pattern = new GDALSeralizer().Read(Path.Combine("TestData", "img", "testPattern.png"), ImageConverters.ValueRangeToNormalizedImage);
            TestPatternColors(pattern, true);
        }

        [TestMethod]
        public void GDALTestReadWriteTiff()
        {
            Image pattern = new GDALSeralizer().Read(Path.Combine("TestData", "img", "testPattern.png"), ImageConverters.ValueRangeToNormalizedImage);

            new GDALSeralizer().Write<byte>("gdalTestWriteByte.tif", pattern, ImageConverters.NormalizedImageToValueRange);
            TestPatternColors(new GDALSeralizer().Read("gdalTestWriteByte.tif", ImageConverters.ValueRangeToNormalizedImage), true);

            new GDALSeralizer().Write<ushort>("gdalTestWriteUshort.tif", pattern, ImageConverters.NormalizedImageToValueRange);
            TestPatternColors(new GDALSeralizer().Read("gdalTestWriteUshort.tif", ImageConverters.ValueRangeToNormalizedImage), true);

            new GDALSeralizer().Write<float>("gdalTestWriteFloat.tif", pattern, ImageConverters.NormalizedImageToValueRange);
            TestPatternColors(new GDALSeralizer().Read("gdalTestWriteFloat.tif", ImageConverters.ValueRangeToNormalizedImage), true);

            new GDALSeralizer().Write<double>("gdalTestWriteDouble.tif", pattern, ImageConverters.NormalizedImageToValueRange);
            TestPatternColors(new GDALSeralizer().Read("gdalTestWriteDouble.tif", ImageConverters.ValueRangeToNormalizedImage), true);
        }

        [TestMethod]
        public void GDALTestReadWritePNG()
        {
            Image pattern = new GDALSeralizer().Read(Path.Combine("TestData", "img", "testPattern.png"), ImageConverters.ValueRangeToNormalizedImage);

            new GDALSeralizer().Write<byte>("gdalTestWriteByte.png", pattern, ImageConverters.NormalizedImageToValueRange);
            TestPatternColors(new GDALSeralizer().Read("gdalTestWriteByte.png", ImageConverters.ValueRangeToNormalizedImage), true);

            new GDALSeralizer().Write<ushort>("gdalTestWriteUshort.png", pattern, ImageConverters.NormalizedImageToValueRange);
            TestPatternColors(new GDALSeralizer().Read("gdalTestWriteUshort.png", ImageConverters.ValueRangeToNormalizedImage), true);                      
        }

        [TestMethod]
        public void GDALTestReadWriteJPG()
        {
            Image pattern = new GDALSeralizer().Read(Path.Combine("TestData", "img", "testPattern.png"), ImageConverters.ValueRangeToNormalizedImage);

            GDALWriteOptions writeOptions = new GDALJPGWriteOptions(jpgQuality: 100);
            new GDALSeralizer(writeOptions).Write<byte>("gdalTestWriteByte.jpg", pattern, ImageConverters.NormalizedImageToValueRange);
            TestPatternColors(new GDALSeralizer().Read("gdalTestWriteByte.jpg", ImageConverters.ValueRangeToNormalizedImage), true, 0.15);
        }

        [TestMethod]
        public void GDALTestReadWriteBMP()
        {
            Image pattern = new GDALSeralizer().Read(Path.Combine("TestData", "img", "testPattern.png"), ImageConverters.ValueRangeToNormalizedImage);

            new GDALSeralizer().Write<byte>("gdalTestWriteByte.bmp", pattern, ImageConverters.NormalizedImageToValueRange);
            TestPatternColors(new GDALSeralizer().Read("gdalTestWriteByte.bmp", ImageConverters.ValueRangeToNormalizedImage), true);
        }


        [TestMethod]
        public void GDALTestFillValue()
        {
            Image pattern = new GDALSeralizer().Read(Path.Combine("TestData", "img", "testPattern.png"), ImageConverters.ValueRangeToNormalizedImage);
            pattern.CreateMask();
            for (int r = 30; r < 100; r++)
            {
                for (int c = 70; c < 80; c++)
                {
                    pattern.SetMaskValue(r, c, true);
                }
            }

            new GDALSeralizer().Write<byte>("gdalTestFillValue.tif", pattern, ImageConverters.NormalizedImageToValueRange, new float[] { 2, 255, 1 });
            Image maskedImg = new GDALSeralizer().Read("gdalTestFillValue.tif", ImageConverters.ValueRangeToNormalizedImage, new float[] { 2, 255, 1 });
            for (int r = 0; r < pattern.Height; r++)
            {
                for (int c = 0; c < pattern.Width; c++)
                {
                    bool shouldBeMasked = r >= 30 && r < 100 && c >= 70 && c < 80;
                    Assert.AreEqual(shouldBeMasked, maskedImg.IsInvalid(r, c));

                }
            }

            Image nonMaskedImg = new GDALSeralizer().Read("gdalTestFillValue.tif", ImageConverters.ValueRangeToNormalizedImage);
            for (int r = 0; r < pattern.Height; r++)
            {
                for (int c = 0; c < pattern.Width; c++)
                {
                    bool shouldBegreen = r >= 30 && r < 100 && c >= 70 && c < 80;
                    if(shouldBegreen)
                    {
                        AssertE.AreSimilar(nonMaskedImg[0, r, c], 2.0/255, 0.0001);
                        AssertE.AreSimilar(nonMaskedImg[1, r, c], 1, 0.0001);
                        AssertE.AreSimilar(nonMaskedImg[2, r, c], 1.0/255, 0.0001);
                    }
                    Assert.IsFalse(nonMaskedImg.IsInvalid(r, c));
                }
            }
        }
    }
}
