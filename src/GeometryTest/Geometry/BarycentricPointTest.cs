using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Geometry;
using Microsoft.Xna.Framework;

namespace GeometryTest
{
    [TestClass]
    public class BarycentricPointTest
    {
        [TestMethod]
        public void GetXYZFromST()
        {
            // Note: Lower left corner at (0,0,0) does not test offset, see below
            Triangle tri = new Triangle(new Vertex(0, 0, 0), new Vertex(0,1,1), new Vertex(2,0,1));
            BarycentricPoint p0 = new BarycentricPoint(0,0,tri);
            BarycentricPoint p1 = new BarycentricPoint(1, 0, tri);
            BarycentricPoint p2 = new BarycentricPoint(0, 1, tri);
            BarycentricPoint p3 = new BarycentricPoint(0, .5, tri);
            BarycentricPoint p4 = new BarycentricPoint(.5, 0, tri);
            BarycentricPoint p5 = new BarycentricPoint(.5, .5, tri);
            BarycentricPoint p6 = new BarycentricPoint(.25, .5, tri);
            Assert.AreEqual(p0.Position, new Vector3(0,0,0));
            Assert.AreEqual(p1.Position, new Vector3(0, 1, 1));
            Assert.AreEqual(p2.Position, new Vector3(2, 0, 1));
            Assert.AreEqual(p3.Position, new Vector3(1, 0, .5));
            Assert.AreEqual(p4.Position, new Vector3(0, .5, .5));
            Assert.AreEqual(p5.Position, new Vector3(1, .5, 1));
            Assert.AreEqual(p6.Position, new Vector3(1, .25, .75));

            // Same as above, but shifted by <1,1,1> to test offset
            Triangle tri1 = new Triangle(new Vertex(1, 1, 1), new Vertex(1, 2, 2), new Vertex(3, 1, 2));
            BarycentricPoint r0 = new BarycentricPoint(0, 0, tri1);
            BarycentricPoint r1 = new BarycentricPoint(1, 0, tri1);
            BarycentricPoint r2 = new BarycentricPoint(0, 1, tri1);
            BarycentricPoint r3 = new BarycentricPoint(0, .5, tri1);
            BarycentricPoint r4 = new BarycentricPoint(.5, 0, tri1);
            BarycentricPoint r5 = new BarycentricPoint(.5, .5, tri1);
            BarycentricPoint r6 = new BarycentricPoint(.25, .5, tri1);
            Assert.AreEqual(r0.Position, new Vector3(1, 1, 1));
            Assert.AreEqual(r1.Position, new Vector3(1, 2, 2));
            Assert.AreEqual(r2.Position, new Vector3(3, 1, 2));
            Assert.AreEqual(r3.Position, new Vector3(2, 1, 1.5));
            Assert.AreEqual(r4.Position, new Vector3(1, 1.5, 1.5));
            Assert.AreEqual(r5.Position, new Vector3(2, 1.5, 2));
            Assert.AreEqual(r6.Position, new Vector3(2, 1.25, 1.75));
        }

