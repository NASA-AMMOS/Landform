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

            var meshes = (new IVSerializer()).LoadAllLODs(filepath);
            Assert.AreEqual(3, meshes.Count);
            Assert.AreEqual(2980, meshes[0].Vertices.Count);
            Assert.AreEqual(5289, meshes[0].Faces.Count);
            Assert.IsTrue(meshes[0].HasUVs);
            Assert.IsFalse(meshes[0].HasColors);
            Assert.IsTrue(meshes[0].HasNormals);

            Assert.AreEqual(959, meshes[1].Vertices.Count);
            Assert.AreEqual(1497, meshes[1].Faces.Count);
            Assert.IsTrue(meshes[1].HasUVs);
            Assert.IsFalse(meshes[1].HasColors);
            Assert.IsTrue(meshes[1].HasNormals);

            Assert.AreEqual(318, meshes[2].Vertices.Count);
            Assert.AreEqual(392, meshes[2].Faces.Count);
            Assert.IsTrue(meshes[2].HasUVs);
            Assert.IsFalse(meshes[2].HasColors);
            Assert.IsTrue(meshes[2].HasNormals);

            var m = (new IVSerializer()).Load(filepath);
            Assert.AreEqual(2980, m.Vertices.Count);
            Assert.AreEqual(5289, m.Faces.Count);
            Assert.IsTrue(m.HasUVs);
            Assert.IsFalse(m.HasColors);
            Assert.IsTrue(m.HasNormals);

            m.Save("test.obj");
        }
    }
}
