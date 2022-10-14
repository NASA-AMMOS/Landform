using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Geometry;
using Microsoft.Xna.Framework;

namespace GeometryTest
{
    [TestClass]
    public class BOBSerializerText
    {
        [TestMethod]
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
            Assert.AreEqual(m2.Vertices.Count, 6);
            Assert.AreEqual(m2.Faces.Count, 2);
            Assert.AreEqual(m2.HasColors, false);
            Assert.AreEqual(m2.HasNormals, false);
            Assert.AreEqual(m2.HasUVs, false);

            Assert.AreEqual(CountNumberOfMatchingVertices(new Vertex(0, 0, 0), m2.Vertices), 2);
            Assert.AreEqual(CountNumberOfMatchingVertices(new Vertex(0, 1, 0), m2.Vertices), 1);
            Assert.AreEqual(CountNumberOfMatchingVertices(new Vertex(1, 0, 0), m2.Vertices), 1);
            Assert.AreEqual(CountNumberOfMatchingVertices(new Vertex(1, 1, 0), m2.Vertices), 2);
        }

        [TestMethod]
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
                        Assert.AreEqual(m.Vertices.Count, m2.Vertices.Count);
                        for (int i = 0; i < m.Vertices.Count; i++)
                        {
                            Assert.AreEqual(m.Vertices[i], m2.Vertices[i]);

                        }
                        Assert.AreEqual(m.Faces.Count, m2.Faces.Count);
                        for (int i = 0; i < m.Faces.Count; i++)
                        {
                            Assert.AreEqual(m.Faces[i], m2.Faces[i]);
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
