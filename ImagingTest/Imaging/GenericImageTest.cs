using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;
using System.Linq;

namespace ImagingTest
{
    [TestClass]
    public class GenericImageTest
    {
        [TestMethod]
        public void TestConstructor()
        {
            GenericImage<byte> img = new GenericImage<byte>(2, 20, 30);
            img[1, 2, 3] = 7;
            Assert.AreEqual(img.Bands, 2);
            Assert.AreEqual(img.Width, 20);
            Assert.AreEqual(img.Height, 30);
            Assert.AreEqual(img.Metadata.Bands, 2);
            Assert.AreEqual(img.Metadata.Width, 20);
            Assert.AreEqual(img.Metadata.Height, 30);

            GenericImage<byte> img2 = new GenericImage<byte>(img);
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
        public void TestClone()
        {
            GenericImage<byte> img = new GenericImage<byte>(2, 20, 30);
            img[1, 2, 3] = 7;
            img.CreateMask();
            img.SetMaskValue(4, true);
            GenericImage<byte> img2 = (GenericImage<byte>)img.Clone();
            Assert.AreEqual(img2.Bands, 2);
            Assert.AreEqual(img2.Width, 20);
            Assert.AreEqual(img2.Height, 30);
            Assert.AreEqual(img2.Metadata.Bands, 2);
            Assert.AreEqual(img2.Metadata.Width, 20);
            Assert.AreEqual(img2.Metadata.Height, 30);
            Assert.AreEqual(img2[1, 2, 3], 7);
            Assert.AreEqual(img.HasMask, img2.HasMask);
            Assert.AreEqual(true, img2.IsMasked(4));
            img[1, 2, 4] = 2;
            img.SetMaskValue(4, false);
            Assert.AreEqual(0, img2[1, 2, 4]);
            Assert.AreEqual(0, img2[1, 2, 4]);
        }

        [TestMethod]
        public void TestCreateMask()
        {
            GenericImage<float> img = new GenericImage<float>(2, 3, 4);
            Assert.IsFalse(img.HasMask);
            img.CreateMask();
            Assert.IsTrue(img.HasMask);
            for(int i = 0; i < img.Width * img.Height; i++)
            {
                Assert.IsFalse(img.IsMasked(i));
            }
            img.CreateMask(true);
            for (int i = 0; i < img.Width * img.Height; i++)
            {
                Assert.IsTrue(img.IsMasked(i));
            }
            img = new GenericImage<float>(2, 3, 4);
            img.SetBandValues(2, 3, new float[] { 4, 5 });
            img.SetBandValues(1, 2, new float[] { 3, 5 });
            img.CreateMask(new float[] { 4, 5 });
            Assert.IsTrue(img.IsMasked(2, 3));
            Assert.IsFalse(img.IsMasked(1, 2));
        }

        [TestMethod]
        public void TestSetMaskValue()
        {
            GenericImage<byte> img = new GenericImage<byte>(3, 2, 3);
            img.CreateMask();
            img.SetMaskValue(1, 0, true);
            Assert.AreEqual(true, img.IsMasked(1, 0));
            img.SetMaskValue(1, 0, false);
            Assert.AreEqual(false, img.IsMasked(1, 0));

            img.SetMaskValue(5, true);
            Assert.AreEqual(true, img.IsMasked(5));
            img.SetMaskValue(5, false);
            Assert.AreEqual(false, img.IsMasked(5));
        }

        [TestMethod]
        public void TestSetBandValuesAndEqualsBandValues()
        {
            GenericImage<float> img = new GenericImage<float>(3, 2, 3);
            img.SetBandValues(0, new float[] { 3, 4, 5 });
            Assert.AreEqual(3, img[0, 0, 0]);
            Assert.AreEqual(4, img[1, 0, 0]);
            Assert.AreEqual(5, img[2, 0, 0]);
            Assert.IsTrue(img.BandValuesEqual(0, new float[] { 3, 4, 5 }));
            Assert.IsTrue(img.BandValuesEqual(1, new float[] { 0, 0, 0 }));
            Assert.IsFalse(img.BandValuesEqual(1, new float[] { 3, 4, 6 }));

            img.SetBandValues(1, 2, new float[] { 7, 8, 9 });
            Assert.AreEqual(7, img[0, 1, 2]);
            Assert.AreEqual(8, img[1, 1, 2]);
            Assert.AreEqual(9, img[2, 1, 2]);
            Assert.IsTrue(img.BandValuesEqual(1, 2, new float[] { 7, 8, 9 }));
            Assert.IsFalse(img.BandValuesEqual(1, 2, new float[] { 7, 9, 8 }));
        }

        [TestMethod]
        public void TestSetBandValuesAndGetBandValues()
        {
            GenericImage<byte> img = new GenericImage<byte>(2, 3, 4);
            img.SetBandValues(3, new byte[] { 7, 9 });
            img.SetBandValues(2,3, new byte[] { 6, 12 });
            CollectionAssert.AreEqual(img.GetBandValues(3), new byte[] { 7, 9 });
            CollectionAssert.AreEqual(img.GetBandValues(2, 3), new byte[] { 6, 12 });
            CollectionAssert.AreEqual(img.GetBandValues(0), new byte[] { 0, 0});
        }

        [TestMethod]
        public void TestReplaceBandValues()
        {
            GenericImage<short> img = new GenericImage<short>(3, 4, 7);
            img[2, 1, 4] = 4;
            img.ReplaceBandValues(new short[] { 0, 0, 0 }, new short[] { -2, 5, 6 });
            CollectionAssert.AreEqual(new short[] { -2, 5, 6 }, img.GetBandValues(3));
            img.ReplaceBandValues(new short[] { 0,0,4}, new short[] { 7, 8, 9 });
            CollectionAssert.AreEqual(new short[] { -2, 5, 6 }, img.GetBandValues(3));
            CollectionAssert.AreEqual(new short[] { 7, 8, 9 }, img.GetBandValues(1,4));
        }

        [TestMethod]
        public void TestApplyInPlace()
        {
            var rand = new Random();
            GenericImage<byte> img = new GenericImage<byte>(2, 20, 30);
            img.ApplyInPlace(x =>
            {
                return (byte)rand.Next(1, 255);
            });
            bool anyValuesZero = img.Aggregate(false, (runningValue, newValue) => runningValue | (newValue == 0));
            Assert.AreEqual(anyValuesZero, false);

            // Test again to see if mask is ignored by default
            img = new GenericImage<byte>(2, 4, 4);
            img.CreateMask();
            img.SetMaskValue(2, 4, true);
            img.ApplyInPlace(x =>
            {
                return 1;
            });
            Assert.AreEqual(1, img[0, 2, 1]);
            Assert.AreEqual(1, img[1, 2, 1]);
            Assert.AreEqual(0, img[0, 2, 4]);
            Assert.AreEqual(0, img[1, 2, 4]);

            // Now again but allow modifcation of mask values
            // Test again to see if mask is ignored by default
            img = new GenericImage<byte>(2, 4, 4);
            img.CreateMask();
            img.SetMaskValue(2, 4, true);
            img.ApplyInPlace(x =>
            {
                return 1;
            }, true);
            Assert.AreEqual(1, img[0, 2, 1]);
            Assert.AreEqual(1, img[1, 2, 1]);
            Assert.AreEqual(1, img[0, 2, 4]);
            Assert.AreEqual(1, img[1, 2, 4]);
        }

        [TestMethod]
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
            Assert.AreEqual(sum, sum2);
            Assert.AreEqual(product, product);

            // Test masking
            img = new GenericImage<byte>(2, 2, 3);
            img[0, 0, 0] = 1;
            img[1, 0, 0] = 1;
            img[1, 1, 2] = 1;
            sum = 0;
            foreach(var x in img)
            {
                sum += x;
            }
            Assert.AreEqual(3, sum);
            sum = 0;
            img.CreateMask();
            img.SetMaskValue(0, 0, true);
            foreach (var x in img)
            {
                sum += x;
            }
            Assert.AreEqual(1, sum);
        }


        [TestMethod]
        public void TestGenericImageDataAccessor()
        {
            GenericImage<byte> img = new GenericImage<byte>(2, 4, 7);
            img[1, 2, 3] = 27;
            img[0, 2, 1] = 29;
            Assert.AreEqual(27, img.Data[1][2 * 4 + 3]);
            Assert.AreEqual(29, img.Data[0][2 * 4 + 1]);
        }
    }
}
