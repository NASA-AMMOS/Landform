using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Imaging;
using Microsoft.Xna.Framework;

namespace ImagingTest
{
    [TestClass]
    public class OrthographicCameraModelTest
    {
        [TestMethod]
        public void TestOrthographic()
        {
            double r = 10;
            double l = -10;
            double t = 10;
            double b = -10;
            double f = 10;
            double n = 2;
            int w = 512, h = 512;
            var camera = new OrthographicCameraModel(r, l, t, b, f, n, w, h);
            Vector3 xyz = new Vector3(10, 10, 5);
            double range = 0;
            Vector2 pixel = camera.Project(xyz, out range);
            Vector3 xyzNew = camera.Unproject(pixel, range);
            Assert.AreEqual(xyz, xyzNew);
        }
    }
}
