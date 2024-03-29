//#define ENABLE_GDAL_JPG_PNG_BMP

using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using JPLOPS.Imaging;
using JPLOPS.Test;

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
            imgOrig[1, 2, 3] = 43f / 255;
            imgOrig.Save<byte>("load.png");
            if (!File.Exists("load.png"))
            {
                Assert.Fail();
            }
#if ENABLE_GDAL_JPG_PNG_BMP
            var ser = new GDALSerializer();
#else
            var ser = new ImageSharpSerializer();
#endif
            Image imgRead = Image.Load("load.png", ser, ImageConverters.ValueRangeToNormalizedImage);
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
                for (int b = 0; b < imgOrig.Bands; b++)
                {
                    float[] origBandData = imgOrig.GetBandData(b);
                    float[] readBandData = imgRead.GetBandData(b);
                    for (int i = 0; i < origBandData.Length; i++)
                    {
                        Assert.AreEqual(readBandData[i], origBandData[i]);
                    }
                }
            }
            // double
            {
                imgOrig.Save<double>("doubleimg.tif");
                var imgRead = Image.Load("doubleimg.tif");
                for (int b = 0; b < imgOrig.Bands; b++)
                {
                    float[] origBandData = imgOrig.GetBandData(b);
                    float[] readBandData = imgRead.GetBandData(b);
                    for (int i = 0; i < origBandData.Length; i++)
                    {
                        Assert.AreEqual(readBandData[i], origBandData[i]);
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
                img.ApplyStdDevStretch(applySameStretchToAllbands: false);
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
                img.ApplyStdDevStretch(applySameStretchToAllbands: false);
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

        [TestMethod()]
        public void SampleAsColorTest()
        {
            Image monoImage = new Image(1, 1, 1);
            monoImage[0, 0, 0] = 0.75f;

            Image colorImage = new Image(3, 1, 1);
            colorImage[0, 0, 0] = 0.15f;
            colorImage[1, 0, 0] = 0.25f;
            colorImage[2, 0, 0] = 0.35f;

            float[] monoSamples = monoImage.SampleAsColor(Vector2.Zero);
            Assert.IsTrue(monoSamples.Length == 3);
            Assert.IsTrue(monoSamples[0] == 0.75f);
            Assert.IsTrue(monoSamples[1] == 0.75f);
            Assert.IsTrue(monoSamples[2] == 0.75f);

            float[] colorSamples = colorImage.SampleAsColor(Vector2.Zero);
            Assert.IsTrue(colorSamples[0] == 0.15f);
            Assert.IsTrue(colorSamples[1] == 0.25f);
            Assert.IsTrue(colorSamples[2] == 0.35f);
        }

        [TestMethod()]
        public void SampleAsMonoTest()
        {
            //When #502 is resolved add another test here for color to mono
            Image monoImage = new Image(1, 1, 1);
            monoImage[0, 0, 0] = 0.75f;

            Assert.IsTrue(monoImage.SampleAsMono(Vector2.Zero) == 0.75f);
        }

        [TestMethod()]
        public void SetAsColorTest()
        {
            Image colorImage = new Image(3, 1, 1);
            colorImage.SetAsColor(new float[] { 0.75f }, 0, 0);
            Assert.IsTrue(colorImage[0, 0, 0] == 0.75f);
            Assert.IsTrue(colorImage[1, 0, 0] == 0.75f);
            Assert.IsTrue(colorImage[2, 0, 0] == 0.75f);

            colorImage.SetAsColor(new float[] { 0.15f, 0.25f, 0.35f }, 0, 0);
            Assert.IsTrue(colorImage[0, 0, 0] == 0.15f);
            Assert.IsTrue(colorImage[1, 0, 0] == 0.25f);
            Assert.IsTrue(colorImage[2, 0, 0] == 0.35f);
        }

        [TestMethod()]
        public void SetAsMonoTest()
        {
            //When #502 is resolved add another test here for color to mono
            Image monoImage = new Image(1, 1, 1);
            monoImage.SetAsMono(new float[] { 0.75f }, 0, 0);
            Assert.IsTrue(monoImage[0, 0, 0] == 0.75f);
        }

        [TestMethod()]
        public void InpaintTest()
        {
            Image monoImage = new Image(1, 4, 4);
            monoImage.CreateMask(false);
            monoImage.SetMaskValue(0, 0, true);
            monoImage.Inpaint(1, true);
            Assert.IsFalse(monoImage.IsValid(0, 0));
            Assert.IsTrue(monoImage.IsValid(0, 1));
            monoImage.Inpaint(1, false);
            Assert.IsTrue(monoImage.IsValid(0, 0));
            Assert.IsTrue(monoImage.IsValid(0, 1));
        }

        [TestMethod()]
        public void CreateMaskTest()
        {
            //create image
            Image monoImage = new Image(1, 4, 4);

            //create mask image
            Image maskImage = new Image(1, 4, 4);
            maskImage.SetBandValues(3,2, new float[]{1.0f});

            //set mask image as mask
            monoImage.SetMask(maskImage);

            for (int idxRow = 0; idxRow < 4; idxRow++)
            {
                for (int idxCol = 0; idxCol < 4; idxCol++)
                {
                    if (idxRow == 3 && idxCol == 2)
                    {
                        Assert.IsFalse(monoImage.IsValid(idxRow, idxCol));
                    }
                    else
                    {
                        Assert.IsTrue(monoImage.IsValid(idxRow, idxCol));
                    }
                }
            }
        }
    }
}
