using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Geometry;
using Microsoft.Xna.Framework;

namespace GeometryTest.Geometry
{
    /// <summary>
    /// Summary description for UnitTest1
    /// </summary>
    [TestClass]
    public class PlaneExtensionTest
    {     
        [TestMethod]
        public void TestPlaneIntersectT()
        {
            Plane p = new Plane(Vector3.Up, -2);
            Assert.AreEqual(null,p.IntersectT(new Vector3(1, 1, 1), new Vector3(0, 0, 0)));
            Assert.AreEqual(0.5, p.IntersectT(new Vector3(1, 1, 1), new Vector3(1, 3, 1)).Value);
            Assert.AreEqual(1, p.IntersectT(new Vector3(0, 0, 0), new Vector3(2, 2, 2)).Value);
            Assert.AreEqual(2.0/3, p.IntersectT(new Vector3(0, 0, 0), new Vector3(3, 3, 3)).Value);
        }


        [TestMethod]
        public void TestPlaneIntersect()
        {
            Plane p = new Plane(Vector3.Up, -2);
            Assert.AreEqual(null, p.Intersect(new Vertex(1, 1, 1), new Vertex(0, 0, 0)));
            Assert.AreEqual(new Vertex(1,2,1), p.Intersect(new Vertex(1, 1, 1), new Vertex(1, 3, 1)));
            Assert.AreEqual(new Vertex(2, 2, 2), p.Intersect(new Vertex(0, 0, 0), new Vertex(2, 2, 2)));
            Assert.AreEqual(new Vertex(2, 2, 2), p.Intersect(new Vertex(0, 0, 0), new Vertex(3, 3, 3)));
        }
    }
}
