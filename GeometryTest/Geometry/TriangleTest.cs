using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using System.Linq;
using OPS.MathExtensions;

namespace GeometryTest
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

        [TestMethod]
        public void TriangleBoundsTest()
        {
            Triangle a = new Triangle(new Vertex(0, 0, 0), new Vertex(10, 1, 2), new Vertex(-1, -3, 1));
            var b = a.Bounds();
            Assert.AreEqual(new Vector3(-1, -3, 0),  b.Min);
            Assert.AreEqual(new Vector3(10, 1, 2), b.Max);
        }

        [TestMethod]
        public void TriangleVerticesTest()
        {
            Triangle a = new Triangle(new Vertex(0, 0, 0), new Vertex(10, 1, 2), new Vertex(-1, -3, 1));
            var verts = a.Vertices();
            Assert.AreEqual(a.V0, verts[0]);
            Assert.AreEqual(a.V1, verts[1]);
            Assert.AreEqual(a.V2, verts[2]);
            a.V0.Position.X = 7;
            Assert.AreEqual(7, verts[0].Position.X);
        }

        void AssertVerticesContain(Triangle t, Vertex[] verts)
        {
            List<Vertex> triangleVerts = new Vertex[] { t.V0, t.V1, t.V2 }.ToList();
            foreach(Vertex v in verts)
            {
                if(!triangleVerts.Contains(v))
                {
                    Assert.Fail();
                }
                triangleVerts.Remove(v);
            }
        }

        [TestMethod]
        public void TriangleClipTest()
        {
            Plane p = new Plane(Vector3.Up, 2);
            Triangle t = new Triangle(new Vertex(0, 0, 0), new Vertex(1, 0, 0), new Vertex(1, 1,0));
            // Test triangle completly below the plane
            Assert.AreEqual(0, t.Clip(p).ToArray().Count());
            // Test triangle completly above the plane
            p.D = -2;
            Assert.AreEqual(1, t.Clip(p).ToArray().Count());
            Triangle other = t.Clip(p).ToArray()[0];
            Assert.AreEqual(t.V0, other.V0);
            Assert.AreEqual(t.V1, other.V1);
            Assert.AreEqual(t.V2, other.V2);
            // Test triangle with top part above the plane
            p.D = 0.5;
            Assert.AreEqual(1, t.Clip(p).ToArray().Count());
            AssertVerticesContain(t.Clip(p).ToArray()[0], new Vertex[] { new Vertex(0.5, 0.5, 0), new Vertex(1, 0.5, 0), new Vertex(1,1,0) });
            // Test triangle with bottom part above the plane
            p.D = -0.5;
            p.Normal *= -1;
            Assert.AreEqual(2, t.Clip(p).ToArray().Count());
            Triangle a = t.Clip(p).ToArray()[0];
            Triangle b = t.Clip(p).ToArray()[1];
            AssertVerticesContain(a, new Vertex[] { new Vertex(0.5, 0.5, 0), new Vertex(1, 0, 0), new Vertex(0, 0, 0) });
            AssertVerticesContain(b, new Vertex[] { new Vertex(0.5, 0.5, 0), new Vertex(1, 0, 0), new Vertex(1, 0.5, 0) });
        }

        [TestMethod]
        public void TraingleClibBoxTest()
        {
            BoundingBox box = new BoundingBox(new Vector3(1, 0, 2), new Vector3(3, 2, 4));
            Random r = new Random(17);
            for(int i = 0; i < 200; i++)
            {
                Triangle t = new Triangle(new Vertex(r.NextDouble() * 4, r.NextDouble() * 4, r.NextDouble() * 7), new Vertex(r.NextDouble() * 4, r.NextDouble() * 4, r.NextDouble() * 7), new Vertex(r.NextDouble() * 4, r.NextDouble() * 4, r.NextDouble() * 7));
                foreach(var clippedT in t.Clip(box))
                {
                    foreach(var v in clippedT.Vertices())
                    {
                        if(v.Position.X < box.Min.X - MathE.EPSILON || v.Position.Y < box.Min.Y - MathE.EPSILON || v.Position.Z < box.Min.Z - MathE.EPSILON)
                        {
                            Assert.Fail();
                        }
                        if (v.Position.X > box.Max.X + MathE.EPSILON || v.Position.Y > box.Max.Y + MathE.EPSILON || v.Position.Z > box.Max.Z + MathE.EPSILON)
                        {
                            Assert.Fail();
                        }
                    }
                }
            }
        }

        [TestMethod]
        public void TriangleUVToBarycentricTest()
        {
            Vertex v0 = new Vertex();
            Vertex v1 = new Vertex();
            Vertex v2 = new Vertex();
            v0.UV = new Vector2(1, 1);
            v1.UV = new Vector2(1, 2);
            v2.UV = new Vector2(3, 1);
            Triangle tri = new Triangle(v0, v1, v2);
            Assert.AreEqual(new Vector2(1, 1), tri.UVToBarycentric(new Vector2(1, 1)).UV);
            Assert.AreEqual(new Vector2(1, 2), tri.UVToBarycentric(new Vector2(1, 2)).UV);
            Assert.AreEqual(new Vector2(3, 1), tri.UVToBarycentric(new Vector2(3, 1)).UV);
            Assert.AreEqual(new Vector2(2, 1), tri.UVToBarycentric(new Vector2(2, 1)).UV);
            Assert.AreEqual(new Vector2(1, 1.5), tri.UVToBarycentric(new Vector2(1, 1.5)).UV);
            Assert.AreEqual(new Vector2(2, 1.5), tri.UVToBarycentric(new Vector2(2, 1.5)).UV);
            Assert.AreEqual(new Vector2(2, 1.25), tri.UVToBarycentric(new Vector2(2, 1.25)).UV);
            Assert.AreEqual(null, tri.UVToBarycentric(new Vector2(3, 2)));
        }

        [TestMethod]
        public void TriangleClosestPointTest()
        {
            Triangle tri1 = new Triangle(new Vertex(1, 1, 1), new Vertex(1, 2, 2), new Vertex(3, 1, 2));
            Assert.AreEqual(new Vector3(1, 1, 1), tri1.ClosestPoint(new Vector3(1, 1, 1)).Position);
            Assert.AreEqual(new Vector3(1, 2, 2), tri1.ClosestPoint(new Vector3(1, 2, 2)).Position);
            Assert.AreEqual(new Vector3(3, 1, 2), tri1.ClosestPoint(new Vector3(3, 1, 2)).Position);
            Assert.AreEqual(new Vector3(2, 1, 1.5), tri1.ClosestPoint(new Vector3(2, 1, 1.5)).Position);
            Assert.AreEqual(new Vector3(1, 1.5, 1.5), tri1.ClosestPoint(new Vector3(1, 1.5, 1.5)).Position);
            Assert.AreEqual(new Vector3(2, 1.5, 2), tri1.ClosestPoint(new Vector3(2, 1.5, 2)).Position);
            Assert.AreEqual(new Vector3(2, 1.25, 1.75), tri1.ClosestPoint(new Vector3(2, 1.25, 1.75)).Position);

            Assert.AreEqual(new Vector3(1,1,1), tri1.ClosestPoint(new Vector3(1, 0, -1)).Position);
            Assert.AreEqual(new Vector3(1, 2, 2), tri1.ClosestPoint(new Vector3(1, 10, 2)).Position);
            Assert.AreEqual(new Vector3(3, 1, 2), tri1.ClosestPoint(new Vector3(20, 1, 4)).Position);

            Triangle tri = new Triangle(new Vertex(1, 1, 0), new Vertex(0, 1, 0), new Vertex(1, 0, 0));
            Assert.AreEqual(new Vector3(1, 1, 0), tri.ClosestPoint(new Vector3(1, 1, 1)).Position);
            Assert.AreEqual(new Vector3(.75, .75, 0), tri.ClosestPoint(new Vector3(.75, .75, -1.6456168)).Position);
            Assert.AreEqual(new Vector3(.5, .5, 0), tri.ClosestPoint(new Vector3(0, 0, 0)).Position);
            Assert.AreEqual(new Vector3(.5, .5, 0), tri.ClosestPoint(new Vector3(0, 0, 8798797.65)).Position);
            Assert.AreEqual(new Vector3(1, 0, 0), tri.ClosestPoint(new Vector3(1.5, -7, -654)).Position);
        }
    }
}
