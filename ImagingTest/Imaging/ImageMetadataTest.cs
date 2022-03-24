using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Imaging;

namespace ImagingTest
{
    [TestClass]
    public class ImageMetadataTest
    {
        [TestMethod]
        public void ImageMetadataConstructorTest()
        {
            var im = new ImageMetadata(2, 4, 3);
            Assert.AreEqual(2, im.Bands);
            Assert.AreEqual(4, im.Width);
            Assert.AreEqual(3, im.Height);

            var im2 = new ImageMetadata(im);
            Assert.AreEqual(2, im2.Bands);
            Assert.AreEqual(4, im2.Width);
            Assert.AreEqual(3, im2.Height);

            var im3 = (ImageMetadata) im.Clone();
            Assert.AreEqual(2, im2.Bands);
            Assert.AreEqual(4, im2.Width);
            Assert.AreEqual(3, im2.Height);

            im2.Width = 20;
            im3.Height = 30;

            Assert.AreEqual(2, im.Bands);
            Assert.AreEqual(4, im.Width);
            Assert.AreEqual(3, im.Height);
        }
    }
}
