//#define ENABLE_GDAL_JPG_PNG_BMP

using System;
using System.IO;
using Microsoft.Xna.Framework;
using JPLOPS.Imaging;
using JPLOPS.TestExtensions;

namespace ImagingTest
{
    public class ImageTest
    {

        [Fact]
        public void TestImageConstructor()
        {
            Image img = new Image(2, 20, 30);
            img[1, 2, 3] = 7;
            Assert.Equal(2, img.Bands);
            Assert.Equal(20, img.Width);
            Assert.Equal(30, img.Height);
            Assert.Equal(2, img.Metadata.Bands);
            Assert.Equal(20, img.Metadata.Width);
            Assert.Equal(30, img.Metadata.Height);

            Image img2 = new Image(img);
            Assert.Equal(2, img2.Bands);
            Assert.Equal(20, img2.Width);
            Assert.Equal(30, img2.Height);
            Assert.Equal(2, img2.Metadata.Bands);
            Assert.Equal(20, img2.Metadata.Width);
            Assert.Equal(30, img2.Metadata.Height);
            Assert.Equal(7, img2[1, 2, 3]);
            img[1, 2, 4] = 2;
            Assert.Equal(0, img2[1, 2, 4]);
        }


        [Fact]
        public void ImageSaveLoad()
        {
            Image imgOrig = new Image(3, 20, 30);
            imgOrig[1, 2, 3] = 43f / 255;
            imgOrig.Save<byte>("load.png");
            Assert.True(File.Exists("load.png"));
#if ENABLE_GDAL_JPG_PNG_BMP
            var ser = new GDALSerializer();
#else
            var ser = new ImageSharpSerializer();
#endif
            Image imgRead = Image.Load("load.png", ser, ImageConverters.ValueRangeToNormalizedImage);
            Assert.Equal(imgRead.Bands, imgOrig.Bands);
            Assert.Equal(imgRead.Width, imgOrig.Width);
            Assert.Equal(imgRead.Height, imgOrig.Height);
            Assert.Equal(43f / 255, imgRead[1, 2, 3]);
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

            Assert.Equal(0, imgRead[0, 0, 0]);
            Assert.Equal(Math.Floor(maxValue / 2), imgRead[0, 0, 1]);
            Assert.Equal(maxValue, imgRead[0, 0, 2]);
            Assert.Equal(maxValue - 1, imgRead[0, 0, 3]);

            imgRead = Image.Load("roundOff.tif");
            Assert.Equal(0, imgRead[0, 0, 0]);
            Assert.True(Math.Abs(imgRead[0, 0, 1] - Math.Floor(maxValue / 2) / maxValue) < 0.00001f);
            Assert.Equal(1, imgRead[0, 0, 2]);
            Assert.True(Math.Abs(imgRead[0, 0, 3] - (maxValue - 1) / maxValue) < 0.00001f);

        }

