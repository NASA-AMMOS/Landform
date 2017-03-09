using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;
using System.IO;
using OPS.Test;

namespace ImagingTest
{
    [TestClass]
    [DeploymentItem("gdal", "gdal")]
    [DeploymentItem("TestData", "TestData")]
    public class PDSSerializerTest
    {
        [TestMethod]
        public void PDSTestRead()
        {
            Image img = new PDSSeralizer().Read(Path.Combine("TestData", "img", "FLB_509619692RAS_T0530000FHAZ00323M_.IMG"), ImageConverters.PDSBitMaskValueRangeToNormalizedImage);
            Assert.IsFalse(img.HasMask);
            Assert.AreEqual(1, img.Data.Length);
            Assert.AreEqual(img.Width*img.Height, img.Data[0].Length);

            Image imgMasked = new PDSSeralizer().Read(Path.Combine("TestData", "img", "FLB_509619692RAS_T0530000FHAZ00323M_.IMG"), ImageConverters.PDSBitMaskValueRangeToNormalizedImage, new float[] { 0 });
            Assert.IsTrue(imgMasked.HasMask);
            Assert.IsTrue(imgMasked.IsMasked(4, 6));
            Assert.IsFalse(imgMasked.IsMasked(5, 6));
            img.Save<byte>("helloWorld.tif");

            Image mastcam = new PDSSeralizer().Read(Path.Combine("TestData", "img", "ML0_451292526RCX_S0311094MCAM02555M1.IMG"), ImageConverters.PDSBitMaskValueRangeToNormalizedImage, new float[] { 0,0,0 });
            Assert.IsTrue(mastcam.HasMask);
            Assert.AreEqual(3, mastcam.Data.Length);
            Assert.AreEqual(1408*1200, mastcam.Data[0].Length);            

            Image range = new PDSSeralizer().Read(Path.Combine("TestData", "img", "NLB_451649560RNGLF0311330NCAM12813M1.IMG"), ImageConverters.PassThrough, new float[] { 0 });
            Assert.IsTrue(range.HasMask);
            double total = 0;
            foreach(double d in range)
            {
                total += d;
            }
            AssertE.AreSimilar(5184855.197033, total, 0.0001);
            Image arm = new PDSSeralizer().Read(Path.Combine("TestData", "img", "NLB_451025090ARMLF0311052NCAM00493M1.IMG"), ImageConverters.PassThrough);
            Assert.AreEqual(5, arm.Bands);
        }
    }
}
