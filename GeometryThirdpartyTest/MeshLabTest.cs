
#if ENABLE_MESHLAB
namespace GeometryThirdpartyTest
{
    [TestClass]
    [DeploymentItem("TestData", "TestData")]
    public class MeshLabTest
    {
        [TestInitialize]
        public void testInit()
        {
            // Current version of meshlab has a bug when using filepaths with spaces in the name
            // https://github.com/cnr-isti-vclab/meshlab/issues/164
            TemporaryFile.TemporaryDirectory = AppDomain.CurrentDomain.BaseDirectory.Replace(" ", "_") + "_tmp";
        }

        [TestMethod]
        public void ComputeNormalsTest()
        {
            {
                Mesh m = TestMeshCreator.CreateMesh(true, true, true);
                Mesh r = MeshLab.ComputeNormals(m);
                Assert.IsTrue(r.HasNormals);
                Assert.IsTrue(r.HasUVs);
                Assert.IsTrue(r.HasColors);
                Assert.AreEqual(m.Vertices.Count, r.Vertices.Count);
                Assert.AreEqual(m.Faces.Count, r.Faces.Count);
                Assert.IsTrue(m.Vertices.Any(v => v.Position != new Microsoft.Xna.Framework.Vector3(0,1,0)));
                r.Save("meshlab_normals.ply");
            }
            {
                Mesh m = TestMeshCreator.CreateMesh(false, true, true);
                Mesh r = MeshLab.ComputeNormals(m);
                Assert.IsTrue(r.HasNormals);
                Assert.IsTrue(r.HasUVs);
                Assert.IsTrue(r.HasColors);
                Assert.AreEqual(m.Vertices.Count, r.Vertices.Count);
                Assert.AreEqual(m.Faces.Count, r.Faces.Count);
            }
            {
                Mesh m = TestMeshCreator.CreateMesh(false, false, false);
                Mesh r = MeshLab.ComputeNormals(m);
                Assert.IsTrue(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.AreEqual(m.Vertices.Count, r.Vertices.Count);
                Assert.AreEqual(m.Faces.Count, r.Faces.Count);
            }
            {
                Mesh m = TestMeshCreator.CreateMesh(false, false, false);
                m.Faces = new List<Face>();
                Mesh r = MeshLab.ComputeNormals(m);
                Assert.IsTrue(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.AreEqual(m.Vertices.Count, r.Vertices.Count);
                Assert.AreEqual(0, r.Faces.Count);
            }
            {
                Mesh m = TestMeshCreator.CreateMesh(false, false, true);
                m.Faces.Clear();
                Mesh r = MeshLab.ComputeNormals(m);
                Assert.IsTrue(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsTrue(r.HasColors);
                Assert.AreEqual(m.Vertices.Count, r.Vertices.Count);
                Assert.AreEqual(0, r.Faces.Count);
            }
        }

        [TestMethod]
        public void SampleTest()
        {
            {
                Mesh m = TestMeshCreator.CreateMesh(true, true, true);
                Mesh r = MeshLab.Sample(m, 1000);
                Assert.IsTrue(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.IsTrue(r.Vertices.Count > 1000);
                Assert.AreEqual(0, r.Faces.Count);
                r.Save("meshlab_sample.ply");
            }
            {
                Mesh m = TestMeshCreator.CreateMesh(false, false, false);
                Mesh r = MeshLab.Sample(m, 4000);
                Assert.IsFalse(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.IsTrue(r.Vertices.Count > 2000);
                Assert.AreEqual(0, r.Faces.Count);
            }
        }

        [TestMethod]
        public void DecimateTest()
        {
            {
                Mesh m = TestMeshCreator.CreateMesh(true, true, true);
                Mesh r = MeshLab.Decimated(m, 2000);
                Assert.IsTrue(m.Bounds().FuzzyContains(r.Bounds(),0.1));
                Assert.IsTrue(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.IsTrue(r.Vertices.Count > 0);
                Assert.IsTrue(r.Faces.Count > 0);
                Assert.IsTrue(r.Faces.Count <= 2000);
                r.Save("meshlab_decimate.ply");
            }
            {
                Mesh m = TestMeshCreator.CreateMesh(false, false, false);
                Mesh r = MeshLab.Decimated(m, 1000);
                Assert.IsTrue(m.Bounds().FuzzyContains(r.Bounds(),0.1));
                Assert.IsFalse(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.IsTrue(r.Vertices.Count > 0);
                Assert.IsTrue(r.Faces.Count > 0);
                Assert.IsTrue(r.Faces.Count <= 1000);
            }
        }

        [TestMethod]
        public void ResampleDecimateTest()
        {
            {
                Mesh m = TestMeshCreator.CreateMesh(true, true, true);
                Mesh r = MeshLab.ResampleDecimated(m, 2000, 2000);
                Assert.IsTrue(m.Bounds().FuzzyContains(r.Bounds(), 0.01));
                Assert.IsTrue(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.IsTrue(r.Vertices.Count > 0);
                Assert.IsTrue(r.Faces.Count > 0);
                Assert.IsTrue(r.Faces.Count <= 2000);
                r.Save("meshlab_resample_decimate.ply");
            }
            {
                Mesh m = TestMeshCreator.CreateMesh(false, false, false);
                Mesh r = MeshLab.ResampleDecimated(m, 1000, 1000);
                Assert.IsTrue(m.Bounds().FuzzyContains(r.Bounds(), 0.01));
                Assert.IsFalse(r.HasNormals);
                Assert.IsFalse(r.HasUVs);
                Assert.IsFalse(r.HasColors);
                Assert.IsTrue(r.Vertices.Count > 0);
                Assert.IsTrue(r.Faces.Count > 0);
                Assert.IsTrue(r.Faces.Count <= 1000);
            }
        }

        [TestMethod]
        public void BidirectionalHausdorffDistanceTest()
        {
            {
                Mesh m = TestMeshCreator.CreateMesh(true, false, false);
                Mesh r = MeshLab.ResampleDecimated(m, 2000, 2000);
                HausdorffDistanceStats distA = MeshLab.BidirectionalHausdorffDistance(m, r);
                HausdorffDistanceStats distB = MeshLab.BidirectionalHausdorffDistance(r, m);
                Assert.AreEqual(distA.Max, distB.Max);
                Assert.AreEqual(distA.Mean, distB.Mean);
                Assert.AreEqual(distA.Min, distB.Min);
                Assert.AreEqual(distA.RMS, distB.RMS);
                Assert.AreNotEqual(0, distA.Mean);
                Assert.AreNotEqual(0, distA.Max);
                Assert.AreNotEqual(0, distA.RMS);

                // Test on point clouds
                r.Faces = m.Faces = new List<Face>();
                distA = MeshLab.BidirectionalHausdorffDistance(m, r);
                distB = MeshLab.BidirectionalHausdorffDistance(r, m);
                Assert.AreEqual(distA.Max, distB.Max);
                Assert.AreEqual(distA.Mean, distB.Mean);
                Assert.AreEqual(distA.Min, distB.Min);
                Assert.AreEqual(distA.RMS, distB.RMS);
                Assert.AreNotEqual(0, distA.Mean);
                Assert.AreNotEqual(0, distA.Max);
                Assert.AreNotEqual(0, distA.RMS);
            }
        }
    }
}
#endif
