using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using Microsoft.Xna.Framework;

namespace ImagingTest
{
    [TestClass]
    public class OrthographicCameraModelTest
    {
        [TestMethod]
        public void TestOrthographic()
        {
            Matrix transform = new Matrix();
            double r = 10;
            double l = -10;
            double t = 10;
            double b = -10;
            double f = 10;
            double n = 2;
            transform.M11 = 2 / (r - l);
            transform.M12 = 0;
            transform.M13 = 0;
            transform.M14 = 0;

            transform.M21 = 0;
            transform.M22 = 2 / (t - b);
            transform.M23 = 0;
            transform.M24 = 0;

            transform.M31 = 0;
            transform.M32 = 0;
            transform.M33 = -2 / (f - n);
            transform.M34 = 0;

            transform.M41 = -(r + l) / (r - l);
            transform.M42 = -(t + b) / (t - b);
            transform.M43 = -(f + n) / (f - n);
            transform.M44 = 1;

            OrthographicCameraModel camera = new OrthographicCameraModel(transform, new Vector2(512, 512), 512);
            Vector3 xyz = new Vector3(10, 10, 5);
            double range = 0;
            Vector2 pixel = camera.Project(xyz, out range);
            Vector3 xyzNew = camera.Unproject(pixel, range);
            Assert.AreEqual(xyz, xyzNew);
        }
    }
}
