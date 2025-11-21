using JPLOPS.Imaging;

namespace ImagingTest
{
    public class SparseImageTest
    {
        [Fact]
        public void TestSparseImageConstructorFromImage()
        {
            Image img = new Image(1, 2, 3);
            img[0, 0, 0] = 43f / 255;
            SparseImage spImg = new SparseImage(img, 1);
            Assert.Equal(43f / 255, spImg[0, 0, 0]);
            Assert.Equal(1, spImg.Bands);
            Assert.Equal(2, spImg.Width);
            Assert.Equal(3, spImg.Height);
            Assert.Equal(1, spImg.Metadata.Bands);
            Assert.Equal(2, spImg.Metadata.Width);
            Assert.Equal(3, spImg.Metadata.Height);
        }

        [Fact]
        public void TestSparseImageConstructorFromUrl()
        {
            Image img = new Image(1, 2, 3);
            img[0, 0, 0] = 43f / 255;
            SparseImage spImg = new SparseImage(img, 1);
            spImg.SaveAllChunks<byte>("sparseImage", ".png");
            SparseImage spImg2 = new SparseImage(spImg.Bands, spImg.Width, spImg.Height, "sparseImage", ".png", 1);
            Assert.Equal(1, spImg2.Bands);
            Assert.Equal(2, spImg2.Width);
            Assert.Equal(3, spImg2.Height);
            Assert.Equal(1, spImg2.Metadata.Bands);
            Assert.Equal(2, spImg2.Metadata.Width);
            Assert.Equal(3, spImg2.Metadata.Height);
        }

        [Fact]
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
            Assert.Equal(43f / 255, img2[0, 0, 0]);
            img2 = Image.Load("sparseImage_0_1.png");
            Assert.Equal(241f / 255, img2[0, 0, 0]);
            img2 = Image.Load("sparseImage_1_0.png");
            Assert.Equal(7f / 255, img2[0, 0, 0]);
            img2 = Image.Load("sparseImage_1_1.png");
            Assert.Equal(123f / 255, img2[0, 0, 0]);
        }

        [Fact]
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
            Assert.Equal(43f / 255, spImg2[0, 0, 0]);
            Assert.Equal(241f / 255, spImg2[0, 0, 1]);
            Assert.Equal(7f / 255, spImg2[0, 1, 0]);
            Assert.Equal(123f / 255, spImg2[0, 1, 1]);
        }
    }
}
