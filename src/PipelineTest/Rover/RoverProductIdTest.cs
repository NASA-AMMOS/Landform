using JPLOPS.Pipeline;

namespace PipelineTest
{
    public class RoverProductIdTest
    {
        [Fact]
        public void TestParseRoverId()
        {
            var id = RoverProductId.Parse("NLB_436643735RASLF0220000NCAM00323M_.IMG");
            Assert.NotNull(id);
            Assert.Equal(RoverProductCamera.NavcamLeft, id.Camera);
            Assert.Equal(RoverProductGeometry.Linearized, id.Geometry);
            Assert.Equal(RoverProductProducer.OPGS, id.Producer);
            Assert.Equal(36, id.Version);
            Assert.Equal(RoverProductType.Image, id.ProductType);
            id = RoverProductId.Parse("0608ML0025660260301542E01_DRCX.IMG");
            Assert.NotNull(id);
            Assert.Equal(RoverProductCamera.MastcamLeft, id.Camera);
            Assert.Equal(RoverProductGeometry.Raw, id.Geometry);
            Assert.Equal(RoverProductProducer.MSSS, id.Producer);
            Assert.Equal(1, id.Version);
            Assert.Equal(RoverProductType.Image, id.ProductType);
            var msssId = (MSLMSSSProductId)id;
            Assert.True(msssId.Decompressed);
            Assert.True(msssId.RadiometricallyCalibrated);
            Assert.True(msssId.ColorCorrected);
            Assert.Equal(RoverProductGeometry.Raw, msssId.Geometry);
        }
    }
}
