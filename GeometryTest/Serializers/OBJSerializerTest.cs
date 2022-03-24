using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using System.IO;
using System.Collections.Generic;
using JPLOPS.Geometry;

namespace GeometryTest
{
    [TestClass]
    public class OBJSerializerTest
    {
        [TestMethod]
        public void OBJSimpleWriteTest()
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

            m.Save("SimpleOBJWriteTest.obj");
            Mesh m2 = Mesh.Load("SimpleOBJWriteTest.obj");
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
        public void OBJBasicReadWriteTest()
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
                        OBJSerializer.Write(m, "OBJBasicReadWriteTest.obj");
                      
                        Mesh m2 = OBJSerializer.Read("OBJBasicReadWriteTest.obj");
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

        [TestMethod]
        public void OBJUnreferencedVertTest()
        {
            // OBJ reader removes unreferenced verts on read
            Mesh m = new Mesh();
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(1, 1, 0));

            m.Vertices.Add(new Vertex(0, 0, 1));
            m.Vertices.Add(new Vertex(1, 0, 0));
            m.Vertices.Add(new Vertex(1, 1, 1));

            m.Faces.Add(new Face(0, 1, 2));
            
            m.Save("OBJUnreferencedVertTest.obj");
            Mesh m2 = Mesh.Load("OBJUnreferencedVertTest.obj");
            Assert.AreEqual(m2.Vertices.Count, 3);
            Assert.AreEqual(m2.Faces.Count, 1);
            Assert.AreEqual(m2.HasColors, false);
            Assert.AreEqual(m2.HasNormals, false);
            Assert.AreEqual(m2.HasUVs, false);

            Assert.AreEqual(CountNumberOfMatchingVertices(new Vertex(0, 0, 0), m2.Vertices), 1);
            Assert.AreEqual(CountNumberOfMatchingVertices(new Vertex(0, 1, 0), m2.Vertices), 1);
            Assert.AreEqual(CountNumberOfMatchingVertices(new Vertex(1, 1, 0), m2.Vertices), 1);
        }

        int CountNumberOfMatchingVertices(Vertex v, List<Vertex> vertices)
        {
            int i = 0;
            foreach(var cur in vertices)
            {
                if(v.Equals(cur))
                {
                    i++;
                }
            }
            return i;
        }

        [TestMethod]
        public void OBJNormalWriteTest()
        {
            Mesh m = new Mesh(hasNormals: true);
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(1, 1, 0));
            m.Vertices[0].Normal = new Vector3(1, 2, 3);
            m.Vertices[1].Normal = new Vector3(4, 5, 6);
            m.Vertices[2].Normal = new Vector3(7, 8, 9);

            // Confrim normals stay when we write a mesh that has normals
            m.Save("OBJNormalWriteTest.obj");
            Mesh m2 = Mesh.Load("OBJNormalWriteTest.obj");
            Assert.AreEqual(m2.HasColors, false);
            Assert.AreEqual(m2.HasNormals, true);
            Assert.AreEqual(m2.HasUVs, false);
            Assert.AreEqual(m2.Vertices[2].Normal, m.Vertices[2].Normal);

