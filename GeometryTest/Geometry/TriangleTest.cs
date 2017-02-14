using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;

namespace OPS.Geometry
{
    /// <summary>
    /// Test triangle data type
    /// </summary>
    [TestClass]
    public class TriangleTest
    {
        [TestMethod]
        public void TriangleConstructorTest()
        {
            Triangle t = new Triangle();
            Assert.AreEqual(t.V0, null);
            Assert.AreEqual(t.V1, null);
            Assert.AreEqual(t.V2, null);

            Vertex v0 = new Vertex(0, 1, 2);
            Vertex v1 = new Vertex(3, 4, 5);
            Vertex v2 = new Vertex(6, 7, 8);
            Triangle a = new Triangle(v0, v1, v2);
            Assert.AreEqual(v0 == a.V0, false);
            Assert.AreEqual(v1 == a.V1, false);
            Assert.AreEqual(v2 == a.V2, false);
            Assert.AreEqual(v0, a.V0);
            Assert.AreEqual(v1, a.V1);
            Assert.AreEqual(v2, a.V2);
            a.V0.Position.X = 10;
            a.V1.Position.X = 11;
            a.V2.Position.X = 12;
            Assert.AreNotEqual(v0, a.V0);
            Assert.AreNotEqual(v1, a.V1);
            Assert.AreNotEqual(v2, a.V2);
            Assert.AreEqual(v0, new Vertex(0, 1, 2));

            Triangle b = new Triangle(a);
            b.V0.Position.Z = 42;
            Assert.AreEqual(a.V0.Position, new Vector3(10, 1, 2));
            Assert.AreEqual(b.V0.Position, new Vector3(10, 1, 42));

        }
    }
}
