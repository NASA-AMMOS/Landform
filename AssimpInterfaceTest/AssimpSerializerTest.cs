using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Experimental.AssimpInterface;
using OPS.Geometry;
using Microsoft.Xna.Framework;
using System.IO;

namespace AssimpInterfaceTest
{
    [TestClass]
    [DeploymentItem("Assimp32.dll")]
    [DeploymentItem("Assimp64.dll")]
    public class AssimpSerializerTest
    {
        static string[] TEST_EXTENSIONS = new string[] { ".obj", ".ply" };

        [TestMethod]
        [DeploymentItem(@"..\..\..\TestData", "TestData")]
        public void AssimpImportTest()
        {

            Mesh m = new Mesh(true, true, true);
            m.Vertices.Add(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1));
            m.Vertices.Add(new Vertex(1, 0, 0, 0, 0, 1, 0.5, 0, 0, 1, 0, 1));
            m.Vertices.Add(new Vertex(1, 1, 0, 0, 0, 1, 0.5, 1, 0, 0, 1, 1));
            m.Vertices.Add(new Vertex(0.5, 1, 0, 0, 0, 1, 0.25, 1, 0, 0, 1, 1));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(0, 2, 3));
            string[] files = new string[] { "meshlab_bin_nct.dae", "blender_bin_nct.dae", "blender_bin_nct.fbx" };
            foreach (string f in files)
            {
                string name = Path.Combine("TestData", "mesh", f);
                Mesh m2 = AssimpSerializer.Import(name);
                Assert.AreEqual(true, m.HasNormals);
                Assert.AreEqual(true, m.HasColors);
                Assert.AreEqual(true, m.HasUVs);
                Assert.AreEqual(m.Vertices.Count, m2.Vertices.Count);
                Assert.AreEqual(m.Faces.Count, m2.Faces.Count);
                for (int i = 0; i < m.Vertices.Count; i++)
                {
                    Assert.AreEqual(m.Vertices[i], m2.Vertices[i]);
                }
                for (int i = 0; i < m.Faces.Count; i++)
                {
                    Assert.AreEqual(m.Faces[i], m2.Faces[i]);
                }
            }
        }

        [TestMethod]
        [DeploymentItem(@"..\..\..\TestData", "TestData")]
        public void AssimpExportTest()
        {
            Mesh m = new Mesh(true, true, true);
            m.Vertices.Add(new Vertex(0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 1));
            m.Vertices.Add(new Vertex(1, 0, 0, 0, 0, 1, 0.5, 0, 0, 1, 0, 1));
            m.Vertices.Add(new Vertex(1, 1, 0, 0, 0, 1, 0.5, 1, 0, 0, 1, 1));
            m.Vertices.Add(new Vertex(0.5, 1, 0, 0, 0, 1, 0.25, 1, 0, 0, 1, 1));
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(0, 2, 3));
            AssimpSerializer.Export(m, "assimpExport.dae");
            AssimpSerializer.Export(m, "assimpExportTexture.dae", "texture.png");
            AssimpSerializer.Export(m, "assimpExport.stl");
        }
    }
}
