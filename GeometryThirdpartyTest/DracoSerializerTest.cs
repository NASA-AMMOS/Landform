using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GeometryThirdpartyTest
{
    [TestClass]
    [DeploymentItem("TestData", "TestData")]
    [DeploymentItem("ExternalApps", "ExternalApps")]
    public class DracoSerializerTest
    {
        static DracoSerializerTest()
        {
            new DracoSerializer().Register();
        }

        [TestMethod]
        public void DracoSimpleWriteTest()
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

            m.Save("SimpleDRCWriteTest.drc");
            Mesh m2 = Mesh.Load("SimpleDRCWriteTest.drc");
            Assert.AreEqual(m2.Vertices.Count, 4);
            Assert.AreEqual(m2.Faces.Count, 2);
            Assert.AreEqual(m2.HasColors, false);
            Assert.AreEqual(m2.HasNormals, false);
            Assert.AreEqual(m2.HasUVs, false);

            Assert.AreEqual(CountNumberOfMatchingVertices(new Vertex(0, 0, 0), m2.Vertices), 1);
            Assert.AreEqual(CountNumberOfMatchingVertices(new Vertex(0, 1, 0), m2.Vertices), 1);
            Assert.AreEqual(CountNumberOfMatchingVertices(new Vertex(1, 0, 0), m2.Vertices), 1);
            Assert.AreEqual(CountNumberOfMatchingVertices(new Vertex(1, 1, 0), m2.Vertices), 1);
        }

        [TestMethod]
        public void DracoBasicReadWriteTest()
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
                        OBJSerializer.Write(m, "DRCBasicReadWriteTest.drc");

                        Mesh m2 = OBJSerializer.Read("DRCBasicReadWriteTest.drc");
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
