using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Pipeline;

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
            Assert.AreEqual("0000100003", sd.ToString());
            sd = new SiteDrive("0000100003");
            Assert.AreEqual(1, sd.Site);
            Assert.AreEqual(3, sd.Drive);
            sd = new SiteDrive(12345, 67890);
            Assert.AreEqual(12345, sd.Site);
            Assert.AreEqual(67890, sd.Drive);
            Assert.AreEqual("1234567890", sd.ToString());
            sd = new SiteDrive("1234567890");
            Assert.AreEqual(12345, sd.Site);
            Assert.AreEqual(67890, sd.Drive);
        }
    }
}
