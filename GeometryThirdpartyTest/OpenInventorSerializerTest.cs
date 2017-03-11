using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Geometry;
using System.IO;

namespace GeometryThirdpartyTest
{
    [TestClass]
    [DeploymentItem("TestData", "TestData")]
    [DeploymentItem("ExternalApps", "ExternalApps")]
    public class OpenInventorSerializerTest
    {
        [TestMethod]
        public void OpenInventorReadTest()
        {
            string filepath = Path.Combine("TestData", "mesh", "MLF_474203921RASLS0450000MCAM03804M1.iv");
            var meshes = OpenInventorSerializer.ReadAllLODs(filepath);
            Assert.AreEqual(2, meshes.Count);
            Assert.AreEqual(20358, meshes[0].Vertices.Count);
            Assert.AreEqual(6786, meshes[0].Faces.Count);
            Assert.IsTrue(meshes[0].HasUVs);
            Assert.IsFalse(meshes[0].HasColors);
            Assert.IsFalse(meshes[0].HasNormals);
            Assert.AreEqual(1176, meshes[1].Vertices.Count);
            Assert.AreEqual(392, meshes[1].Faces.Count);
            Assert.IsTrue(meshes[1].HasUVs);
            Assert.IsFalse(meshes[1].HasColors);
            Assert.IsFalse(meshes[1].HasNormals);

            var m = OpenInventorSerializer.ReadBestLOD(filepath);
            Assert.AreEqual(20358, m.Vertices.Count);
            Assert.AreEqual(6786, m.Faces.Count);
            Assert.IsTrue(m.HasUVs);
            Assert.IsFalse(m.HasColors);
            Assert.IsFalse(m.HasNormals);
            m.Save("test.obj");
        }
    }
}
