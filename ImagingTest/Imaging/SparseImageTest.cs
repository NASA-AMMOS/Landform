using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Imaging;

namespace ImagingTest
{
    [TestClass]
    [DeploymentItem("gdal", "gdal")]
    public class SparseImageTest
    {
        [TestMethod]
        public void TestSparseImageConstructorFromImage()
        {
            Image img = new Image(1, 2, 3);
            img[0, 0, 0] = 43f / 255;
            SparseImage spImg = new SparseImage(img, 1);
            Assert.AreEqual(spImg[0, 0, 0], 43f / 255);
            Assert.AreEqual(spImg.Bands, 1);
            Assert.AreEqual(spImg.Width, 2);
            Assert.AreEqual(spImg.Height, 3);
            Assert.AreEqual(spImg.Metadata.Bands, 1);
            Assert.AreEqual(spImg.Metadata.Width, 2);
            Assert.AreEqual(spImg.Metadata.Height, 3);
        }

        [TestMethod]
        public void TestSparseImageConstructorFromUrl()
        {
            Image img = new Image(1, 2, 3);
            img[0, 0, 0] = 43f / 255;
            SparseImage spImg = new SparseImage(img, 1);
            spImg.SaveAllChunks<byte>("sparseImage", ".png");
            SparseImage spImg2 = new SparseImage(spImg.Bands, spImg.Width, spImg.Height, "sparseImage", ".png", 1);
            Assert.AreEqual(spImg2.Bands, 1);
            Assert.AreEqual(spImg2.Width, 2);
            Assert.AreEqual(spImg2.Height, 3);
            Assert.AreEqual(spImg2.Metadata.Bands, 1);
            Assert.AreEqual(spImg2.Metadata.Width, 2);
            Assert.AreEqual(spImg2.Metadata.Height, 3);
        }

        [TestMethod]
        public void TestSave()
        {
            Image img = new Image(1, 2, 3);
            img[0, 0, 0] = 43f / 255;
            img[0, 0, 1] = 241f / 255;
            img[0, 1, 0] = 7f / 255;
            img[0, 1, 1] = 123f / 255;
            SparseImage spImg = new SparseImage(img, 1);
            spImg.SaveAllChunks<byte>("sparseImage", ".png");
            Image img2 = Image.Load("sparseImage_0_0.png");
            Assert.AreEqual(img2[0, 0, 0], 43f / 255);
            img2 = Image.Load("sparseImage_0_1.png");
            Assert.AreEqual(img2[0, 0, 0], 241f / 255);
            img2 = Image.Load("sparseImage_1_0.png");
            Assert.AreEqual(img2[0, 0, 0], 7f / 255);
            img2 = Image.Load("sparseImage_1_1.png");
            Assert.AreEqual(img2[0, 0, 0], 123f / 255);
        }

        [TestMethod]
        public void TestRead()
        {
            Image img = new Image(1, 2, 3);
            img[0, 0, 0] = 43f / 255;
            img[0, 0, 1] = 241f / 255;
            img[0, 1, 0] = 7f / 255;
            img[0, 1, 1] = 123f / 255;
            SparseImage spImg = new SparseImage(img, 1);
            spImg.SaveAllChunks<byte>("sparseImage", ".png");
            SparseImage spImg2 = new SparseImage(spImg.Bands, spImg.Width, spImg.Height, "sparseImage", ".png", 1);
            Assert.AreEqual(spImg2[0, 0, 0], 43f / 255);
            Assert.AreEqual(spImg2[0, 0, 1], 241f / 255);
            Assert.AreEqual(spImg2[0, 1, 0], 7f / 255);
            Assert.AreEqual(spImg2[0, 1, 1], 123f / 255);
        }
    }
}
