using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;

namespace ImagingTest
{
    [TestClass]
    public class ImageTest
    {
        [TestMethod]
        public void Constructor()
        {
            Image img = new Image(@"G:\TerrainResults\sd0003101330\build\SuperDem\superdem_near.tif");
        }
    }
}