        [TestMethod]
        public void GetUVFromST()
        {
            Vertex v0 = new Vertex();
            Vertex v1 = new Vertex();
            Vertex v2 = new Vertex();
            v0.UV = new Vector2(0, 0);
            v1.UV = new Vector2(0, 1);
            v2.UV = new Vector2(2, 0);
            Triangle tri = new Triangle(v0, v1, v2);
            BarycentricPoint p0 = new BarycentricPoint(0, 0, tri);
            BarycentricPoint p1 = new BarycentricPoint(1, 0, tri);
            BarycentricPoint p2 = new BarycentricPoint(0, 1, tri);
            BarycentricPoint p3 = new BarycentricPoint(0, .5, tri);
            BarycentricPoint p4 = new BarycentricPoint(.5, 0, tri);
            BarycentricPoint p5 = new BarycentricPoint(.5, .5, tri);
            BarycentricPoint p6 = new BarycentricPoint(.25, .5, tri);
            Assert.AreEqual(p0.UV, new Vector2(0, 0));
            Assert.AreEqual(p1.UV, new Vector2(0, 1));
            Assert.AreEqual(p2.UV, new Vector2(2, 0));
            Assert.AreEqual(p3.UV, new Vector2(1, 0));
            Assert.AreEqual(p4.UV, new Vector2(0, .5));
            Assert.AreEqual(p5.UV, new Vector2(1, .5));
            Assert.AreEqual(p6.UV, new Vector2(1, .25));

            v0.UV = new Vector2(1, 1);
            v1.UV = new Vector2(1, 2);
            v2.UV = new Vector2(3, 1);
            tri = new Triangle(v0, v1, v2);
            BarycentricPoint r0 = new BarycentricPoint(0, 0, tri);
            BarycentricPoint r1 = new BarycentricPoint(1, 0, tri);
            BarycentricPoint r2 = new BarycentricPoint(0, 1, tri);
            BarycentricPoint r3 = new BarycentricPoint(0, .5, tri);
            BarycentricPoint r4 = new BarycentricPoint(.5, 0, tri);
            BarycentricPoint r5 = new BarycentricPoint(.5, .5, tri);
            BarycentricPoint r6 = new BarycentricPoint(.25, .5, tri);
            Assert.AreEqual(r0.UV, new Vector2(1, 1));
            Assert.AreEqual(r1.UV, new Vector2(1, 2));
            Assert.AreEqual(r2.UV, new Vector2(3, 1));
            Assert.AreEqual(r3.UV, new Vector2(2, 1));
            Assert.AreEqual(r4.UV, new Vector2(1, 1.5));
            Assert.AreEqual(r5.UV, new Vector2(2, 1.5));
            Assert.AreEqual(r6.UV, new Vector2(2, 1.25));
        }

        [TestMethod]
        public void GetXYZFromBary()
        {
            Triangle tri1 = new Triangle(new Vertex(1, 1, 1), new Vertex(1, 2, 2), new Vertex(3, 1, 2));
            BarycentricPoint p0 = new BarycentricPoint(1, 0, 0, tri1);
            BarycentricPoint p1 = new BarycentricPoint(0, 1, 0, tri1);
            BarycentricPoint p2 = new BarycentricPoint(0, 0, 1, tri1);
            BarycentricPoint p3 = new BarycentricPoint(.5, 0, .5, tri1);
            BarycentricPoint p4 = new BarycentricPoint(.5, .5, 0, tri1);
            BarycentricPoint p5 = new BarycentricPoint(0, .5, .5, tri1);
            BarycentricPoint p6 = new BarycentricPoint(.25, .25, .5, tri1);
            Assert.AreEqual(p0.Position, new Vector3(1, 1, 1));
            Assert.AreEqual(p1.Position, new Vector3(1, 2, 2));
            Assert.AreEqual(p2.Position, new Vector3(3, 1, 2));
            Assert.AreEqual(p3.Position, new Vector3(2, 1, 1.5));
            Assert.AreEqual(p4.Position, new Vector3(1, 1.5, 1.5));
            Assert.AreEqual(p5.Position, new Vector3(2, 1.5, 2));
            Assert.AreEqual(p6.Position, new Vector3(2, 1.25, 1.75));
        }

        [TestMethod]
        public void GetUVFromBary()
        {
            Vertex v0 = new Vertex();
            Vertex v1 = new Vertex();
            Vertex v2 = new Vertex();
            v0.UV = new Vector2(1, 1);
            v1.UV = new Vector2(1, 2);
            v2.UV = new Vector2(3, 1);
            Triangle tri = new Triangle(v0, v1, v2);
            BarycentricPoint r0 = new BarycentricPoint(1, 0, 0, tri);
            BarycentricPoint r1 = new BarycentricPoint(0, 1, 0, tri);
            BarycentricPoint r2 = new BarycentricPoint(0, 0, 1, tri);
            BarycentricPoint r3 = new BarycentricPoint(.5, 0, .5, tri);
            BarycentricPoint r4 = new BarycentricPoint(.5, .5, 0, tri);
            BarycentricPoint r5 = new BarycentricPoint(0, .5, .5, tri);
            BarycentricPoint r6 = new BarycentricPoint(.25, .25, .5, tri);
            Assert.AreEqual(r0.UV, new Vector2(1, 1));
            Assert.AreEqual(r1.UV, new Vector2(1, 2));
            Assert.AreEqual(r2.UV, new Vector2(3, 1));
            Assert.AreEqual(r3.UV, new Vector2(2, 1));
            Assert.AreEqual(r4.UV, new Vector2(1, 1.5));
            Assert.AreEqual(r5.UV, new Vector2(2, 1.5));
            Assert.AreEqual(r6.UV, new Vector2(2, 1.25));
        }
    }
}
