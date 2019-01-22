using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;
using System.IO;
using OPS.Test;

namespace ImageTest
{
    [TestClass]
    [DeploymentItem("gdal", "gdal")]
    public class ImageTest
    {

        [TestMethod]
        public void TestImageConstructor()
        {
            Image img = new Image(2, 20, 30);
            img[1, 2, 3] = 7;
            Assert.AreEqual(img.Bands, 2);
            Assert.AreEqual(img.Width, 20);
            Assert.AreEqual(img.Height, 30);
            Assert.AreEqual(img.Metadata.Bands, 2);
            Assert.AreEqual(img.Metadata.Width, 20);
            Assert.AreEqual(img.Metadata.Height, 30);

            Image img2 = new Image(img);
            Assert.AreEqual(img2.Bands, 2);
            Assert.AreEqual(img2.Width, 20);
            Assert.AreEqual(img2.Height, 30);
            Assert.AreEqual(img2.Metadata.Bands, 2);
            Assert.AreEqual(img2.Metadata.Width, 20);
            Assert.AreEqual(img2.Metadata.Height, 30);
            Assert.AreEqual(img2[1, 2, 3], 7);
            img[1, 2, 4] = 2;
            Assert.AreEqual(img2[1, 2, 4], 0);
        }


        [TestMethod]
        public void ImageSaveLoad()
        {
            Image imgOrig = new Image(3, 20, 30);
            imgOrig[1, 2, 3] = 43f/255;
            imgOrig.Save<byte>("load.png");
            if (!File.Exists("load.png"))
            {
                Assert.Fail();
            }
            Image imgRead = Image.Load("load.png", new GDALSerializer(), ImageConverters.ValueRangeToNormalizedImage);
            Assert.AreEqual(imgOrig.Bands, imgRead.Bands);
            Assert.AreEqual(imgOrig.Width, imgRead.Width);
            Assert.AreEqual(imgOrig.Height, imgRead.Height);
            Assert.AreEqual(43f / 255, imgRead[1, 2, 3]);
        }


        void RoundOffHelper<T>(float maxValue)
        {
            Image imgOrig = new Image(3, 10, 10);
            imgOrig[0, 0, 0] = 0;
            imgOrig[0, 0, 1] = 0.5f;
            imgOrig[0, 0, 2] = 1;
            imgOrig[0, 0, 3] = (maxValue - 1) / maxValue;

            imgOrig.Save<T>("roundOff.tif");
            Image imgRead = Image.Load("roundOff.tif", new GDALSerializer(), ImageConverters.PassThrough);

            Assert.AreEqual(0, imgRead[0, 0, 0]);
            Assert.AreEqual(Math.Floor(maxValue / 2), imgRead[0, 0, 1]);
            Assert.AreEqual(maxValue, imgRead[0, 0, 2]);
            Assert.AreEqual(maxValue - 1, imgRead[0, 0, 3]);

            imgRead = Image.Load("roundOff.tif");
            Assert.AreEqual(0, imgRead[0, 0, 0]);
            Assert.IsTrue(Math.Abs(imgRead[0, 0, 1] - Math.Floor(maxValue / 2) / maxValue) < 0.00001f);
            Assert.AreEqual(1, imgRead[0, 0, 2]);
            Assert.IsTrue(Math.Abs(imgRead[0, 0, 3] - (maxValue - 1) / maxValue) < 0.00001f);

        }

        [TestMethod]
        public void ImageSaveLoadRoundoff()
        {
            RoundOffHelper<byte>(byte.MaxValue);
            RoundOffHelper<short>(short.MaxValue);
            RoundOffHelper<ushort>(ushort.MaxValue);
            RoundOffHelper<int>(int.MaxValue);

            Random rand = new Random();
            Image imgOrig = new Image(3, 10, 10);
            imgOrig.ApplyInPlace(x => rand.Next() / (float)int.MaxValue);

            // float
            {
                imgOrig.Save<float>("floatimg.tif");
                var imgRead = Image.Load("floatimg.tif");
                for (int b = 0; b < imgOrig.Data.Length; b++)
                {
                    for (int i = 0; i < imgOrig.Data[b].Length; i++)
                    {
                        Assert.AreEqual(imgRead.Data[b][i], imgOrig.Data[b][i]);
                    }
                }
            }
            // double
            {
                imgOrig.Save<double>("doubleimg.tif");
                var imgRead = Image.Load("doubleimg.tif");
                for (int b = 0; b < imgOrig.Data.Length; b++)
                {
                    for (int i = 0; i < imgOrig.Data[b].Length; i++)
                    {
                        Assert.AreEqual(imgRead.Data[b][i], imgOrig.Data[b][i]);
                    }
                }
            }
        }

