using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImagingTest.Serializers
{
    [TestClass]
    [DeploymentItem("gdal", "gdal")]
    [DeploymentItem("TestData", "TestData")]
    public class FITSSerializerTest
    {
        [TestMethod]
        public void FITSSerializerReadTest()
        {
            Image img = Image.Load(Path.Combine("TestData", "img", "horsehead.fit"));
            img.Save<ushort>("horsehead.tif");
            Assert.AreEqual(1, img.Bands);
            Assert.AreEqual(891, img.Width);
            Assert.AreEqual(893, img.Height);
            Assert.AreEqual(1, img.Metadata.Bands);
            Assert.AreEqual(891, img.Metadata.Width);
            Assert.AreEqual(893, img.Metadata.Height);
            Assert.AreEqual(65, ((RawMetadata)img.Metadata).ReadAsDouble("EXPOSURE"));
        }
    }
}
