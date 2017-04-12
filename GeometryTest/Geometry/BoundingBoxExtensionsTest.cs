using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Geometry;
using Microsoft.Xna.Framework;

namespace GeometryTest.Geometry
{
    [TestClass]
    public class BoundingBoxExtensionsTest
    {

        [TestMethod]
        public void BoundingBoxSizeTest()
        {
            Assert.AreEqual(new Vector3(2, 1, 3), new BoundingBox(new Vector3(3, -2, 5), new Vector3(5, -1, 8)).Size());
        }

        [TestMethod]
        public void BoundingBoxInsideTest()
        {
            BoundingBox bb = new BoundingBox(new Vector3(3, -2, 5), new Vector3(5, -1, 8));
            Assert.IsTrue(bb.Inside(new BoundingBox(new Vector3(3, -2, 5), new Vector3(5, -1, 8))));
            Assert.IsTrue(bb.Inside(new BoundingBox(new Vector3(2, -3, 4), new Vector3(6, 0, 9))));
            Assert.IsFalse(bb.Inside(new BoundingBox(new Vector3(2, -3, 4), new Vector3(6, 0, 7))));
        }

        [TestMethod]
        public void BoundingBoxToFromRectangle()
        {
            BoundingBox bb = new BoundingBox(new Vector3(3, -2, 5), new Vector3(5, -1, 8));
            BoundingBox bb8 = bb.ToRectangle().ToBoundingBox();
            Assert.AreEqual(bb.Min, bb8.Min);
            Assert.AreEqual(bb.Max, bb8.Max);
        }
    }
}
