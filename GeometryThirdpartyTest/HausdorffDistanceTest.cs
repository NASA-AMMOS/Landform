
#if ENABLE_MESHLAB
namespace GeometryThirdpartyTest
{
    /// <summary>
    /// Summary description for UnitTest1
    /// </summary>
    [TestClass]
    [DeploymentItem("TestData", "TestData")]
    [DeploymentItem("ExternalApps", "ExternalApps")]
    [DeploymentItem("x86", "x86")]
    [DeploymentItem("x64", "x64")]
    public class HausdorffDistanceTest
    {

        [TestInitialize]
        public void testInit()
        {
            // Current version of meshlab has a bug when using filepaths with spaces in the name
            // https://github.com/cnr-isti-vclab/meshlab/issues/164
            TemporaryFile.TemporaryDirectory = AppDomain.CurrentDomain.BaseDirectory.Replace(" ", "_") + "_tmp";
        }

        [TestMethod]
        public void HausdorffDistanceParse()
        {
            string data = "Loading Plugins:\r\nTotal 241 filtering actions\r\nTotal 12 io plugins\r\nMesh E:/Repos/Pipeline/TestResults/Deploy_menzies_2017-06-27_11_54_27/Out_tmp/2ce3d7cd-1a89-46ff-bfef-3c697942d7e0.tmp.ply loaded has 2500 vn 4802 fn\r\nMesh E:/Repos/Pipeline/TestResults/Deploy_menzies_2017-06-27_11_54_27/Out_tmp/a07b21f9-8fcd-44d7-a0b2-286b689c6cc0.tmp.ply loaded has 1293 vn 1942 fn\r\nApply FilterScript: 'E:/Repos/Pipeline/TestResults/Deploy_menzies_2017-06-27_11_54_27/Out_tmp/c7126012-9338-4172-b74f-81cf541f3b8a.tmp.mlx'\r\nStarting Script of 1 actionsfilter: Hausdorff Distance\r\nno additional memory available!!! memory required: 117624\r\nno additional memory available!!! memory required: 54336\r\nHausdorff Distance computed\r\n     Sampled 501293 pts (rng: 0) on a07b21f9-8fcd-44d7-a0b2-286b689c6cc0.tmp.ply searched closest on 2ce3d7cd-1a89-46ff-bfef-3c697942d7e0.tmp.ply\r\n     min : 0.000000   max 0.423964   mean : 0.063893   RMS : 0.083235\r\nValues w.r.t. BBox Diag (69.398132)\r\n     min : 0.000000   max 0.006109   mean: 0.000921   RMS: 0.001199\r\n\r\n";
            HausdorffDistanceStats hd = HausdorffDistanceStats.ParseFromMeshLabOutput(data);
            Assert.AreEqual(0, hd.Min);
            Assert.AreEqual(0.423964, hd.Max);
            Assert.AreEqual(0.063893, hd.Mean);
            Assert.AreEqual(0.083235, hd.RMS);
        }
        
        [TestMethod]
        public void HausdorffDistanceComareTest()
        {
            Mesh m1 = Mesh.Load(Path.Combine("TestData", "mesh", "raptor.obj"));
            m1.HasNormals = false;
            m1.HasUVs = false;
            m1.HasColors = false;
            Mesh m2 = Mesh.Load(Path.Combine("TestData", "mesh", "raptor_decimated.obj"));
            HausdorffDistanceStats hd = MeshLab.BidirectionalHausdorffDistance(m1, m2);
            double dist = HausdorffDistance.Calculate(m1, m2, 0.001);
            AssertE.AreSimilar(hd.Max, dist, 0.001);
        }
    }
}
#endif
