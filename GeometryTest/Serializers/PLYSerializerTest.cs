using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using System.IO;
using OPS.Geometry;

namespace GeometryTest
{
    /// <summary>
    /// Summary description for PLYSerializerTest
    /// </summary>
    [TestClass]
    public class PLYSerializerTest
    {
        [TestMethod]
        public void PLYBasicReadWriteTest()
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
                        // zero out any feilds this mesh doesn't have
                        for (int i = 0; i < m.Vertices.Count; i++)
                        {
                            m.Vertices[i].Normal = normals ? m.Vertices[i].Normal : Vector3.Zero;
                            m.Vertices[i].UV = uvs ? m.Vertices[i].UV : Vector2.Zero;
                            m.Vertices[i].Color = colors ? m.Vertices[i].Color : Vector4.Zero;
                        }
                        m.Faces.Add(new Face(0, 1, 2));
                        m.Faces.Add(new Face(0, 2, 3));
                        PLYSerializer.Write(m, "PLYMaxCompatibilityFloat.ply", new PLYMaximumCompatibilityWriter(false));
                        PLYSerializer.Write(m, "PLYMaxCompatibilityDouble.ply");
                        PLYSerializer.Write(m, "PLYMaxCompatibilityTexture.ply", "texture.png");
                        PLYSerializer.Write(m, "PLYPrecision.ply", new PLYHighPrecisionWriter(), "texture.png");


                        Mesh m2 = PLYSerializer.Read("PLYMaxCompatibilityFloat.ply");
                        Mesh m3 = PLYSerializer.Read("PLYMaxCompatibilityDouble.ply");
                        Mesh m4 = PLYSerializer.Read("PLYMaxCompatibilityTexture.ply");
                        Mesh m5 = PLYSerializer.Read("PLYPrecision.ply");

                        Assert.AreEqual(m.Vertices.Count, m2.Vertices.Count);
                        Assert.AreEqual(m.Vertices.Count, m3.Vertices.Count);
                        Assert.AreEqual(m.Vertices.Count, m4.Vertices.Count);
                        Assert.AreEqual(m.Vertices.Count, m5.Vertices.Count);
                        for (int i = 0; i < m.Vertices.Count; i++)
                        {
                            Assert.AreEqual(m.Vertices[i], m2.Vertices[i]);
                            Assert.AreEqual(m.Vertices[i], m3.Vertices[i]);
                            Assert.AreEqual(m.Vertices[i], m4.Vertices[i]);
                            Assert.AreEqual(m.Vertices[i], m5.Vertices[i]);
                        }

