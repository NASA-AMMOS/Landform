using System;
using JPLOPS.Imaging;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace ImagingTest
{
    public class GenericImageTest
    {
        [Fact]
        public void CalculateTriPixelAreaTest()
        {
            Vector2[] pixels = new Vector2[]
           {
                new Vector2(0,0),
                new Vector2(0,2),
                new Vector2(2,0)
           };

            double result = Image.CalculateTriPixelArea(pixels);
            Assert.True(Math.Abs(result - 2.0) < 0.00001);
        }

        [Fact]
        public void CalculateQuadPixelAreaTest()
        {
            Vector2[] pixels = new Vector2[]
            {
                new Vector2(0,0),
                new Vector2(0,4),
                new Vector2(2,4),
                new Vector2(2,0)
            };

            double result = Image.CalculateQuadPixelArea(pixels);

            Assert.True(Math.Abs(result - 8.0) < 0.00001);
        }

        [Fact]
        public void CalculateQuadSubPixelAreaTest()
        {
            Vector2[] pixels = new Vector2[]
            {
                new Vector2(0,0),
                new Vector2(0,0.5),
                new Vector2(0.5,0.5),
                new Vector2(0.5,0)
            };

            double result = Image.CalculateQuadPixelArea(pixels);
            Assert.True(Math.Abs(result - 0.25) < 0.00001);
        }

        [Fact]
        public void TestConstructor()
        {
            GenericImage<byte> img = new GenericImage<byte>(2, 20, 30);
            img[1, 2, 3] = 7;
            Assert.Equal(2, img.Bands);
            Assert.Equal(20, img.Width);
            Assert.Equal(30, img.Height);
            Assert.Equal(2, img.Metadata.Bands);
            Assert.Equal(20, img.Metadata.Width);
            Assert.Equal(30, img.Metadata.Height);

            GenericImage<byte> img2 = new GenericImage<byte>(img);
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
        public void TestClone()
        {
            GenericImage<byte> img = new GenericImage<byte>(2, 20, 30);
            img[1, 2, 3] = 7;
            img.CreateMask();
            img.SetMaskValue(4, true);
            GenericImage<byte> img2 = (GenericImage<byte>)img.Clone();
            Assert.Equal(2, img2.Bands);
            Assert.Equal(20, img2.Width);
            Assert.Equal(30, img2.Height);
            Assert.Equal(2, img2.Metadata.Bands);
            Assert.Equal(20, img2.Metadata.Width);
            Assert.Equal(30, img2.Metadata.Height);
            Assert.Equal(7, img2[1, 2, 3]);
            Assert.Equal(img2.HasMask, img.HasMask);
            Assert.True(!img2.IsValid(4));
            img[1, 2, 4] = 2;
            img.SetMaskValue(4, false);
            Assert.Equal(0, img2[1, 2, 4]);
            Assert.Equal(0, img2[1, 2, 4]);
        }

        [Fact]
        public void TestCreateMask()
        {
            GenericImage<float> img = new GenericImage<float>(2, 3, 4);
            Assert.False(img.HasMask);
            img.CreateMask();
            Assert.True(img.HasMask);
            for (int r = 0; r < img.Height; r++)
            {
                for (int c = 0; c < img.Width; c++)
                {
                    Assert.True(img.IsValid(r, c));
                }
            }
            img.CreateMask(true);
            for (int r = 0; r < img.Height; r++)
            {
                for (int c = 0; c < img.Width; c++)
                {
                    Assert.False(img.IsValid(r, c));
                }
            }
            img = new GenericImage<float>(2, 3, 4);
            img.SetBandValues(2, 3, new float[] { 4, 5 });
            img.SetBandValues(1, 2, new float[] { 3, 5 });
            img.CreateMask(new float[] { 4, 5 });
            Assert.False(img.IsValid(2, 3));
            Assert.True(img.IsValid(1, 2));
        }

        [Fact]
        public void TestSetMaskValue()
        {
            GenericImage<byte> img = new GenericImage<byte>(3, 2, 3);
            img.CreateMask();
            img.SetMaskValue(1, 0, true);
            Assert.False(img.IsValid(1, 0));
            img.SetMaskValue(1, 0, false);
            Assert.True(img.IsValid(1, 0));

            img.SetMaskValue(5, true);
            Assert.False(img.IsValid(5));
            img.SetMaskValue(5, false);
            Assert.True(img.IsValid(5));
        }

        [Fact]
        public void TestSetBandValuesAndEqualsBandValues()
        {
            GenericImage<float> img = new GenericImage<float>(3, 2, 3);
            img.SetBandValues(0, new float[] { 3, 4, 5 });
            Assert.Equal(3, img[0, 0, 0]);
            Assert.Equal(4, img[1, 0, 0]);
            Assert.Equal(5, img[2, 0, 0]);
            Assert.True(img.BandValuesEqual(0, new float[] { 3, 4, 5 }));
            Assert.True(img.BandValuesEqual(1, new float[] { 0, 0, 0 }));
            Assert.False(img.BandValuesEqual(1, new float[] { 3, 4, 6 }));

            img.SetBandValues(1, 2, new float[] { 7, 8, 9 });
            Assert.Equal(7, img[0, 1, 2]);
            Assert.Equal(8, img[1, 1, 2]);
            Assert.Equal(9, img[2, 1, 2]);
            Assert.True(img.BandValuesEqual(1, 2, new float[] { 7, 8, 9 }));
            Assert.False(img.BandValuesEqual(1, 2, new float[] { 7, 9, 8 }));
        }

        [Fact]
        public void TestSetBandValuesAndGetBandValues()
        {
            GenericImage<byte> img = new GenericImage<byte>(2, 3, 4);
            img.SetBandValues(3, new byte[] { 7, 9 });
            img.SetBandValues(2, 3, new byte[] { 6, 12 });
            Assert.Equal(new byte[] { 7, 9 }, img.GetBandValues(3));
            Assert.Equal(new byte[] { 6, 12 }, img.GetBandValues(2, 3));
            Assert.Equal(new byte[] { 0, 0 }, img.GetBandValues(0));
        }

        [Fact]
        public void TestReplaceBandValues()
        {
            GenericImage<short> img = new GenericImage<short>(3, 4, 7);
            img[2, 1, 4] = 4;
            img.ReplaceBandValues(new short[] { 0, 0, 0 }, new short[] { -2, 5, 6 });
            Assert.Equal(new short[] { -2, 5, 6 }, img.GetBandValues(3));
            img.ReplaceBandValues(new short[] { 0, 0, 4 }, new short[] { 7, 8, 9 });
            Assert.Equal(new short[] { -2, 5, 6 }, img.GetBandValues(3));
            Assert.Equal(new short[] { 7, 8, 9 }, img.GetBandValues(1, 4));
        }

        [Fact]
        public void TestApplyInPlace()
        {
            var rand = new Random();
            GenericImage<byte> img = new GenericImage<byte>(2, 20, 30);
            img.ApplyInPlace(x =>
            {
                return (byte)rand.Next(1, 255);
            });
            bool anyValuesZero = img.Aggregate(false, (runningValue, newValue) => runningValue | (newValue == 0));
            Assert.False(anyValuesZero);

            // Test again to see if mask is ignored by default
            img = new GenericImage<byte>(2, 4, 4);
            img.CreateMask();
            img.SetMaskValue(2, 4, true);
            img.ApplyInPlace(x =>
            {
                return 1;
            });
            Assert.Equal(1, img[0, 2, 1]);
            Assert.Equal(1, img[1, 2, 1]);
            Assert.Equal(0, img[0, 2, 4]);
            Assert.Equal(0, img[1, 2, 4]);

            // Now again but allow modifcation of mask values
            // Test again to see if mask is ignored by default
            img = new GenericImage<byte>(2, 4, 4);
            img.CreateMask();
            img.SetMaskValue(2, 4, true);
            img.ApplyInPlace(x =>
            {
                return 1;
            }, true);
            Assert.Equal(1, img[0, 2, 1]);
            Assert.Equal(1, img[1, 2, 1]);
            Assert.Equal(1, img[0, 2, 4]);
            Assert.Equal(1, img[1, 2, 4]);

            img = new GenericImage<byte>(2, 3, 4);
            img.CreateMask();
            img.SetMaskValue(0, 4, true);
            img.ApplyInPlace(0, x =>
            {
                return 1;
            });
            int total = img.Sum(x => x);
            Assert.Equal(3 * 4 - 1, total);
        }

        [Fact]
        public void TestEnumerator()
        {
            var rand = new Random();
            GenericImage<byte> img = new GenericImage<byte>(2, 20, 30);
            float sum = 0;
            double product = 1;
            img.ApplyInPlace(x =>
            {
                var v = rand.Next(1, 255);
                sum += v;
                product *= v;
                return (byte)v;
            });

            float sum2 = 0;
            double product2 = 1;
            foreach (var x in img)
            {
                sum2 += x;
                product2 *= x;
            }
            Assert.Equal(sum2, sum);
            Assert.Equal(product, product);

            // Test masking
            img = new GenericImage<byte>(2, 2, 3);
            img[0, 0, 0] = 1;
            img[1, 0, 0] = 1;
            img[1, 1, 2] = 1;
            sum = 0;
            foreach (var x in img)
            {
                sum += x;
            }
            Assert.Equal(3, sum);
            sum = 0;
            img.CreateMask();
            img.SetMaskValue(0, 0, true);
            foreach (var x in img)
            {
                sum += x;
            }
            Assert.Equal(1, sum);
        }


        [Fact]
        public void TestGenericImageDataAccessor()
        {
            GenericImage<byte> img = new GenericImage<byte>(2, 4, 7);
            img[1, 2, 3] = 27;
            img[0, 2, 1] = 29;
            Assert.Equal(27, img.GetBandData(1)[2 * 4 + 3]);
            Assert.Equal(29, img.GetBandData(0)[2 * 4 + 1]);
        }


        [Fact]
        public void TestCoordinates()
        {
            {
                GenericImage<byte> img = new GenericImage<byte>(2, 3, 4);
                List<ImageCoordinate> coords = img.Coordinates(true).ToList();
                Assert.Equal(2 * 3 * 4, coords.Count);
                Assert.Equal(new ImageCoordinate(0, 0, 0), coords.First());
                Assert.Equal(new ImageCoordinate(1, 3, 2), coords.Last());
                Assert.Equal(new ImageCoordinate(0, 2, 1), coords[7]);
            }
            {
                GenericImage<int> img = new GenericImage<int>(3, 4, 5);
                img.CreateMask();
                img.SetMaskValue(7, true);
                foreach (var ic in img.Coordinates(false))
                {
                    img[ic.Band, ic.Row, ic.Col] += 1;
                }
                for (int b = 0; b < img.Bands; b++)
                {
                    int[] bandData = img.GetBandData(b);
                    for (int i = 0; i < bandData.Length; i++)
                    {
                        int v = bandData[i];
                        if (i == 7)
                        {
                            Assert.Equal(0, v);
                        }
                        else
                        {
                            Assert.Equal(1, v);
                        }
                    }
                }
                foreach (var ic in img.Coordinates(true))
                {
                    img[ic.Band, ic.Row, ic.Col] += 1;
                }
                for (int b = 0; b < img.Bands; b++)
                {
                    int[] bandData = img.GetBandData(b);
                    for (int i = 0; i < bandData.Length; i++)
                    {
                        int v = bandData[i];
                        if (i == 7)
                        {
                            Assert.Equal(1, v);
                        }
                        else
                        {
                            Assert.Equal(2, v);
                        }
                    }
                }
            }
        }

        [Fact]
        public void TestUVPixelCoordinateConversion()
        {
            GenericImage<float> img = new GenericImage<float>(1, 100, 200);
            Assert.Equal(new Vector2(0, 1), img.PixelToUV(new Vector2(0, 0)));
            Assert.Equal(new Vector2(1, 0), img.PixelToUV(new Vector2(100, 200)));
            Assert.Equal(new Vector2(32 / 100.0, 1 - (150 / 200.0)), img.PixelToUV(new Vector2(32, 150)));

            Assert.Equal(new Vector2(0, 200), img.UVToPixel(new Vector2(0, 0)));
            Assert.Equal(new Vector2(100, 0), img.UVToPixel(new Vector2(1, 1)));
            Assert.Equal(img.PixelToUV(new Vector2(32, 150)), new Vector2(32 / 100.0, 1 - (150 / 200.0)));

            Assert.Equal(new Vector2(0, -200), img.UVToPixel(new Vector2(0, 2)));
            Assert.Equal(new Vector2(2, 1), img.PixelToUV(new Vector2(200, 0)));
        }

        [Fact]
        public void TestUVPixelBoundsCoordinateConversion()
        {
            GenericImage<float> img = new GenericImage<float>(1, 100, 200);
            BoundingBox pixels = new BoundingBox(new Vector3(10, 30, 0), new Vector3(25, 60, 0));
            BoundingBox uvs = img.PixelToUV(pixels);
            Assert.Equal(new Vector3(10 / 100.0, 1 - (60 / 200.0), 0), uvs.Min);
            Assert.Equal(new Vector3(25 / 100.0, 1 - (30 / 200.0), 0), uvs.Max);
            BoundingBox p2 = img.UVToPixel(uvs);
            Assert.True(Vector3.AlmostEqual(pixels.Min, p2.Min));
            Assert.True(Vector3.AlmostEqual(pixels.Max, p2.Max));
        }
    }
}
