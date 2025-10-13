using System.Collections.Generic;
using Xunit;
using JPLOPS.Geometry;
using Microsoft.Xna.Framework;

namespace GeometryTest
{
    public class BOBSerializerText
    {
        [Fact]
        public void BOBSimpleWriteTest()
        {
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(1, 1, 0));

            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(1, 1, 0));

            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(5, 4, 3));

            m.Save("SimpleBOBWriteTest.bob");
            Mesh m2 = Mesh.Load("SimpleBOBWriteTest.bob");
            Assert.Equal(6, m2.Vertices.Count);
            Assert.Equal(2, m2.Faces.Count);
            Assert.False(m2.HasColors);
            Assert.False(m2.HasNormals);
            Assert.False(m2.HasUVs);

            Assert.Equal(2, CountNumberOfMatchingVertices(new Vertex(0, 0, 0), m2.Vertices));
            Assert.Equal(1, CountNumberOfMatchingVertices(new Vertex(0, 1, 0), m2.Vertices));
            Assert.Equal(1, CountNumberOfMatchingVertices(new Vertex(1, 0, 0), m2.Vertices));
            Assert.Equal(2, CountNumberOfMatchingVertices(new Vertex(1, 1, 0), m2.Vertices));
        }

        [Fact]
        public void BOBBasicReadWriteTest()
        {
            // Test all combinations of normal, uv, and color
            bool[] onOff = new bool[] { false, true };
            foreach (bool normals in onOff)
            {
                foreach (bool uvs in onOff)
                {
                    foreach (bool colors in onOff)
                    {
                        Mesh m = new Mesh(hasNormals: normals, hasUVs: uvs, hasColors: colors);
                        m.Vertices.Add(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1));
                        m.Vertices.Add(new Vertex(1, 0, 0, 0, 0, 1, 0.5, 0, 0, 1, 0, 1));
                        m.Vertices.Add(new Vertex(1, 1, 0, 0, 0, 1, 0.5, 1, 0, 0, 1, 1));
                        m.Vertices.Add(new Vertex(0.5, 1, 0, 0, 0, 1, 0.25, 1, 0, 0, 1, 1));
                        // zero out any fields this mesh doesn't have
                        for (int i = 0; i < m.Vertices.Count; i++)
                        {
                            m.Vertices[i].Normal = normals ? m.Vertices[i].Normal : Vector3.Zero;
                            m.Vertices[i].UV = uvs ? m.Vertices[i].UV : Vector2.Zero;
                            m.Vertices[i].Color = colors ? m.Vertices[i].Color : Vector4.Zero;
                        }
                        m.Faces.Add(new Face(0, 1, 2));
                        m.Faces.Add(new Face(0, 2, 3));
                        OBJSerializer.Write(m, "BOBBasicReadWriteTest.obj");

                        Mesh m2 = OBJSerializer.Read("BOBBasicReadWriteTest.obj");
                        Assert.Equal(m.Vertices.Count, m2.Vertices.Count);
                        for (int i = 0; i < m.Vertices.Count; i++)
                        {
                            Assert.Equal(m.Vertices[i], m2.Vertices[i]);

                        }
                        Assert.Equal(m.Faces.Count, m2.Faces.Count);
                        for (int i = 0; i < m.Faces.Count; i++)
                        {
                            Assert.Equal(m.Faces[i], m2.Faces[i]);
                        }
                    }
                }
            }
        }

        int CountNumberOfMatchingVertices(Vertex v, List<Vertex> vertices)
        {
            int i = 0;
            foreach (var cur in vertices)
            {
                if (v.Equals(cur))
                {
                    i++;
                }
            }
            return i;
        }
    }
}
