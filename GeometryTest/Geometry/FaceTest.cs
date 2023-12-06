using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using JPLOPS.Geometry;

namespace GeometryTest
{
    [TestClass]
    public class FaceTest
    {
        [TestMethod]
        public void BasicFaceMethodTest()
        {
            Face f = new Face(1, 2, 4);
            Assert.AreEqual(f.P0,1);
            Assert.AreEqual(f.P1,2);
            Assert.AreEqual(f.P2,4);

            Face z = new Face(1, 2, 4);
            Dictionary<Face, int> dict = new Dictionary<Face, int>();
            dict.Add(f, 1);
            Assert.IsTrue(dict.ContainsKey(z));

            int[] a = new int[3];
            f.FillArray(a);
            Assert.AreEqual(a[0], 1);
            Assert.AreEqual(a[1], 2);
            Assert.AreEqual(a[2], 4);
            

            int[] b = f.ToArray();
            Assert.AreEqual(b[0], 1);
            Assert.AreEqual(b[1], 2);
            Assert.AreEqual(b[2], 4);


            Face j = new Face(new int[] { 5, 6, 7 });
            Assert.AreEqual(j.P0, 5);
            Assert.AreEqual(j.P1, 6);
            Assert.AreEqual(j.P2, 7);

            Face c = new Face(j);
            Assert.AreEqual(c.P0, 5);
            Assert.AreEqual(c.P1, 6);
            Assert.AreEqual(c.P2, 7);
        }

        [TestMethod]
        public void FaceIsValidTest()
        {
            Assert.AreEqual(new Face(1, 2, 4).IsValid(), true);
            Assert.AreEqual(new Face(1, 1, 4).IsValid(), false);
            Assert.AreEqual(new Face(1, 3, 1).IsValid(), false);
            Assert.AreEqual(new Face(0, 1, 1).IsValid(), false);
        }        
    }
}
