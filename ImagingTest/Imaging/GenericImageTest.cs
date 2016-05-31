using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;

namespace ImagingTest
{
    [TestClass]
    public class GenericImageTest
    {
        [TestMethod]
        public void TestConstructor()
        {
            GenericImage<byte> img = new GenericImage<byte>(2,20,30);
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
    }
}