        [Fact]
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
                        Assert.Equal(origBandData[i], readBandData[i]);
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
                        Assert.Equal(origBandData[i], readBandData[i]);
                    }
                }
            }
        }

        [Fact]
        public void BandScaleValues()
        {
            Image img = new Image(3, 2, 3);
            img[0, 0, 0] = 7;
            img[0, 0, 1] = 1;
            img[0, 0, 2] = 40;
            img.ScaleValues(0, 3, 20, -1, 0);
            AssertE.AreSimilar(-1 + (7 - 3) / (double)(20 - 3), img[0, 0, 0], 1E-5);
            Assert.Equal(-1, img[0, 0, 1]);
            Assert.Equal(0, img[0, 0, 2]);
        }

        [Fact]
        public void ImageScaleValues()
        {
            Image img = new Image(3, 2, 2);
            img[0, 0, 0] = 4;
            img[1, 0, 1] = 10;
            img[1, 1, 1] = 20;
            img[0, 0, 1] = -2;
            img.ScaleValues(0, 10, 20, 40);
            Assert.Equal(28, img[0, 0, 0]);
            Assert.Equal(40, img[1, 0, 1]);
            Assert.Equal(40, img[1, 1, 1]);
            Assert.Equal(20, img[0, 0, 1]);
        }

        [Fact]
        public void ImageStdStretch()
        {
            {
                Image img = new Image(3, 2, 2);
                img[0, 0, 0] = 4;
                img[1, 0, 1] = 10;
                img[1, 1, 1] = 20;
                img[0, 0, 1] = -2;
                img.ApplyStdDevStretch(applySameStretchToAllbands: false);
                Assert.NotEqual(4, img[0, 0, 0]);
                Assert.NotEqual(10, img[1, 0, 1]);
                Assert.NotEqual(20, img[1, 1, 1]);
                Assert.NotEqual(-2, img[0, 0, 1]);
                Assert.Equal(0, img[2, 0, 1]);
                foreach (double d in img)
                {
                    Assert.True(d >= 0 && d <= 1);
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
                Assert.Equal(17, img[0, 0, 0]);
                Assert.Equal(7, img[0, 0, 1]);
                Assert.Equal(7, img[0, 0, 2]);
            }
        }

        [Fact]
        public void TestImageCrop()
        {
            Image img = new Image(2, 4, 7);
            foreach (ImageCoordinate ic in img.Coordinates(true))
            {
                img[ic.Band, ic.Row, ic.Col] = ic.Band * 100 + ic.Row * 10 + ic.Col;
            }
            Image crop = img.Crop(1, 2, 2, 3);
            Assert.Equal(2, img.Bands);
            Assert.Equal(2, crop.Width);
            Assert.Equal(3, crop.Height);
            foreach (ImageCoordinate ic in crop.Coordinates(true))
            {
                int value = (ic.Band) * 100 + (ic.Row + 1) * 10 + (ic.Col + 2);
                Assert.Equal(value, crop[ic.Band, ic.Row, ic.Col]);
            }
        }

        [Fact]
        public void TestImageResizeBicubic()
        {
            Image img = Image.Load("testPattern.png");
            Image smaller = img.ResizeBicubic(64, 64);
            smaller.Save<byte>("testPatternBicubicSmall.png");
            Image bigger = img.ResizeBicubic(1200, 1401);
            bigger.Save<byte>("testPatternBicubicBigger.png");
        }


        [Fact]
        public void TestImageResizeResampling()
        {
            Image img = Image.Load("testPattern.png");
            Image smaller = img.Resize(64, 64);
            Assert.Equal(64, smaller.Width);
            Assert.Equal(64, smaller.Height);
            smaller.Save<byte>("testPatternResamplingSmall.png");
            Image bigger = img.Resize(1200, 1401);
            Assert.Equal(1200, bigger.Width);
            Assert.Equal(1401, bigger.Height);
            bigger.Save<byte>("testPatternResamplingBigger.png");
        }

        [Fact]
        public void TestImageFlipVertically()
        {
            Image img = Image.Load("testPattern.png");
            Image flipped = new Image(img);
            flipped.FlipVertical();
            for (int c = 0; c < img.Width; c++)
            {
                Assert.Equal(img[0, 50, c], flipped[0, img.Height - 1 - 50, c]);
                Assert.Equal(img[1, 50, c], flipped[1, img.Height - 1 - 50, c]);
                Assert.Equal(img[2, 50, c], flipped[2, img.Height - 1 - 50, c]);
            }
            flipped.Save<byte>("flipped.png");
        }

        [Fact]
        public void TestImageBlur()
        {
            Image orig = Image.Load("testPattern.png");
            {
                Image img = Image.Load("testPattern.png");
                img.GaussianBoxBlur(10);
                img.Save<byte>("blur_10.png");

            }
            {
                Image img = Image.Load("testPattern.png");
                img.CreateMask(false);
                for (int r = 100; r < 300; r++)
                {
                    for (int c = 200; c < 500; c++)
                    {
                        img.SetMaskValue(r, c, true);
                    }
                }
                img.GaussianBoxBlur(10);
                float[] bandData = img.GetBandData(0);
                for (int i = 0; i < bandData.Length; i++)
                {
                    if (!img.IsValid(i))
                    {
                        var a = img.GetBandValues(i);
                        var b = orig.GetBandValues(i);
                        for (int j = 0; j < img.Bands; j++)
                        {
                            Assert.Equal(a[j], b[j]);
                        }
                    }
                    img.SetMaskValue(i, false);
                }
                img.Save<byte>("blur_10_mask.png");
            }
            {
                Image img = Image.Load("testPattern.png");
                img.GaussianBoxBlur(1000);
                img.Save<byte>("blur_1000.png");
            }
            {
                Image img = Image.Load("testPattern.png");
                img.GaussianBoxBlur(0);
                float[] bandData = img.GetBandData(0);
                for (int i = 0; i < bandData.Length; i++)
                {
                    var a = img.GetBandValues(i);
                    var b = orig.GetBandValues(i);
                    for (int j = 0; j < img.Bands; j++)
                    {
                        Assert.Equal(a[j], b[j]);
                    }
                }
                img.Save<byte>("blur_0.png");
            }
        }

        [Fact]
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
            Assert.Equal(rotatedImg.Height, img.Width);
            Assert.Equal(rotatedImg.Width, img.Height);
            Assert.Equal(rotatedImg[0, 0, 2], img[0, 0, 0]);
            Assert.Equal(rotatedImg[0, 1, 2], img[0, 0, 1]);
            Assert.Equal(rotatedImg[0, 0, 1], img[0, 1, 0]);
            Assert.Equal(rotatedImg[0, 1, 1], img[0, 1, 1]);
            Assert.Equal(rotatedImg[0, 0, 0], img[0, 2, 0]);
            Assert.Equal(rotatedImg[0, 1, 0], img[0, 2, 1]);
        }

        [Fact]
        public void SampleAsColorTest()
        {
            Image monoImage = new Image(1, 1, 1);
            monoImage[0, 0, 0] = 0.75f;

            Image colorImage = new Image(3, 1, 1);
            colorImage[0, 0, 0] = 0.15f;
            colorImage[1, 0, 0] = 0.25f;
            colorImage[2, 0, 0] = 0.35f;

            float[] monoSamples = monoImage.SampleAsColor(Vector2.Zero);
            Assert.True(monoSamples.Length == 3);
            Assert.True(monoSamples[0] == 0.75f);
            Assert.True(monoSamples[1] == 0.75f);
            Assert.True(monoSamples[2] == 0.75f);

            float[] colorSamples = colorImage.SampleAsColor(Vector2.Zero);
            Assert.True(colorSamples[0] == 0.15f);
            Assert.True(colorSamples[1] == 0.25f);
            Assert.True(colorSamples[2] == 0.35f);
        }

        [Fact]
        public void SampleAsMonoTest()
        {
            //When #502 is resolved add another test here for color to mono
            Image monoImage = new Image(1, 1, 1);
            monoImage[0, 0, 0] = 0.75f;

            Assert.True(monoImage.SampleAsMono(Vector2.Zero) == 0.75f);
        }

        [Fact]
        public void SetAsColorTest()
        {
            Image colorImage = new Image(3, 1, 1);
            colorImage.SetAsColor(new float[] { 0.75f }, 0, 0);
            Assert.True(colorImage[0, 0, 0] == 0.75f);
            Assert.True(colorImage[1, 0, 0] == 0.75f);
            Assert.True(colorImage[2, 0, 0] == 0.75f);

            colorImage.SetAsColor(new float[] { 0.15f, 0.25f, 0.35f }, 0, 0);
            Assert.True(colorImage[0, 0, 0] == 0.15f);
            Assert.True(colorImage[1, 0, 0] == 0.25f);
            Assert.True(colorImage[2, 0, 0] == 0.35f);
        }

        [Fact]
        public void SetAsMonoTest()
        {
            //When #502 is resolved add another test here for color to mono
            Image monoImage = new Image(1, 1, 1);
            monoImage.SetAsMono(new float[] { 0.75f }, 0, 0);
            Assert.True(monoImage[0, 0, 0] == 0.75f);
        }

        [Fact]
        public void InpaintTest()
        {
            Image monoImage = new Image(1, 4, 4);
            monoImage.CreateMask(false);
            monoImage.SetMaskValue(0, 0, true);
            monoImage.Inpaint(1, true);
            Assert.False(monoImage.IsValid(0, 0));
            Assert.True(monoImage.IsValid(0, 1));
            monoImage.Inpaint(1, false);
            Assert.True(monoImage.IsValid(0, 0));
            Assert.True(monoImage.IsValid(0, 1));
        }

        [Fact]
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
                        Assert.False(monoImage.IsValid(idxRow, idxCol));
                    }
                    else
                    {
                        Assert.True(monoImage.IsValid(idxRow, idxCol));
                    }
                }
            }
        }
    }
}
