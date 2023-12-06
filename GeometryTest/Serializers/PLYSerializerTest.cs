using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using System.IO;
using JPLOPS.Geometry;

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
                        string msg = $" normals={normals}, uvs={uvs}, colors={colors}";

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

                        var testFiles = new List<string>();

                        string fileName = null;
                        try
                        {
                            fileName = "test.ply";
                            PLYSerializer.Write(m, fileName);
                            testFiles.Add(fileName);

                            fileName = "testTexture.ply";
                            PLYSerializer.Write(m, fileName, "texture.png");
                            testFiles.Add(fileName);

                            fileName = "testPrecision.ply";
                            PLYSerializer.Write(m, fileName, new PLYHighPrecisionWriter());
                            testFiles.Add(fileName);
                           
                            fileName = "testCompact.ply";
                            PLYSerializer.Write(m, fileName, new PLYCompactFileWriter());
                            testFiles.Add(fileName);

                            fileName = "testNormalLengthsAsValue.ply";
                            PLYSerializer.Write(m, fileName,
                                                new PLYMaximumCompatibilityWriter(writeNormalLengthsAsValue: true));
                            testFiles.Add(fileName);
                        }
                        catch (Exception ex)
                        {
                            throw new Exception("error writing " + fileName + msg + ": " + ex.Message, ex);
                        }

                        if (colors)
                        {
                            PLYSerializer.Write(m, "testWithoutAlpha.ply",
                                                new PLYMaximumCompatibilityWriter(writeAlpha: false));
                            testFiles.Add("testWithoutAlpha.ply");
                        }

                        foreach (string testFile in testFiles)
                        {
                            Mesh rm = null;
                            try
                            {
                                rm = PLYSerializer.Read(testFile);
                            }
                            catch (Exception ex)
                            {
                                throw new Exception("error reading " + testFile + msg + ": " + ex.Message, ex);
                            }
                            Assert.AreEqual(m.Vertices.Count, rm.Vertices.Count, testFile + msg + " vertex count");
                            for (int i = 0; i < m.Vertices.Count; i++)
                            {
                                Assert.AreEqual(m.Vertices[i], rm.Vertices[i], testFile + msg + " vertex " + i);
                            }
                            Assert.AreEqual(m.Faces.Count, rm.Faces.Count, testFile + msg + " face count");
                            for (int i = 0; i < m.Faces.Count; i++)
                            {
                                Assert.AreEqual(m.Faces[i], rm.Faces[i], testFile + msg + " face " + i);
                            }
                        }
                    }
                }
            }
        }
    }
}