        [TestMethod]
        public void BandScaleValues()
        {
            Image img = new Image(3, 2, 3);
            img[0, 0, 0] = 7;
            img[0, 0, 1] = 1;
            img[0, 0, 2] = 40;
            img.ScaleValues(0, 3, 20, -1, 0);
            AssertE.AreSimilar(-1 + (7 - 3) / (double)(20 - 3), img[0, 0, 0], 1E-5);
            Assert.AreEqual(-1, img[0, 0, 1]);
            Assert.AreEqual(0, img[0, 0, 2]);
        }

        [TestMethod]
        public void ImageScaleValues()
        {
            Image img = new Image(3, 2, 2);
            img[0, 0, 0] = 4;
            img[1, 0, 1] = 10;
            img[1, 1, 1] = 20;
            img[0, 0, 1] = -2;
            img.ScaleValues(0, 10, 20, 40);
            Assert.AreEqual(28, img[0, 0, 0]);
            Assert.AreEqual(40, img[1, 0, 1]);
            Assert.AreEqual(40, img[1, 1, 1]);
            Assert.AreEqual(20, img[0, 0, 1]);
        }

        [TestMethod]
        public void ImageStdStretch()
        {
            {
                Image img = new Image(3, 2, 2);
                img[0, 0, 0] = 4;
                img[1, 0, 1] = 10;
                img[1, 1, 1] = 20;
                img[0, 0, 1] = -2;
                img.ApplyStdDevStretch();
                Assert.AreNotEqual(4, img[0, 0, 0]);
                Assert.AreNotEqual(10, img[1, 0, 1]);
                Assert.AreNotEqual(20, img[1, 1, 1]);
                Assert.AreNotEqual(-2, img[0, 0, 1]);
                Assert.AreEqual(0, img[2, 0, 1]);
                foreach (double d in img)
                {
                    Assert.IsTrue(d >= 0 && d <= 1);
                }
            }
            {
                // Test masked values and bands with no variance
                Image img = new Image(1, 1, 3);
                img.CreateMask();
                img.SetMaskValue(0, 0, true);
                img[0, 0, 0] = 17;
                img[0, 0, 1] = 7;
                img[0, 0, 2] = 7;
                img.ApplyStdDevStretch();
                Assert.AreEqual(17, img[0, 0, 0]);
                Assert.AreEqual(7, img[0, 0, 1]);
                Assert.AreEqual(7, img[0, 0, 2]);
            }
        }

        [TestMethod]
        public void TestImageCrop()
        {
            Image img = new Image(2, 4, 7);
            foreach (ImageCoordinate ic in img.Coordinates(true))
            {
                img[ic.Band, ic.Row, ic.Col] = ic.Band * 100 + ic.Row * 10 + ic.Col;
            }
            Image crop = img.Crop(1, 2, 2, 3);
            Assert.AreEqual(2, img.Bands);
            Assert.AreEqual(2, crop.Width);
            Assert.AreEqual(3, crop.Height);
            foreach (ImageCoordinate ic in crop.Coordinates(true))
            {
                int value = (ic.Band) * 100 + (ic.Row + 1) * 10 + (ic.Col + 2);
                Assert.AreEqual(value, crop[ic.Band, ic.Row, ic.Col]);
            }
        }

        [TestMethod]
        [DeploymentItem("TestData", "TestData")]
        public void TestImageResizeBicubic()
        {
            Image img = Image.Load(Path.Combine("TestData", "img", "testPattern.png"));
            Image smaller = img.ResizeSimpleBicubic(64, 64);
            smaller.Save<byte>("testPatternBicubicSmall.png");
            Image bigger = img.ResizeSimpleBicubic(1200, 1401);
            bigger.Save<byte>("testPatternBicubicBigger.png");
        }


