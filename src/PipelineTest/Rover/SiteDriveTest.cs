using JPLOPS.Pipeline;

namespace PipelineTest
{
    /// <summary>
    /// Summary description for SiteDriveTest
    /// </summary>
    public class SiteDriveTest
    {

        [Fact]
        public void SiteDriveConstructorTest()
        {
            SiteDrive sd = new SiteDrive(1,3);
            Assert.Equal(1, sd.Site);
            Assert.Equal(3, sd.Drive);
            Assert.Equal("0010003", sd.ToString());
            sd = new SiteDrive("0010003");
            Assert.Equal(1, sd.Site);
            Assert.Equal(3, sd.Drive);
            sd = new SiteDrive(123, 6789);
            Assert.Equal(123, sd.Site);
            Assert.Equal(6789, sd.Drive);
            Assert.Equal("1236789", sd.ToString());
            sd = new SiteDrive("1236789");
            Assert.Equal(123, sd.Site);
            Assert.Equal(6789, sd.Drive);
        }
    }
}
