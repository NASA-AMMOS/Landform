using JPLOPS.Imaging;

namespace ImagingTest
{
    public class ImageCoordinateTest
    {

        [Fact]
        public void TestCoordinateConstructor()
        {
            ImageCoordinate ic = new ImageCoordinate(1, 2, 3);
            Assert.Equal(1, ic.Band);
            Assert.Equal(2, ic.Row);
            Assert.Equal(3, ic.Col);
        }
    }
}
