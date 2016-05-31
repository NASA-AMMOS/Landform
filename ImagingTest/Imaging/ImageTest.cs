using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;

namespace ImageTest
{
    [TestClass]
    public class ImageTest
    {
        [TestMethod]
        public void ImageLoad()
        {

            Image imgOrig = new Image(3, 20, 30);
            imgOrig.Save<byte>("load.jpg");
            Image imgRead = Image.Load("load.jpg", new GDALSeralizer(),  ImageConverters.ValueRangeToNormalizedImage);
        }
    }
}
