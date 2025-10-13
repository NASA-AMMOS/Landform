using Xunit;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using JPLOPS.Geometry;

namespace GeometryTest
{
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

        [Fact]
        public void VertexConstructorTest()
        {
            Vertex v1 = new Vertex(new Vector3(1, 2, 3));
            Assert.Equal(new Vector3(1, 2, 3), v1.Position);
            Assert.Equal(Vector3.Zero, v1.Normal);
            Assert.Equal(Vector2.Zero, v1.UV);
            Assert.Equal(Vector4.Zero, v1.Color);

            Vertex v2 = new Vertex(1, 2, 3);
            Assert.Equal(new Vector3(1, 2, 3), v2.Position);
            Assert.Equal(Vector3.Zero, v2.Normal);
            Assert.Equal(Vector2.Zero, v2.UV);
            Assert.Equal(Vector4.Zero, v2.Color);

            Vertex v3 = new Vertex(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
            Assert.Equal(new Vector3(1, 2, 3), v3.Position);
            Assert.Equal(new Vector3(4, 5, 6), v3.Normal);
            Assert.Equal(new Vector2(7, 8), v3.UV);
            Assert.Equal(new Vector4(9, 10, 11, 12), v3.Color);

            Vertex v4 = new Vertex(new Vector3(1, 2, 3), new Vector3(1, 0, 0));
            Assert.Equal(new Vector3(1, 2, 3), v4.Position);
            Assert.Equal(new Vector3(1, 0, 0), v4.Normal);
            Assert.Equal(Vector2.Zero, v4.UV);
            Assert.Equal(Vector4.Zero, v4.Color);

            Vertex v5 = new Vertex(new Vector3(1, 2, 3), new Vector3(1, 0, 0), new Vector4(1, 0, 1, 1), new Vector2(1, 1));
            Assert.Equal(new Vector3(1, 2, 3), v5.Position);
            Assert.Equal(new Vector3(1, 0, 0), v5.Normal);
            Assert.Equal(new Vector2(1, 1), v5.UV);
            Assert.Equal(new Vector4(1, 0, 1, 1), v5.Color);

            Vertex v6 = new Vertex(v3);
            Assert.Equal(new Vector3(1, 2, 3), v6.Position);
            Assert.Equal(new Vector3(4, 5, 6), v6.Normal);
            Assert.Equal(new Vector2(7, 8), v6.UV);
            Assert.Equal(new Vector4(9, 10, 11, 12), v6.Color);

            Vertex v7 = new Vertex(1.23, 4.56, 7.89, -1, 0, 0, 0.25, 0.75, 255, 127, 0, 1);
            Assert.Equal(new Vector3(1.23, 4.56, 7.89), v7.Position);
            Assert.Equal(new Vector3(-1, 0, 0), v7.Normal);
            Assert.Equal(new Vector2(0.25, 0.75), v7.UV);
            Assert.Equal(new Vector4(255, 127, 0, 1), v7.Color);
        }

        [Fact]
        public void VertexEqualityTest()
        {
            Vertex v1 = new Vertex(new Vector3(1, 2, 3));
            Vertex v2 = new Vertex(new Vector3(1, 2, 3));
            Assert.False(v1 == v2);
            Assert.True(v1.Equals(v2));
            Assert.Equal(v1.GetHashCode(), v2.GetHashCode());

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
            Assert.True(dict.ContainsKey(v2));
            i = 0;
            foreach (var v in verts)
            {
                int x = dict[v];
                Assert.Equal(i++, dict[v]);
            }
            Assert.Equal(0, dict[v2]);
            for (i = 0; i < verts.Length - 1; i++)
            {
                for (int j = i + 1; j < verts.Length; j++)
                {
                    Assert.False(verts[i].Equals(verts[j]));
                }
            }
        }

        [Fact]
        public void VertexHashCodeTest()
        {
            Vertex v = new Vertex(1, 2, 3, 4, 5, 0.1, 0.2, 0.3, 0.1, 3, 4, 0.1);
            Vertex t = (Vertex)v.Clone();
            Assert.Equal(v.GetHashCode(), t.GetHashCode());

            v.Position.X += 0.001;
            Assert.NotEqual(v.GetHashCode(), t.GetHashCode());
            t = (Vertex)v.Clone();
            v.Position.Y += 0.001;
            Assert.NotEqual(v.GetHashCode(), t.GetHashCode());
            t = (Vertex)v.Clone();
            v.Position.Z += 0.001;
            Assert.NotEqual(v.GetHashCode(), t.GetHashCode());

            v.Normal.X += 0.001;
            Assert.NotEqual(v.GetHashCode(), t.GetHashCode());
            t = (Vertex)v.Clone();
            v.Normal.Y += 0.001;
            Assert.NotEqual(v.GetHashCode(), t.GetHashCode());
            t = (Vertex)v.Clone();
            v.Normal.Z += 0.001;
            Assert.NotEqual(v.GetHashCode(), t.GetHashCode());

            v.UV.X += 0.001;
            Assert.NotEqual(v.GetHashCode(), t.GetHashCode());
            t = (Vertex)v.Clone();
            v.UV.Y += 0.001;
            Assert.NotEqual(v.GetHashCode(), t.GetHashCode());
            t = (Vertex)v.Clone();

            v.Color.X += 0.001;
            Assert.NotEqual(v.GetHashCode(), t.GetHashCode());
            t = (Vertex)v.Clone();
            v.Color.Y += 0.001;
            Assert.NotEqual(v.GetHashCode(), t.GetHashCode());
            t = (Vertex)v.Clone();
            v.Color.Z += 0.001;
            Assert.NotEqual(v.GetHashCode(), t.GetHashCode());
            v.Color.A += 0.001;
            Assert.NotEqual(v.GetHashCode(), t.GetHashCode());
        }


        [Fact]
        public void VertexCloneTest()
        {
            Vertex v0 = new Vertex(1, 2, 3, 4, 5, 6, 7, 8, 9, 0, 11, 22);
            Vertex v1 = (Vertex)v0.Clone();
            Assert.False(v0 == v1);
            Assert.Equal(v0, v1);
            v1.Position.X = 37;
            Assert.NotEqual(v0, v1);
        }

        [Fact]
        public void VertexLerpTest()
        {
            Vertex a = new Vertex(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
            Vertex b = new Vertex(2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14);

            Vertex c = Vertex.Lerp(a, b, 0.7);
            Vertex d = new Vertex(1.7, 2.7, 3.7, 4.7, 5.7, 6.7, 7.7, 8.7, 9.7, 10.7, 11.7, 13.4);
            Assert.Equal(d, c);
        }

        [Fact]
        public void VertexBoundsTest()
        {
            Vertex a = new Vertex(3, 2, 4);
            Assert.Equal(a.Position, a.Bounds().Min);
            Assert.Equal(a.Position, a.Bounds().Max);
        }
    }
}
