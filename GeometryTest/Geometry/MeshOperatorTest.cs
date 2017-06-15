using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace GeometryTest
{
    /// <summary>
    /// Summary description for MeshOperatorTest
    /// </summary>
    [TestClass]
    public class MeshOperatorTest
    {

        [TestMethod]
        public void MeshOperatorClipTest()
        {
            Random r = new Random(17);
            List<Triangle> tris = new List<Triangle>();
            for (int i = 0; i < 200; i++)
            {
                tris.Add(new Triangle(new Vertex((r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10),
                                     new Vertex((r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10),
                                     new Vertex((r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10, (r.NextDouble() - 0.5) * 10)));
            }
            Mesh m = new Mesh(tris);
            MeshOperator mo = new MeshOperator(m);
            BoundingBox bb = new BoundingBox(new Vector3(-2, -3, -4), new Vector3(-1, -1, -2));
            Mesh clipped = mo.Clip(bb);
            BoundingBox clippedBB = clipped.Bounds();
            Assert.IsTrue(Vector3.AlmostEqual(clippedBB.Min, bb.Min));
            Assert.IsTrue(Vector3.AlmostEqual(clippedBB.Max, bb.Max));

            Mesh other = Mesh.Clip(m, bb);
            Assert.AreEqual(other.Vertices.Count, clipped.Vertices.Count);
            Assert.AreEqual(other.Faces.Count, clipped.Faces.Count);
            Assert.IsTrue(mo.CountFaces(bb) > 0);
            Assert.IsTrue(mo.CountVertices(bb) > 0);
            Assert.IsFalse(mo.Empty(bb));
        }

        [TestMethod]
        public void MeshOperatorClipPointCloudTest()
        {
            Random r = new Random(17);
            Mesh m = new Mesh();
            for (int i = 0; i < 10000; i++)
            {
                m.Vertices.Add(new Vertex((r.NextDouble() - 0.5) * 5, (r.NextDouble() - 0.5) * 5, (r.NextDouble() - 0.5) * 5));
            }
            MeshOperator mo = new MeshOperator(m);
            BoundingBox bb = new BoundingBox(new Vector3(-4, -4, -4), new Vector3(3, 2, -2));
            Mesh clipped = mo.Clip(bb);
            BoundingBox clippedBB = clipped.Bounds();
            Assert.IsTrue(bb.FuzzyContains(clippedBB));
            Assert.IsTrue(clipped.Vertices.Count > 0);

            Mesh other = Mesh.Clip(m, bb);
            Assert.AreEqual(other.Vertices.Count, clipped.Vertices.Count);
            Assert.AreEqual(clipped.Vertices.Count, mo.CountVertices(bb));
            Assert.AreEqual(0, mo.CountFaces(bb));
            mo.CountVertices(bb);
            Assert.IsFalse(mo.Empty(bb));
        }
    }
}