            // Set the mesh's normal flag to false and confirm normals aren't serialized
            m.HasNormals = false;
            m.Save("OBJNormallessWriteTest.obj");
            Mesh m3 = Mesh.Load("OBJNormallessWriteTest.obj");
            Assert.AreEqual(m3.HasColors, false);
            Assert.AreEqual(m3.HasNormals, false);
            Assert.AreEqual(m3.HasUVs, false);
            Assert.AreNotEqual(m3.Vertices[2].Normal, m.Vertices[2].Normal);
        }

        [TestMethod]
        public void OBJUVWriteTest()
        {
            Mesh m = new Mesh(hasUVs: true);
            m.Vertices.Add(new Vertex(0, 0, 0));
            m.Vertices.Add(new Vertex(0, 1, 0));
            m.Vertices.Add(new Vertex(1, 1, 0));
            m.Vertices[0].UV = new Vector2(1, 2);
            m.Vertices[1].UV = new Vector2(4, 5);
            m.Vertices[2].UV = new Vector2(7, 8);

            // Confrim uvs stay when we write a mesh that has normals
            m.Save("OBJUVWriteTest.obj");
            Mesh m2 = Mesh.Load("OBJUVWriteTest.obj");
            Assert.AreEqual(m2.HasColors, false);
            Assert.AreEqual(m2.HasNormals, false);
            Assert.AreEqual(m2.HasUVs, true);
            Assert.AreEqual(m2.Vertices[2].UV, m.Vertices[2].UV);

            // Set the mesh's uv flag to false and confirm uvs aren't serialized
            m.HasUVs = false;
            m.Save("OBJUVlessWriteTest.obj");
            Mesh m3 = Mesh.Load("OBJUVlessWriteTest.obj");
            Assert.AreEqual(m3.HasColors, false);
            Assert.AreEqual(m3.HasNormals, false);
            Assert.AreEqual(m3.HasUVs, false);
            Assert.AreNotEqual(m3.Vertices[2].UV, m.Vertices[2].UV);
        }

        [TestMethod]
        public void OBJOneToOneTest()
        {
            string objContent = @"
v 1 0 0
v 2 1 0
v 3 1 0
vt 1 0
vt 2 1
vn 1 0 1
f 1/1/1 2/2/1 3/1/1
f 1/1/1 2/2/1 3/2/1
";
            File.WriteAllText("OBJOneToOneTest.obj", objContent);
            Mesh m = OBJSerializer.Read("OBJOneToOneTest.obj");
            Assert.AreEqual(m.Vertices.Count, 4);
            Vertex v0 = new Vertex(1, 0, 0, 1, 0, 1, 1, 0, 0, 0, 0, 0);
            Vertex v1 = new Vertex(2, 1, 0, 1, 0, 1, 2, 1, 0, 0, 0, 0);
            Vertex v2 = new Vertex(3, 1, 0, 1, 0, 1, 1, 0, 0, 0, 0, 0);
            Vertex v3 = new Vertex(3, 1, 0, 1, 0, 1, 2, 1, 0, 0, 0, 0);
            Assert.AreEqual(m.Vertices[0], v0);
            Assert.AreEqual(m.Vertices[1], v1);
            Assert.AreEqual(m.Vertices[2], v2);
            Assert.AreEqual(m.Vertices[3], v3);

        }

        [TestMethod]
        public void OBJPointCloudTest()
        {
            Mesh m = new Mesh(true, true, true);
            m.Vertices.Add(new Vertex(0, 1, 2, 3, 4, 5, 6, 7, 1, 0, 0, 1));
            m.Vertices.Add(new Vertex(2, 3, 2, 5, 4, 2, 3, 2, 0, 1, 0, 1));
            m.Vertices.Add(new Vertex(1, 4, 2, 6, 5, 1, 4, 3, 0, 0, 1, 1));
            OBJSerializer.Write(m, "OBJPointCloud.obj");
            Mesh m2 = OBJSerializer.Read("OBJPointCloud.obj");
            Assert.AreEqual(m2.HasNormals, m.HasNormals);
            Assert.AreEqual(m2.HasUVs, m.HasUVs);
            Assert.AreEqual(m2.HasColors, m.HasColors);
            Assert.AreEqual(m2.Vertices.Count, m.Vertices.Count);
            for(int i = 0; i < m.Vertices.Count; i++)
            {
                Assert.AreEqual(m2.Vertices[i], m.Vertices[i]);
            }
        }


        [TestMethod]
        public void OBJReadMeshlabExport()
        {
            string content = @"vn 0.000000 0.000000 1.000000
v 0.000000 0.000000 0.000000 1.000000 0.000000 0.000000
vn 0.000000 0.000000 1.000000
v 1.000000 0.000000 0.000000 0.000000 1.000000 0.000000
vn 0.000000 0.000000 1.000000
v 1.000000 1.000000 0.000000 0.000000 0.000000 1.000000
vn 0.000000 0.000000 1.000000
v 0.500000 1.000000 0.000000 0.000000 0.000000 1.000000
vt 0.000000 0.000000
vt 0.500000 0.000000
vt 0.500000 1.000000
vt 0.250000 1.000000
f 1/1/1 2/2/2 3/3/3
f 1/1/1 3/3/3 4/4/4";
            File.WriteAllText("OBJReadMeshlabExport.obj", content);
            Mesh m = OBJSerializer.Read("OBJReadMeshlabExport.obj");

            Assert.AreEqual(4, m.Vertices.Count);
            Assert.AreEqual(2, m.Faces.Count);
            Assert.AreEqual(true, m.HasNormals);
            Assert.AreEqual(true, m.HasUVs);
            Assert.AreEqual(true, m.HasColors);
            Assert.AreEqual(new Face(0, 1, 2), m.Faces[0]);
            Assert.AreEqual(new Face(0, 2, 3), m.Faces[1]);

            List<Vertex> vertices = new List<Vertex>();
            vertices.Add(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1));
            vertices.Add(new Vertex(1, 0, 0, 0, 0, 1, 0.5, 0, 0, 1, 0, 1));
            vertices.Add(new Vertex(1, 1, 0, 0, 0, 1, 0.5, 1, 0, 0, 1, 1));
            vertices.Add(new Vertex(0.5, 1, 0, 0, 0, 1, 0.25, 1, 0, 0, 1, 1));
            foreach(var v in vertices)
            {
                bool match = false;
                foreach(var v2 in m.Vertices)
                {
                    if(v.Equals(v2))
                    {
                        match = true;
                        break;
                    }
                }
                Assert.IsTrue(match);
            }
        }

        [TestMethod]
        public void OBJExceptionTest()
        {
            string unbalancedPointCloud = @"
v 1 0 0
v 2 1 0
v 3 1 0
vt 1 0
vt 2 1
";
            File.WriteAllText("OBJUnbalancedPointCloud.obj", unbalancedPointCloud);
            try
            {
                OBJSerializer.Read("OBJUnbalancedPointCloud.obj");
                Assert.Fail();
            }
            catch (OBJSerializerException) { }

            string unbalancedPointCloud2 = @"
v 1 0 0
v 2 1 0
v 3 1 0
vn 1 0 1
";
            File.WriteAllText("OBJUnbalancedPointCloud2.obj", unbalancedPointCloud2);
            try
            {
                OBJSerializer.Read("OBJUnbalancedPointCloud2.obj");
                Assert.Fail();
            }
            catch (OBJSerializerException) { }

        }
    }
}
