using JPLOPS.Imaging;

namespace ImagingTest
{
    public class ImageStatisticsTest
    {
        [Fact]
        public void TestImageStatistics()
        {
            Image img = new Image(2, 2, 2);
            img[0, 0, 0] = 1;
            img[0, 1, 0] = 17;
            img[0, 0, 1] = -2;
            img[0, 1, 1] = 6;
            img[1, 0, 0] = 3;
            img[1, 1, 0] = 7;
            img[1, 0, 1] = 2;
            img[1, 1, 1] = -17;
            ImageStatistics stats = new ImageStatistics(img);
            Assert.Equal((1+17-2+6) / 4.0, stats.Average(0).Mean);
            Assert.Equal((3 + 7 + 2 - 17) / 4.0, stats.Average(1).Mean);
        }
    }
}
