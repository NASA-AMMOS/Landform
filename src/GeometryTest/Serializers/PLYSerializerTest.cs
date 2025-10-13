using System;
using System.Collections.Generic;
using Xunit;
using Microsoft.Xna.Framework;
using System.IO;
using JPLOPS.Geometry;

namespace GeometryTest
{
    /// <summary>
    /// Summary description for PLYSerializerTest
    /// </summary>
    public class PLYSerializerTest
    {
        [Fact]
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

                        string? fileName = null;
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
                            Mesh? rm = null;
                            try
                            {
                                rm = PLYSerializer.Read(testFile);
                            }
                            catch (Exception ex)
                            {
                                throw new Exception("error reading " + testFile + msg + ": " + ex.Message, ex);
                            }
                            Assert.Equal(m.Vertices.Count, rm.Vertices.Count);
                            for (int i = 0; i < m.Vertices.Count; i++)
                            {
                                Assert.Equal(m.Vertices[i], rm.Vertices[i]);
                            }
                            Assert.Equal(m.Faces.Count, rm.Faces.Count);
                            for (int i = 0; i < m.Faces.Count; i++)
                            {
                                Assert.Equal(m.Faces[i], rm.Faces[i]);
                            }
                        }
                    }
                }
            }
        }

        [Fact]
        public void PLYReadBlender()
        {
            // Note that blender does not write out alpha as part of its RGB so this will test if the reader correctly
            // sets the default values of 1
            Vector3[] ps = new Vector3[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0),
                                           new Vector3(0.5, 1, 0) };
            Vector3[] ns = new Vector3[] { new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1),
                                           new Vector3(0, 0, 1) };
            Vector2[] uvs = new Vector2[] { new Vector2(0, 0), new Vector2(0.5, 0), new Vector2(0.5, 1),
                                            new Vector2(0.25, 1) };
            Vector4[] cs = new Vector4[] { new Vector4(1, 0, 0, 1), new Vector4(0, 1, 0, 1), new Vector4(0, 0, 1, 1),
                                           new Vector4(0, 0, 1, 1) };
            Face[] faces = new Face[] { new Face(0, 1, 2), new Face(0, 2, 3) };

            Mesh m = PLYSerializer.Read("blender_ascii_nct.ply");
            Assert.True(m.HasNormals);
            Assert.True(m.HasColors);
            Assert.True(m.HasUVs);
            Assert.Equal(ps.Length, m.Vertices.Count);
            Assert.Equal(faces.Length, m.Faces.Count);

            for (int i = 0; i < ps.Length; i++)
            {
                Assert.Equal(ps[i], m.Vertices[i].Position);
                Assert.Equal(ns[i], m.Vertices[i].Normal);
                Assert.Equal(uvs[i], m.Vertices[i].UV);
                Assert.Equal(cs[i], m.Vertices[i].Color);
            }
            for (int i = 0; i < faces.Length; i++)
            {
                Assert.Equal(faces[i], m.Faces[i]);
            }
        }

        [Fact]
        public void PLYReadCloudCompare()
        {
            // Note that cloud compare doesn't export uvs and also has numerical issues with normals
            Vector3[] ps = new Vector3[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0),
                                           new Vector3(0.5, 1, 0) };
            Vector3[] ns = new Vector3[] { new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1),
                                           new Vector3(0, 0, 1) };
            Vector4[] cs = new Vector4[] { new Vector4(1, 0, 0, 1), new Vector4(0, 1, 0, 1), new Vector4(0, 0, 1, 1),
                                           new Vector4(0, 0, 1, 1) };
            Face[] faces = new Face[] { new Face(0, 1, 2), new Face(0, 2, 3) };

            foreach (string kind in new string[] { "ascii", "bin" })
            {
                Mesh m = PLYSerializer.Read("cloudcompare_"+ kind + "_nc_.ply");
                Assert.True(m.HasNormals);
                Assert.True(m.HasColors);

                Assert.Equal(ps.Length, m.Vertices.Count);
                Assert.Equal(faces.Length, m.Faces.Count);

                for (int i = 0; i < ps.Length; i++)
                {
                    Assert.Equal(ps[i], m.Vertices[i].Position);
                    Assert.True(ns[i].AlmostEqual(m.Vertices[i].Normal, 0.001));
                    Assert.Equal(cs[i], m.Vertices[i].Color);
                }
                for (int i = 0; i < faces.Length; i++)
                {
                    Assert.Equal(faces[i], m.Faces[i]);
                }
            }
        }

        [Fact]
        public void PLYReadMeshlab()
        {            
            Vector3[] ps = new Vector3[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0),
                                           new Vector3(0.5, 1, 0) };
            Vector3[] ns = new Vector3[] { new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1),
                                           new Vector3(0, 0, 1) };
            Vector2[] uvs = new Vector2[] { new Vector2(0, 0), new Vector2(0.5, 0), new Vector2(0.5, 1),
                                            new Vector2(0.25, 1) };
            Vector4[] cs = new Vector4[] { new Vector4(1, 0, 0, 1), new Vector4(0, 1, 0, 1), new Vector4(0, 0, 1, 1),
                                           new Vector4(0, 0, 1, 1) };
            Face[] faces = new Face[] { new Face(0, 1, 2), new Face(0, 2, 3) };

            bool[] onOff = new bool[] { false, true };
            foreach (bool hasN in onOff)
            {
                foreach (bool hasUV in onOff)
                {
                    foreach (bool hasC in onOff)
                    {
                        foreach (string kind in new string[] { "ascii", "bin" })
                        {
                            string endPart = (hasN ? "n" : "_") + (hasC ? "c" : "_") + (hasUV ? "t" : "_");
                            string filename = "meshlab_" + kind + "_" + endPart + ".ply";
                            Mesh m = PLYSerializer.Read(filename);
                            Assert.Equal(hasN, m.HasNormals);
                            Assert.Equal(hasC, m.HasColors);
                            Assert.Equal(hasUV, m.HasUVs);
                            Assert.Equal(ps.Length, m.Vertices.Count);
                            Assert.Equal(faces.Length, m.Faces.Count);

                            for (int i = 0; i < ps.Length; i++)
                            {
                                Assert.Equal(ps[i], m.Vertices[i].Position);
                                if (hasN)
                                {
                                    Assert.Equal(ns[i], m.Vertices[i].Normal);
                                }
                                if (hasUV)
                                {
                                    Assert.Equal(uvs[i], m.Vertices[i].UV);
                                }
                                if (hasC)
                                {
                                    Assert.Equal(cs[i], m.Vertices[i].Color);
                                }
                            }
                            for (int i = 0; i < faces.Length; i++)
                            {
                                Assert.Equal(faces[i], m.Faces[i]);
                            }
                        }
                    }
                }
            }
        }        
    }
}
