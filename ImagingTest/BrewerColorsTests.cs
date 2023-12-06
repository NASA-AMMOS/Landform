using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Imaging;

namespace ImagingTest
{
    [TestClass()]
    public class BrewerColorsTests
    {
        [TestMethod()]
        public void GetColorsTest()
        {
            var result = BrewerColors.GetColors("PuRd", 3);
            Assert.IsTrue(result.Length == 3);
            Assert.IsTrue(result[0].X == 231/255.0f && result[0].Y == 225/255.0f && result[0].Z == 239/255.0f);
            Assert.IsTrue(result[1].X == 201/255.0f && result[1].Y == 148/255.0f && result[1].Z == 199/255.0f);
            Assert.IsTrue(result[2].X == 221 / 255.0f && result[2].Y == 28 / 255.0f && result[2].Z == 119 / 255.0f);

            result = BrewerColors.GetColors("PuRd", 75);
            Assert.IsTrue(result == null);

            result = BrewerColors.GetColors("nope", 3);
            Assert.IsTrue(result == null);

        }
    }
}