        [TestMethod]
        [DeploymentItem("TestData", "TestData")]
        public void TestImageResizeResampling()
        {
            Image img = Image.Load(Path.Combine("TestData", "img", "testPattern.png"));
            Image smaller = img.Resize(64, 64);
            Assert.AreEqual(64, smaller.Width);
            Assert.AreEqual(64, smaller.Height);
            smaller.Save<byte>("testPatternResamplingSmall.png");
            Image bigger = img.Resize(1200, 1401);
            Assert.AreEqual(1200, bigger.Width);
            Assert.AreEqual(1401, bigger.Height);
            bigger.Save<byte>("testPatternResamplingBigger.png");
        }

        [TestMethod]
        [DeploymentItem("TestData", "TestData")]
        public void TestImageFlipVertically()
        {
            Image img = Image.Load(Path.Combine("TestData", "img", "testPattern.png"));
            Image flipped = new Image(img);
            flipped.FlipVertical();
            for(int c = 0; c< img.Width; c++)
            {
                Assert.AreEqual(img[0, 50, c], flipped[0, img.Height - 1 - 50, c]);
                Assert.AreEqual(img[1, 50, c], flipped[1, img.Height - 1 - 50, c]);
                Assert.AreEqual(img[2, 50, c], flipped[2, img.Height - 1 - 50, c]);
            }
            flipped.Save<byte>("flipped.png");
        }



        [TestMethod]
        [DeploymentItem("TestData", "TestData")]
        public void TestImageBlur()
        {
            Image orig = Image.Load(Path.Combine("TestData", "img", "testPattern.png"));
            {
                Image img = Image.Load(Path.Combine("TestData", "img", "testPattern.png"));
                img.GuassianBoxBlur(10);
                img.Save<byte>("blur_10.png");

            }
            {
                Image img = Image.Load(Path.Combine("TestData", "img", "testPattern.png"));
                img.CreateMask(false);
                for (int r = 100; r < 300; r++)
                {
                    for (int c = 200; c < 500; c++)
                    {
                        img.SetMaskValue(r, c, true);
                    }
                }
                img.GuassianBoxBlur(10);
                for (int i = 0; i < img.Data[0].Length; i++)
                {
                    if (img.IsInvalid(i))
                    {
                        var a = img.GetBandValues(i);
                        var b = orig.GetBandValues(i);
                        for (int j = 0; j < img.Bands; j++)
                        {
                            Assert.AreEqual(a[j], b[j]);
                        }
                    }
                    img.SetMaskValue(i, false);
                }
                img.Save<byte>("blur_10_mask.png");
            }
            {
                Image img = Image.Load(Path.Combine("TestData", "img", "testPattern.png"));
                img.GuassianBoxBlur(1000);
                img.Save<byte>("blur_1000.png");
            }
            {
                Image img = Image.Load(Path.Combine("TestData", "img", "testPattern.png"));
                img.GuassianBoxBlur(0);
                for (int i = 0; i < img.Data[0].Length; i++)
                {
                    var a = img.GetBandValues(i);
                    var b = orig.GetBandValues(i);
                    for (int j = 0; j < img.Bands; j++)
                    {
                        Assert.AreEqual(a[j], b[j]);
                    }
                }
                img.Save<byte>("blur_0.png");
            }
        }

        [TestMethod]
        public void TestImageRotate90()
        {
            Image img = new Image(1, 2, 3);
            img[0, 0, 0] = 0;
            img[0, 0, 1] = 1;
            img[0, 1, 0] = 2;
            img[0, 1, 1] = 3;
            img[0, 2, 0] = 4;
            img[0, 2, 1] = 5;
            Image rotatedImg = img.Rotate90Clockwise();
            Assert.AreEqual(img.Width, rotatedImg.Height);
            Assert.AreEqual(img.Height, rotatedImg.Width);
            Assert.AreEqual(img[0, 0, 0], rotatedImg[0, 0, 2]);
            Assert.AreEqual(img[0, 0, 1], rotatedImg[0, 1, 2]);
            Assert.AreEqual(img[0, 1, 0], rotatedImg[0, 0, 1]);
            Assert.AreEqual(img[0, 1, 1], rotatedImg[0, 1, 1]);
            Assert.AreEqual(img[0, 2, 0], rotatedImg[0, 0, 0]);
            Assert.AreEqual(img[0, 2, 1], rotatedImg[0, 1, 0]);
        }
    }
}
