using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using JPLOPS.Geometry;

namespace GeometryTest
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
            Vertex v1 = new Vertex(new Vector3(1, 2, 3));
            Assert.AreEqual(v1.Position, new Vector3(1, 2, 3));
            Assert.AreEqual(v1.Normal, Vector3.Zero);
            Assert.AreEqual(v1.UV, Vector2.Zero);
            Assert.AreEqual(v1.Color, Vector4.Zero);

            Vertex v2 = new Vertex(1, 2, 3);
            Assert.AreEqual(v2.Position, new Vector3(1, 2, 3));
            Assert.AreEqual(v2.Normal, Vector3.Zero);
            Assert.AreEqual(v2.UV, Vector2.Zero);
            Assert.AreEqual(v2.Color, Vector4.Zero);

            Vertex v3 = new Vertex(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
            Assert.AreEqual(v3.Position, new Vector3(1, 2, 3));
            Assert.AreEqual(v3.Normal, new Vector3(4, 5, 6));
            Assert.AreEqual(v3.UV, new Vector2(7, 8));
            Assert.AreEqual(v3.Color, new Vector4(9, 10, 11, 12));

            Vertex v4 = new Vertex(new Vector3(1, 2, 3), new Vector3(1, 0, 0));
            Assert.AreEqual(v4.Position, new Vector3(1, 2, 3));
            Assert.AreEqual(v4.Normal, new Vector3(1, 0, 0));
            Assert.AreEqual(v4.UV, Vector2.Zero);
            Assert.AreEqual(v4.Color, Vector4.Zero);

            Vertex v5 = new Vertex(new Vector3(1, 2, 3), new Vector3(1, 0, 0), new Vector4(1, 0, 1, 1), new Vector2(1, 1));
            Assert.AreEqual(v5.Position, new Vector3(1, 2, 3));
            Assert.AreEqual(v5.Normal, new Vector3(1, 0, 0));
            Assert.AreEqual(v5.UV, new Vector2(1, 1));
            Assert.AreEqual(v5.Color, new Vector4(1, 0, 1, 1));

            Vertex v6 = new Vertex(v3);
            Assert.AreEqual(v6.Position, new Vector3(1, 2, 3));
            Assert.AreEqual(v6.Normal, new Vector3(4, 5, 6));
            Assert.AreEqual(v6.UV, new Vector2(7, 8));
            Assert.AreEqual(v6.Color, new Vector4(9, 10, 11, 12));

            Vertex v7 = new Vertex(1.23, 4.56, 7.89, -1, 0, 0, 0.25, 0.75, 255, 127, 0, 1);
            Assert.AreEqual(v7.Position, new Vector3(1.23, 4.56, 7.89));
            Assert.AreEqual(v7.Normal, new Vector3(-1, 0, 0));
            Assert.AreEqual(v7.UV, new Vector2(0.25, 0.75));
            Assert.AreEqual(v7.Color, new Vector4(255, 127, 0, 1));
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
        public void VertexHashCodeTest()
        {
            Vertex v = new Vertex(1, 2, 3, 4, 5, 0.1, 0.2, 0.3, 0.1, 3, 4, 0.1);
            Vertex t = (Vertex)v.Clone();
            Assert.AreEqual(t.GetHashCode(), v.GetHashCode());

            v.Position.X += 0.001;
            Assert.AreNotEqual(t.GetHashCode(), v.GetHashCode());
            t = (Vertex)v.Clone();
            v.Position.Y += 0.001;
            Assert.AreNotEqual(t.GetHashCode(), v.GetHashCode());
            t = (Vertex)v.Clone();
            v.Position.Z += 0.001;
            Assert.AreNotEqual(t.GetHashCode(), v.GetHashCode());

            v.Normal.X += 0.001;
            Assert.AreNotEqual(t.GetHashCode(), v.GetHashCode());
            t = (Vertex)v.Clone();
            v.Normal.Y += 0.001;
            Assert.AreNotEqual(t.GetHashCode(), v.GetHashCode());
            t = (Vertex)v.Clone();
            v.Normal.Z += 0.001;
            Assert.AreNotEqual(t.GetHashCode(), v.GetHashCode());

            v.UV.X += 0.001;
            Assert.AreNotEqual(t.GetHashCode(), v.GetHashCode());
            t = (Vertex)v.Clone();
            v.UV.Y += 0.001;
            Assert.AreNotEqual(t.GetHashCode(), v.GetHashCode());
            t = (Vertex)v.Clone();

            v.Color.X += 0.001;
            Assert.AreNotEqual(t.GetHashCode(), v.GetHashCode());
            t = (Vertex)v.Clone();
            v.Color.Y += 0.001;
            Assert.AreNotEqual(t.GetHashCode(), v.GetHashCode());
            t = (Vertex)v.Clone();
            v.Color.Z += 0.001;
            Assert.AreNotEqual(t.GetHashCode(), v.GetHashCode());
            v.Color.A += 0.001;
            Assert.AreNotEqual(t.GetHashCode(), v.GetHashCode());
        }


        [TestMethod]
        public void VertexCloneTest()
        {
            Vertex v0 = new Vertex(1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 11, 22);
            Vertex v1 = (Vertex)v0.Clone();
            Assert.AreEqual(v0 == v1, false);
            Assert.AreEqual(v0, v1);
            v1.Position.X = 37;
            Assert.AreNotEqual(v0, v1);
        }

        [TestMethod]
        public void VertexLerpTest()
        {
            Vertex a = new Vertex(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
            Vertex b = new Vertex(2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14);

            Vertex c = Vertex.Lerp(a, b, 0.7);
            Vertex d = new Vertex(1.7, 2.7, 3.7, 4.7, 5.7, 6.7, 7.7, 8.7, 9.7, 10.7, 11.7, 13.4);
            Assert.AreEqual(d, c);
        }

        [TestMethod]
        public void VertexBoundsTest()
        {
            Vertex a = new Vertex(3, 2, 4);
            Assert.AreEqual(a.Position, a.Bounds().Min);
            Assert.AreEqual(a.Position, a.Bounds().Max);
        }
    }
}
