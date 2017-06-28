using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;

namespace ImagingTest
{
    [TestClass]
    class ImageCoordinateTest
    {

        [TestMethod]
        public void TestCoordinateConstructor()
        {
            ImageCoordinate ic = new ImageCoordinate(1, 2, 3);
            Assert.AreEqual(1, ic.b);
            Assert.AreEqual(2, ic.r);
            Assert.AreEqual(3, ic.c);
        }
    }
}
