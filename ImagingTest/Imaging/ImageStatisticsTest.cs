using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Imaging;

namespace ImagingTest
{
    [TestClass]
    public class ImageStatisticsTest
    {
        [TestMethod]
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
            Assert.AreEqual((1+17-2+6) / 4.0, stats.Average(0).Mean);
            Assert.AreEqual((3 + 7 + 2 - 17) / 4.0, stats.Average(1).Mean);
        }
    }
}
