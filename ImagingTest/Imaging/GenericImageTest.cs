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
            GenericImage<byte> img2 = (GenericImage<byte>)img.Clone();
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
