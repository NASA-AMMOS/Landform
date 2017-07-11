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
            Assert.IsTrue(new BoundingBox(new Vector3(3, -2, 5), new Vector3(5, -1, 8)).FuzzyContains(bb));
            Assert.IsTrue(new BoundingBox(new Vector3(2, -3, 4), new Vector3(6, 0, 9)).FuzzyContains(bb));
            Assert.IsFalse(new BoundingBox(new Vector3(2, -3, 4), new Vector3(6, 0, 7)).FuzzyContains(bb));

            BoundingBox bb8 = new BoundingBox(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
            Assert.IsFalse(bb8.FuzzyContains(new BoundingBox(new Vector3(-1, 0, 0), new Vector3(1, 1, 1))));
            Assert.IsFalse(bb8.FuzzyContains(new BoundingBox(new Vector3(0, -1, 0), new Vector3(1, 1, 1))));
            Assert.IsFalse(bb8.FuzzyContains(new BoundingBox(new Vector3(0, 0, -1), new Vector3(1, 1, 1))));
            Assert.IsFalse(bb8.FuzzyContains(new BoundingBox(new Vector3(0, 0, 0), new Vector3(2, 1, 1))));
            Assert.IsFalse(bb8.FuzzyContains(new BoundingBox(new Vector3(0, 0, 0), new Vector3(1, 2, 1))));
            Assert.IsFalse(bb8.FuzzyContains(new BoundingBox(new Vector3(0, 0, 0), new Vector3(1, 1, 2))));
        }

        [TestMethod]
        public void BoundingBoxToFromRectangle()
        {
            BoundingBox bb = new BoundingBox(new Vector3(3, -2, 5), new Vector3(5, -1, 8));
            BoundingBox bb8 = bb.ToRectangle().ToBoundingBox();
            Assert.AreEqual(bb.Min, bb8.Min);
            Assert.AreEqual(bb.Max, bb8.Max);
        }

        [TestMethod]
        public void BoundingBoxMaxDimension()
        {
            BoundingBox bb = new BoundingBox(new Vector3(3, -2, 5), new Vector3(5, -1, 8));
            Assert.AreEqual(3, bb.MaxDimension());
            bb = new BoundingBox(new Vector3(3, -2, 5), new Vector3(5, 7, 8));
            Assert.AreEqual(9, bb.MaxDimension());
        }

        [TestMethod]
        public void BoundingBoxCenterTest()
        {
            BoundingBox bb = new BoundingBox(new Vector3(3, -2, 5), new Vector3(5, -1, 8));
            Assert.AreEqual(new Vector3(4, -1.5, 6.5), bb.Center());
        }

    }
}
