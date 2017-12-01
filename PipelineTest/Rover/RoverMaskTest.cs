using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using OPS.Imaging;
using OPS.Pipeline;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PipelineTest
{
    [TestClass]
    public class RoverMaskTest
    {
        [TestMethod]
        [DeploymentItem("TestData", "TestData")]
        [DeploymentItem("Rover\\Resources", "Rover\\Resources")]
        [DeploymentItem("x86", "x86")]
        [DeploymentItem("x64", "x64")]
        public void RoverMaskSanity()
        {
            string filename = Path.Combine("TestData", "img", @"NLB_451557756RASLF0311330NCAM00353M1.IMG");

            ImageRef r = new ImageRef(filename);
            Image mask = RoverMask.Build(r);

            // Check pixel in center of "O" in "CURIOSITY" is masked out
            Assert.AreEqual(mask[0, 590, 388], 0.0);
            // Check rock pixel is not
            Assert.AreEqual(mask[0, 287, 819], 1.0);
        }
    }
}
