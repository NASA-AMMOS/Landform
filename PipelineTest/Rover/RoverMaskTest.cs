using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Imaging;
using JPLOPS.Pipeline;
using System.IO;

namespace PipelineTest
{
    [TestClass]
    public class RoverMaskTest
    {
        [TestMethod]
        [DeploymentItem("gdal", "gdal")]
        [DeploymentItem("TestData", "TestData")]
        [DeploymentItem("MissionSpecific\\Resources", "MissionSpecific\\Resources")]
        [DeploymentItem("x86", "x86")]
        [DeploymentItem("x64", "x64")]
        public void RoverMaskSanity()
        {
            string filename = Path.Combine("TestData", "img", @"NLB_451557756RASLF0311330NCAM00353M1.IMG");

            var masker = MissionSpecific.GetInstance(Mission.MSL).GetMasker();
            Image mask = masker.Build(Image.Load(filename).Metadata as PDSMetadata);

            // Check pixel in center of "O" in "CURIOSITY" is masked out
            Assert.AreEqual(mask[0, 590, 388], 0.0);
            // Check rock pixel is not
            Assert.AreEqual(mask[0, 287, 819], 1.0);
        }
    }
}
