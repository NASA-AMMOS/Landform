using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Geometry;
using System.Collections.Generic;
using OPS.Util;

namespace GeometryThirdpartyTest
{
    [TestClass]
    public class FSSRTest
    {
        [TestInitialize]
        public void testInit()
        {
            // Current version of meshlab has a bug when using filepaths with spaces in the name
            // https://github.com/cnr-isti-vclab/meshlab/issues/164
            TemporaryFile.TemporaryDirectory = AppDomain.CurrentDomain.BaseDirectory.Replace(" ", "_") + "_tmp";
        }

        [TestMethod]
        public void FSSRReconstruct()
        {
            Mesh m = TestMeshCreator.CreateMesh(true, false, false);
            m.Faces = new List<Face>();
            Mesh r = FSSR.Reconstruct(m);
            r.Save("fssr_recon.ply");
            Assert.AreNotEqual(0, r.Vertices.Count);
            Assert.AreNotEqual(0, r.Faces.Count);
            Assert.IsTrue(m.HasNormals);
            Assert.IsFalse(m.HasUVs);
            Assert.IsFalse(m.HasColors);
        }

        [TestMethod]
        public void FSSRResampleDeimation()
        {
            Mesh m = TestMeshCreator.CreateMesh(true, false, false);
            Mesh r = FSSR.ResampleDeimation(m, 10000, 2000);
            r.Save("fssr_resample_recon.ply");
            Assert.AreNotEqual(0, r.Vertices.Count);
            Assert.AreNotEqual(0, r.Faces.Count);
            Assert.IsTrue(m.HasNormals);
            Assert.IsFalse(m.HasUVs);
            Assert.IsFalse(m.HasColors);
        }
    }
}
