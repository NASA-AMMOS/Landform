using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using System.IO;

namespace ImagingTest.Serializers
{
    [TestClass]
    [DeploymentItem("gdal", "gdal")]
    [DeploymentItem("FreeImage32.dll")]
    [DeploymentItem("FreeImage64.dll")]
    [DeploymentItem("nvtt32.dll")]
    [DeploymentItem("nvtt64.dll")]
    [DeploymentItem("TestData", "TestData")]
    public class DDSSeralizerTest
    {
        [TestMethod]
        public void DDSSeralizerWrite()
        {
            Image pattern = Image.Load(Path.Combine("TestData", "img", "testPattern.png"));
            pattern.Save<byte>("ddsTest.dds");

            Image roundTrip = Image.Load("ddsTest.dds");
            roundTrip.Save<byte>("ddsTest_RoundTrip.png");
        }

        [TestMethod]
        public void DDSSeralizerWriteAlpha()
        {
            Image pattern = Image.Load(Path.Combine("TestData", "img", "testPattern.png"));
            Image alpha = new Image(4, pattern.Width, pattern.Height);
            for (int r = 0; r < pattern.Height; r++)
            {
                for (int c = 0; c < pattern.Width; c++)
                {
                    for (int b = 0; b < pattern.Bands; b++)
                    {
                        alpha[b, r, c] = pattern[b, r, c];
                    }
                    alpha[3, r, c] = 0.5f;
                }
            }
            alpha.Save<byte>("ddsTestAlpha.dds");

            Image roundTrip = Image.Load("ddsTestAlpha.dds");
            roundTrip.Save<byte>("ddsTestAlpha_RoundTrip.png");
        }

        [TestMethod]
        public void DDSSeralizerWriteSingleBand()
        {
            Image pattern = Image.Load(Path.Combine("TestData", "img", "testPattern.png"));
            Image singleBand = new Image(1, pattern.Width, pattern.Height);
            for (int r = 0; r < pattern.Height; r++)
            {
                for (int c = 0; c < pattern.Width; c++)
                {
                    singleBand[0, r, c] = pattern[0, r, c];
                }
            }
            singleBand.Save<byte>("ddsTestSingleBand.dds");


            Image roundTrip = Image.Load("ddsTestSingleBand.dds");
            roundTrip.Save<byte>("ddsTestSingleBand_RoundTrip.png");
        }

    }
}
