using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Pipeline;

namespace PipelineTest
{
    /// <summary>
    /// Summary description for SiteDriveTest
    /// </summary>
    [TestClass]
    public class SiteDriveTest
    {

        [TestMethod]
        public void SiteDriveConstructorTest()
        {
            SiteDrive sd = new SiteDrive(1,3);
            Assert.AreEqual(1, sd.Site);
            Assert.AreEqual(3, sd.Drive);
            Assert.AreEqual("0010003", sd.ToString());
            sd = new SiteDrive("0010003");
            Assert.AreEqual(1, sd.Site);
            Assert.AreEqual(3, sd.Drive);
            sd = new SiteDrive(123, 6789);
            Assert.AreEqual(123, sd.Site);
            Assert.AreEqual(6789, sd.Drive);
            Assert.AreEqual("1236789", sd.ToString());
            sd = new SiteDrive("1236789");
            Assert.AreEqual(123, sd.Site);
            Assert.AreEqual(6789, sd.Drive);
        }
    }
}