                        Assert.AreEqual(m.Faces.Count, m2.Faces.Count);
                        Assert.AreEqual(m.Faces.Count, m3.Faces.Count);
                        Assert.AreEqual(m.Faces.Count, m4.Faces.Count);
                        Assert.AreEqual(m.Faces.Count, m5.Faces.Count);
                        for (int i = 0; i < m.Faces.Count; i++)
                        {
                            Assert.AreEqual(m.Faces[i], m2.Faces[i]);
                            Assert.AreEqual(m.Faces[i], m3.Faces[i]);
                            Assert.AreEqual(m.Faces[i], m4.Faces[i]);
                            Assert.AreEqual(m.Faces[i], m5.Faces[i]);
                        }
                    }
                }
            }
        }


        [TestMethod]
        [DeploymentItem("TestData", "TestData")]
        public void PLYReadBlender()
        {
            // Note that blender does not write out alpha as part of its RGB so this will test if the reader correctly sets the default values of 1
            Vector3[] ps = new Vector3[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0.5, 1, 0) };
            Vector3[] ns = new Vector3[] { new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1) };
            Vector2[] uvs = new Vector2[] { new Vector2(0, 0), new Vector2(0.5, 0), new Vector2(0.5, 1), new Vector2(0.25, 1) };
            Vector4[] cs = new Vector4[] { new Vector4(1, 0, 0, 1), new Vector4(0, 1, 0, 1), new Vector4(0, 0, 1, 1), new Vector4(0, 0, 1, 1) };
            Face[] faces = new Face[] { new Face(0, 1, 2), new Face(0, 2, 3) };
            
            Mesh m = PLYSerializer.Read(Path.Combine("TestData", "mesh", "blender_ascii_nct.ply"));
            Assert.AreEqual(true, m.HasNormals);
            Assert.AreEqual(true, m.HasColors);
            Assert.AreEqual(true, m.HasUVs);
            Assert.AreEqual(ps.Length, m.Vertices.Count);
            Assert.AreEqual(faces.Length, m.Faces.Count);

            for (int i = 0; i < ps.Length; i++)
            {
                Assert.AreEqual(ps[i], m.Vertices[i].Position);
                Assert.AreEqual(ns[i], m.Vertices[i].Normal);           
                Assert.AreEqual(uvs[i], m.Vertices[i].UV);              
                Assert.AreEqual(cs[i], m.Vertices[i].Color);
            }
            for (int i = 0; i < faces.Length; i++)
            {
                Assert.AreEqual(faces[i], m.Faces[i]);
            }
        }

        [TestMethod]
        [DeploymentItem("TestData", "TestData")]
        public void PLYReadCloudCompare()
        {
            // Note that cloud compare doesn't export uvs and also has numerical issues with normals
            Vector3[] ps = new Vector3[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0.5, 1, 0) };
            Vector3[] ns = new Vector3[] { new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1) };
            Vector4[] cs = new Vector4[] { new Vector4(1, 0, 0, 1), new Vector4(0, 1, 0, 1), new Vector4(0, 0, 1, 1), new Vector4(0, 0, 1, 1) };
            Face[] faces = new Face[] { new Face(0, 1, 2), new Face(0, 2, 3) };

            foreach (string kind in new string[] { "ascii", "bin" })
            {
                Mesh m = PLYSerializer.Read(Path.Combine("TestData", "mesh", "cloudcompare_"+ kind + "_nc_.ply"));
                Assert.AreEqual(true, m.HasNormals);
                Assert.AreEqual(true, m.HasColors);

                Assert.AreEqual(ps.Length, m.Vertices.Count);
                Assert.AreEqual(faces.Length, m.Faces.Count);

                for (int i = 0; i < ps.Length; i++)
                {
                    Assert.AreEqual(ps[i], m.Vertices[i].Position);
                    Assert.IsTrue(ns[i].AlmostEqual(m.Vertices[i].Normal, 0.001));
                    Assert.AreEqual(cs[i], m.Vertices[i].Color);
                }
                for (int i = 0; i < faces.Length; i++)
                {
                    Assert.AreEqual(faces[i], m.Faces[i]);
                }
            }
        }

        [TestMethod]
        [DeploymentItem("TestData", "TestData")]
        public void PLYReadMeshlab()
        {            
            Vector3[] ps = new Vector3[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0.5, 1, 0) };
            Vector3[] ns = new Vector3[] { new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1) };
            Vector2[] uvs = new Vector2[] { new Vector2(0, 0), new Vector2(0.5, 0), new Vector2(0.5, 1), new Vector2(0.25, 1) };
            Vector4[] cs = new Vector4[] { new Vector4(1, 0, 0, 1), new Vector4(0, 1, 0, 1), new Vector4(0, 0, 1, 1), new Vector4(0, 0, 1, 1) };
            Face[] faces = new Face[] { new Face(0, 1, 2), new Face(0, 2, 3) };

            bool[] onOff = new bool[] { false, true };
            foreach (bool hasN in onOff)
            {
                foreach (bool hasUV in onOff)
                {
                    foreach (bool hasC in onOff)
                    {
                        foreach(string kind in new string[] { "ascii", "bin" })
                        {
                            string endPart = (hasN ? "n" : "_") + (hasC ? "c" : "_") + (hasUV ? "t" : "_");
                            string filename = Path.Combine("TestData","mesh", "meshlab_" + kind + "_" + endPart + ".ply");
                            Mesh m = PLYSerializer.Read(filename);
                            Assert.AreEqual(hasN, m.HasNormals);
                            Assert.AreEqual(hasC, m.HasColors);
                            Assert.AreEqual(hasUV, m.HasUVs);
                            Assert.AreEqual(ps.Length, m.Vertices.Count);
                            Assert.AreEqual(faces.Length, m.Faces.Count);

                            for(int i = 0; i < ps.Length; i++)
                            {
                                Assert.AreEqual(ps[i], m.Vertices[i].Position);
                                if (hasN)
                                {
                                    Assert.AreEqual(ns[i], m.Vertices[i].Normal);
                                }
                                if (hasUV)
                                {
                                    Assert.AreEqual(uvs[i], m.Vertices[i].UV);
                                }
                                if (hasC)
                                {
                                    Assert.AreEqual(cs[i], m.Vertices[i].Color);
                                }
                            }
                            for(int i = 0; i < faces.Length; i++)
                            {
                                Assert.AreEqual(faces[i], m.Faces[i]);
                            }
                        }
                    }
                }
            }
        }        
    }
}
