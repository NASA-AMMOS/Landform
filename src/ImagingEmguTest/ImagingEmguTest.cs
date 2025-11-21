using System.IO;
using Emgu.CV.Structure;
using Emgu.CV;
using JPLOPS.Imaging;
using JPLOPS.Imaging.Emgu;
using JPLOPS.TestExtensions;

namespace ImagingEmguTest
{
    public class ImagingEmguTest
    {
        [Fact]
        public void OpsToEmguTest()
        {
            Image pattern = Image.Load("testPattern.png");
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

        [Fact]
        public void EmguToOpsTest()
        {
            Image<Rgb, byte> pattern = new Image<Rgb, byte>("testPattern.png");
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

        [Fact]
        public void RoundTripTest()
        {
            Image pattern = Image.Load("testPattern.png");
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
    }
}
