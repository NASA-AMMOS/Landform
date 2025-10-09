using JPLOPS.Imaging;

namespace ImagingTest.Imaging
{
    public class InPainterTest
    {
        [Fact]
        public void IpPaintMaskTest()
        {
            Image img = new Image(1, 100, 100);
            img.CreateMask(true);
            img.SetMaskValue(27, 37, false);
            img.Inpaint();
            for (int r = 0; r < 100; r++)
            {
                for (int c = 0; c < 100; c++)
                {
                    Assert.True(img.IsValid(r, c));
                }
            }
        }
    }
}
