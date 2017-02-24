using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace OPS.Geometry
{
    [TestClass]
    public class VertexTest
    {

        Vertex VertexFactory()
        {
            Vertex v = new Vertex(1, 2, 3);
            v.UV = new Vector2(4, 5);
            v.Normal = new Vector3(6, 7, 8);
            v.Color = new Vector4(9, 10, 11, 12);
            return v;
        }

        [TestMethod]
        public void VertexConstructorTest()
        {
            Vertex v = new Vertex(1, 2, 3);
            Assert.AreEqual(v.Position, new Vector3(1, 2, 3));
            Assert.AreEqual(v.Normal, Vector3.Zero);
            Assert.AreEqual(v.UV, Vector2.Zero);
            Assert.AreEqual(v.Color, Vector4.Zero);

            Vertex v1 = new Vertex(new Vector3(1, 2, 3));
            Assert.AreEqual(v1.Position, new Vector3(1, 2, 3));
            Assert.AreEqual(v1.Normal, Vector3.Zero);
            Assert.AreEqual(v1.UV, Vector2.Zero);
            Assert.AreEqual(v1.Color, Vector4.Zero);

            Vertex v2 = new Vertex(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
            Assert.AreEqual(v2.Position, new Vector3(1, 2, 3));
            Assert.AreEqual(v2.Normal, new Vector3(4, 5, 6));
            Assert.AreEqual(v2.UV, new Vector2(7, 8));
            Assert.AreEqual(v2.Color, new Vector4(9, 10, 11, 12));

            Vertex v3 = new Vertex(v2);
            Assert.AreEqual(v3.Position, new Vector3(1, 2, 3));
            Assert.AreEqual(v3.Normal, new Vector3(4, 5, 6));
            Assert.AreEqual(v3.UV, new Vector2(7, 8));
            Assert.AreEqual(v3.Color, new Vector4(9, 10, 11, 12));
        }

        [TestMethod]
        public void VertexEqualityTest()
        {
            Vertex v1 = new Vertex(new Vector3(1, 2, 3));
            Vertex v2 = new Vertex(new Vector3(1, 2, 3));
            Assert.IsFalse(v1 == v2);
            Assert.IsTrue(v1.Equals(v2));
            Assert.AreEqual(v1.GetHashCode(), v2.GetHashCode());

            Vertex a = VertexFactory();
            Vertex b = VertexFactory();
            b.Position.X = 99;
            Vertex c = VertexFactory();
            c.UV.U = 0.2;
            Vertex d = VertexFactory();
            d.Normal.Z = 22;
            Vertex e = VertexFactory();
            e.Color.A = 0;

            Dictionary<Vertex, int> dict = new Dictionary<Vertex, int>();
            int i = 0;
            Vertex[] verts = new Vertex[] { v1, a, b, c, d, e };
            foreach (var v in verts)
            {
                dict.Add(v, i++);
            }
            Assert.IsTrue(dict.ContainsKey(v2));
            i = 0;
            foreach (var v in verts)
            {
                int x = dict[v];
                Assert.AreEqual(dict[v], i++);
            }
            Assert.AreEqual(dict[v2], 0);
            for (i = 0; i < verts.Length - 1; i++)
            {
                for (int j = i + 1; j < verts.Length; j++)
                {
                    Assert.IsFalse(verts[i].Equals(verts[j]));
                }
            }
        }

        [TestMethod]
        public void VertexCloneTest()
        {
            Vertex v0 = new Vertex(1,2,3,4,5,6,7,8,9,0,11,22);
            Vertex v1 = (Vertex) v0.Clone();
            Assert.AreEqual(v0 == v1, false);
            Assert.AreEqual(v0, v1);
            v1.Position.X = 37;
            Assert.AreNotEqual(v0, v1);
        }
    }
}
