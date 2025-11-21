using JPLOPS.Imaging;
using System.IO;
using JPLOPS.TestExtensions;

namespace ImagingTest
{
    public class PDSSerializerTest
    {
        [Fact]
        public void PDSTestRead()
        {
            Image img = new PDSSerializer().Read("FLB_509619692RAS_T0530000FHAZ00323M1.IMG", ImageConverters.PDSBitMaskValueRangeToNormalizedImage);
            Assert.False(img.HasMask);
            Assert.Equal(1, img.Bands);
            Assert.Equal(img.Width * img.Height, img.GetBandData(0).Length);

            Image imgMasked = new PDSSerializer().Read("FLB_509619692RAS_T0530000FHAZ00323M1.IMG", ImageConverters.PDSBitMaskValueRangeToNormalizedImage, new float[] { 0 });
            Assert.True(imgMasked.HasMask);
            Assert.False(imgMasked.IsValid(4, 6));
            Assert.True(imgMasked.IsValid(5, 6));

            Image mastcam = new PDSSerializer().Read("ML0_451292526RCX_S0311094MCAM02555M1.IMG", ImageConverters.PDSBitMaskValueRangeToNormalizedImage, new float[] { 0, 0, 0 });
            Assert.True(mastcam.HasMask);
            Assert.Equal(3, mastcam.Bands);
            Assert.Equal(1408 * 1200, mastcam.GetBandData(0).Length);

            Image range = new PDSSerializer().Read("NLB_451649560RNGLF0311330NCAM12813M1.IMG", ImageConverters.PassThrough, new float[] { 0 });
            Assert.True(range.HasMask);
            double total = 0;
            foreach (double d in range)
            {
                total += d;
            }
            AssertE.AreSimilar(5184855.197033, total, 0.0001);
            Image arm = new PDSSerializer().Read("NLB_451025090ARMLF0311052NCAM00493M1.IMG", ImageConverters.PassThrough);
            Assert.Equal(5, arm.Bands);

            Image msss = new PDSSerializer().Read("0608ML0025660260301542E01_DRCX.IMG", ImageConverters.PDSBitMaskValueRangeToNormalizedImage);
            Assert.Equal(3, msss.Bands);
        }

        [Fact]
        public void PDSTestWrite()
        {
            Image img = new PDSSerializer().Read("FLB_509619692RAS_T0530000FHAZ00323M1.IMG", ImageConverters.PDSBitMaskValueRangeToNormalizedImage);
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
