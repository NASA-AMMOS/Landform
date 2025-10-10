using JPLOPS.Imaging;

namespace ImagingTest
{
    public class ImageMetadataTest
    {
        [Fact]
        public void ImageMetadataConstructorTest()
        {
            var im = new ImageMetadata(2, 4, 3);
            Assert.Equal(2, im.Bands);
            Assert.Equal(4, im.Width);
            Assert.Equal(3, im.Height);

            var im2 = new ImageMetadata(im);
            Assert.Equal(2, im2.Bands);
            Assert.Equal(4, im2.Width);
            Assert.Equal(3, im2.Height);

            var im3 = (ImageMetadata) im.Clone();
            Assert.Equal(2, im2.Bands);
            Assert.Equal(4, im2.Width);
            Assert.Equal(3, im2.Height);

            im2.Width = 20;
            im3.Height = 30;

            Assert.Equal(2, im.Bands);
            Assert.Equal(4, im.Width);
            Assert.Equal(3, im.Height);
        }

        [Fact]
        public void PDSLoadFromLBL()
        {
            string filename = Path.Combine("0606MR0025570000400933E01_DRCX.LBL");
            var meta = new PDSMetadata(filename);
            Assert.True(meta.DataPath != null);
            var img = Image.Load(filename);
            Assert.Equal(1344, img.Width);
            Assert.Equal(1200, img.Height);
        }
    }
}
