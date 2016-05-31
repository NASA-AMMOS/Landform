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
            Assert.AreEqual(img.Bands, 2);
            Assert.AreEqual(img.Width, 20);
            Assert.AreEqual(img.Height, 30);
            Assert.AreEqual(img.Metadata.Bands, 2);
            Assert.AreEqual(img.Metadata.Width, 20);
            Assert.AreEqual(img.Metadata.Height, 30);
        }
    }
}
