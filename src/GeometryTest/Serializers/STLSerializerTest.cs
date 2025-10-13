using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Geometry;
using System.IO;

namespace GeometryTest
{
    /// <summary>
    /// Test class for reading and writing stls
    /// </summary>
    [TestClass]
    [DeploymentItem("TestData", "TestData")]
    public class STLSerializerTest
    {
        [TestMethod]
        public void STLBinaryReadTest()
        {           
            Mesh m = Mesh.Load(Path.Combine("TestData", "mesh", "utah_teapot_binary.stl"));
            Assert.AreEqual(12096, m.Vertices.Count);
            Assert.AreEqual(4032, m.Faces.Count);
            Assert.IsTrue(m.HasNormals);
            Assert.IsFalse(m.HasUVs);
            Assert.IsFalse(m.HasColors);
            Assert.IsTrue(m.HasFaces);
            m.Save("utah_teapot_binary_out.stl");
            m = Mesh.Load("utah_teapot_binary_out.stl");
            Assert.AreEqual(12096, m.Vertices.Count);
            Assert.AreEqual(4032, m.Faces.Count);
            Assert.IsTrue(m.HasNormals);
            Assert.IsFalse(m.HasUVs);
            Assert.IsFalse(m.HasColors);
            Assert.IsTrue(m.HasFaces);
            m.ClearNormals();
            m.Clean();
            Assert.AreEqual(2081, m.Vertices.Count);
        }

        [TestMethod]
        public void STLASCIIReadTest()
        {
            Mesh m = Mesh.Load(Path.Combine("TestData", "mesh", "utah_teapot_ascii.stl"));
            Assert.AreEqual(12096, m.Vertices.Count);
            Assert.AreEqual(4032, m.Faces.Count);
            Assert.IsTrue(m.HasNormals);
            Assert.IsFalse(m.HasUVs);
            Assert.IsFalse(m.HasColors);
            Assert.IsTrue(m.HasFaces);
            m.Save("utah_teapot_ascii_out.stl");
            m = Mesh.Load("utah_teapot_ascii_out.stl");
            Assert.AreEqual(12096, m.Vertices.Count);
            Assert.AreEqual(4032, m.Faces.Count);
            Assert.IsTrue(m.HasNormals);
            Assert.IsFalse(m.HasUVs);
            Assert.IsFalse(m.HasColors);
            Assert.IsTrue(m.HasFaces);
            m.ClearNormals();
            m.Clean();
            Assert.AreEqual(2081, m.Vertices.Count);
        }
    }

}
