using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;
using OPS.Imaging.Emgu;
using System.IO;
using Emgu.CV.Structure;
using Emgu.CV;
using OPS.Test;

namespace ImagingEmguTest
{
    [TestClass]
    [DeploymentItem("gdal", "gdal")]
    [DeploymentItem("TestData", "TestData")]
    [DeploymentItem("x86", "x86")]
    [DeploymentItem("x64", "x64")]
    public class ImageTests
    {
        [TestMethod]
        public void OpsToEmguTest()
        {
            Image pattern = Image.Load(Path.Combine("TestData", "img", "testPattern.png"));
            var converted = pattern.ToEmgu<Rgb>();
            var data = converted.Data;
            for (int band = 0; band < pattern.Bands; band++)
            {
                for (int row = 0; row < pattern.Height; row++)
                {
                    for (int col = 0; col < pattern.Width; col++)
                    {
                        AssertE.AreSimilar(data[row, col, band] / 255.0, pattern[band, row, col], 1 / 512.0);
                    }
                }
            }
        }

        [TestMethod]
        public void EmguToOpsTest()
        {
            Image<Rgb, byte> pattern = new Image<Rgb, byte>(Path.Combine("TestData", "img", "testPattern.png"));
            var converted = pattern.ToOPSImage();
            var data = pattern.Data;
            for (int band = 0; band < pattern.NumberOfChannels; band++)
            {
                for (int row = 0; row < pattern.Height; row++)
                {
                    for (int col = 0; col < pattern.Width; col++)
                    {
                        AssertE.AreSimilar(data[row, col, band] / 255.0, converted[band, row, col], 1 / 512.0);
                    }
                }
            }
        }

        [TestMethod]
        public void RoundTripTest()
        {
            Image pattern = Image.Load(Path.Combine("TestData", "img", "testPattern.png"));
            var converted = pattern.ToEmgu<Rgb>();
            var rt = converted.ToOPSImage();
            for (int band = 0; band < pattern.Bands; band++)
            {
                for (int row = 0; row < pattern.Height; row++)
                {
                    for (int col = 0; col < pattern.Width; col++)
                    {
                        AssertE.AreSimilar(rt[band, row, col], pattern[band, row, col], 1 / 512.0);
                    }
                }
            }
        }

        [TestMethod]
        public void PDSLoadFromLBL()
        {
            string filename = Path.Combine("TestData", "img", @"0606MR0025570000400933E01_DRCX.LBL");
            var meta = new PDSMetadata(filename);
            Assert.IsTrue(meta.DataPath != null);
            var img = Image.Load(filename);
            Assert.AreEqual(img.Width, 1344);
            Assert.AreEqual(img.Height, 1200);
        }
    }
}
