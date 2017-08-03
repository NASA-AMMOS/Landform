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
            Assert.AreEqual(img.Width * img.Height, img.Data[0].Length);

            Image imgMasked = new PDSSeralizer().Read(Path.Combine("TestData", "img", "FLB_509619692RAS_T0530000FHAZ00323M_.IMG"), ImageConverters.PDSBitMaskValueRangeToNormalizedImage, new float[] { 0 });
            Assert.IsTrue(imgMasked.HasMask);
            Assert.IsTrue(imgMasked.IsInvalid(4, 6));
            Assert.IsFalse(imgMasked.IsInvalid(5, 6));

            Image mastcam = new PDSSeralizer().Read(Path.Combine("TestData", "img", "ML0_451292526RCX_S0311094MCAM02555M1.IMG"), ImageConverters.PDSBitMaskValueRangeToNormalizedImage, new float[] { 0, 0, 0 });
            Assert.IsTrue(mastcam.HasMask);
            Assert.AreEqual(3, mastcam.Data.Length);
            Assert.AreEqual(1408 * 1200, mastcam.Data[0].Length);

            Image range = new PDSSeralizer().Read(Path.Combine("TestData", "img", "NLB_451649560RNGLF0311330NCAM12813M1.IMG"), ImageConverters.PassThrough, new float[] { 0 });
            Assert.IsTrue(range.HasMask);
            double total = 0;
            foreach (double d in range)
            {
                total += d;
            }
            AssertE.AreSimilar(5184855.197033, total, 0.0001);
            Image arm = new PDSSeralizer().Read(Path.Combine("TestData", "img", "NLB_451025090ARMLF0311052NCAM00493M1.IMG"), ImageConverters.PassThrough);
            Assert.AreEqual(5, arm.Bands);

            Image msss = new PDSSeralizer().Read(Path.Combine("TestData", "img", "0608ML0025660260301542E01_DRCX.IMG"), ImageConverters.PDSBitMaskValueRangeToNormalizedImage);
            Assert.AreEqual(3, msss.Bands);
        }

        [TestMethod]
        public void PDSTestWrite()
        {
            Image img = new PDSSeralizer().Read(Path.Combine("TestData", "img", "FLB_509619692RAS_T0530000FHAZ00323M_.IMG"), ImageConverters.PDSBitMaskValueRangeToNormalizedImage);
            img.Save<byte>("pds_write_byte.IMG");
            Image other = Image.Load("pds_write_byte.IMG");
            foreach(ImageCoordinate coord in img.Coordinates(false))
            {
                AssertE.AreSimilar(img[coord.Band, coord.Row, coord.Col], other[coord.Band, coord.Row, coord.Col], 1E-2);
            }
            img.Save<ushort>("pds_write_ushort.IMG");
            other = Image.Load("pds_write_ushort.IMG");
            foreach (ImageCoordinate coord in img.Coordinates(false))
            {
                AssertE.AreSimilar(img[coord.Band, coord.Row, coord.Col], other[coord.Band, coord.Row, coord.Col], 1E-3);
            }
        }
    }
}
