using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Geometry;
using Microsoft.Xna.Framework;

namespace GeometryTest.Geometry
{
    [TestClass]
    public class BoundingBoxExtensionsTest
    {

        [TestMethod]
        public void BoundingBoxExtentTest()
        {
            Assert.AreEqual(new Vector3(2, 1, 3), new BoundingBox(new Vector3(3, -2, 5), new Vector3(5, -1, 8)).Extent());
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

        [TestMethod]
        public void BoundingBoxUnionTest()
        {
            BoundingBox r2 = new BoundingBox(new Vector3(0, -1.5, 2), new Vector3(6, 1, 2.5));
            BoundingBox c3 = new BoundingBox(new Vector3(3, 5, 0), new Vector3(4, 6, 2.5));
            BoundingBox d2 = new BoundingBox(new Vector3(1, -1.25, 1.5), new Vector3(18, 0, 2));
            BoundingBox p0 = new BoundingBox(new Vector3(4, 3.14, 1), new Vector3(5, 5, 2.5));
            BoundingBox c3p0 = BoundingBoxExtensions.Union(c3, p0);
            BoundingBox r2d2 = BoundingBoxExtensions.Union(r2, d2);
            BoundingBox trouble = BoundingBoxExtensions.Union(c3p0, r2d2);
            Assert.AreEqual(c3p0, new BoundingBox(new Vector3(3, 3.14, 0), new Vector3(5, 6, 2.5)));
            Assert.AreEqual(r2d2, new BoundingBox(new Vector3(0, -1.5, 1.5), new Vector3(18, 1, 2.5)));
            Assert.AreEqual(trouble, new BoundingBox(new Vector3(0, -1.5, 0), new Vector3(18, 6, 2.5)));
            Assert.AreEqual(trouble, BoundingBoxExtensions.Union( r2, d2, c3, p0));
            Assert.AreEqual(r2, BoundingBoxExtensions.Union(r2));
        }


        [TestMethod]
        public void BoundingBoxDistanceTest()
        {
            BoundingBox a = new BoundingBox(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
            BoundingBox b = new BoundingBox(new Vector3(2, 0, 0), new Vector3(3, 0, 0));
            Assert.AreEqual(1, a.ClosestDistanceSquared(b));
            Assert.AreEqual(Vector3.DistanceSquared(new Vector3(0,0,0), new Vector3(3,1,1)), a.FurthestDistanceSquared(b));
            a = new BoundingBox(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
            b = new BoundingBox(new Vector3(-2, -1, -1), new Vector3(-1, 0, 0));
            Assert.AreEqual(1, a.ClosestDistanceSquared(b));
            Assert.AreEqual(Vector3.DistanceSquared(new Vector3(-2, -1, -1), new Vector3(1, 1, 1)), a.FurthestDistanceSquared(b));
            a = new BoundingBox(new Vector3(1, -3, 7), new Vector3(2, 5, 9));
            b = new BoundingBox(new Vector3(-2, 7, 6), new Vector3(0, 10, 8));
            Assert.AreEqual(1 * 1 + 2 * 2 + 0 * 0, a.ClosestDistanceSquared(b));
            Assert.AreEqual(Vector3.DistanceSquared(new Vector3(-2, -3, 6), new Vector3(2, 10, 9)), a.FurthestDistanceSquared(b));
        }

    }
}
