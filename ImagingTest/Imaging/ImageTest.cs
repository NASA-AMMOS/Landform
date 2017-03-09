using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;
using System.IO;

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
            Image imgRead = Image.Load("load.png", new GDALSeralizer(), ImageConverters.ValueRangeToNormalizedImage);
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
            Image imgRead = Image.Load("roundOff.tif", new GDALSeralizer(), ImageConverters.PassThrough);

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
    }
}
