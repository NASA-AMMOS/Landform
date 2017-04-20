using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Pipeline;
using Microsoft.Xna.Framework;

namespace PipelineTest.Tiling
{
    [TestClass]
    public class Tile3DTest
    {
        [TestMethod]
        public void Tile3DMatrixTest()
        {
            Tile3D.Tile t = new Tile3D.Tile();
            Matrix m = new Matrix(0, 1, 2, 3, 
                                  4, 5, 6, 7, 
                                  8, 9, 10, 11, 
                                  12, 13, 14, 15);
            t.TransformMatrix = m;
            double[] expected = new double[] { 0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15}; 
            for(int i = 0; i < 16; i++)
            {
                Assert.AreEqual(expected[i], t.transform[i]);
            }
            Matrix m2 = t.TransformMatrix;
            Assert.IsTrue(m.Equals(m2));

        }
    }
}
