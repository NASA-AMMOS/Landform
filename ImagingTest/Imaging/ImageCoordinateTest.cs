using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Imaging;

namespace ImagingTest
{
    [TestClass]
    public class ImageCoordinateTest
    {

        [TestMethod]
        public void TestCoordinateConstructor()
        {
            ImageCoordinate ic = new ImageCoordinate(1, 2, 3);
            Assert.AreEqual(1, ic.Band);
            Assert.AreEqual(2, ic.Row);
            Assert.AreEqual(3, ic.Col);
        }
    }
}
