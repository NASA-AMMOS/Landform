using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Pipeline;

namespace PipelineTest
{
    [TestClass]
    public class RoverProductIdTest
    {
        [TestMethod]
        public void TestParseRoverId()
        {
            var id = RoverProductId.Parse("NLB_436643735RASLF0220000NCAM00323M_.IMG");
            Assert.IsTrue(id != null);
            Assert.AreEqual(RoverProductCamera.NavcamLeft, id.Camera);
            Assert.AreEqual(RoverProductGeometry.Linearized, id.Geometry);
            Assert.AreEqual(RoverProductProducer.OPGS, id.Producer);
            Assert.AreEqual(36, id.Version);
            Assert.AreEqual(RoverProductType.Image, id.ProductType);
            id = RoverProductId.Parse("0608ML0025660260301542E01_DRCX.IMG");
            Assert.IsTrue(id != null);
            Assert.AreEqual(RoverProductCamera.MastcamLeft, id.Camera);
            Assert.AreEqual(RoverProductGeometry.Raw, id.Geometry);
            Assert.AreEqual(RoverProductProducer.MSSS, id.Producer);
            Assert.AreEqual(1, id.Version);
            Assert.AreEqual(RoverProductType.Image, id.ProductType);
            var msssId = (MSLMSSSProductId)id;
            Assert.AreEqual(true, msssId.Decompressed);
            Assert.AreEqual(true, msssId.RadiometricallyCalibrated);
            Assert.AreEqual(true, msssId.ColorCorrected);
            Assert.AreEqual(RoverProductGeometry.Raw, msssId.Geometry);
        }
    }
}
