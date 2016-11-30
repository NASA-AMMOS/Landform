using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;

namespace XnaTest
{
    [TestClass]
    public class Vector4Test
    {
        [TestMethod]
        public void ConstructorTest()
        {
            Vector4 b = new Vector4(1, 2, 3,4);
            Assert.AreEqual(1, b.X);
            Assert.AreEqual(2, b.Y);
            Assert.AreEqual(3, b.Z);
            Assert.AreEqual(3, b.Z);

            Vector4 c = new Vector4(b);
            Assert.AreEqual(1, c.X);
            Assert.AreEqual(2, c.Y);
            Assert.AreEqual(3, c.Z);
            Assert.AreEqual(4, c.W);
        }

        [TestMethod]
        public void SetTest()
        {
            Vector4 a = new Vector4();
            Assert.AreEqual(0, a.X);
            Assert.AreEqual(0, a.Y);
            Assert.AreEqual(0, a.Z);
            Assert.AreEqual(0, a.W);
            a.Set(3, 4, 5,6);
            Assert.AreEqual(3, a.X);
            Assert.AreEqual(4, a.Y);
            Assert.AreEqual(5, a.Z);
            Assert.AreEqual(6, a.W);
        }

   
        [TestMethod]
        public void NormalizeTest()
        {
            Vector4 a = new Vector4(8, -9, 2, 2);
            a.Normalize();
            Assert.AreEqual(true, Math.Abs(1-a.Length()) < 0.0001);

            Vector4 b = new Vector4(1, 2, 3, 2);
            b.Normalize();
            Assert.AreEqual(b.X, 1.0 / Math.Sqrt(18));
            Assert.AreEqual(b.Y, 2.0 / Math.Sqrt(18));

            b = new Vector4(1, 2, 3, 2);
            Vector4 c = Vector4.Normalize(b);
            Assert.AreEqual(b.X, 1);
            Assert.AreEqual(b.Y, 2);
            Assert.AreEqual(c.X, 1.0 / Math.Sqrt(18));
            Assert.AreEqual(c.Y, 2.0 / Math.Sqrt(18));

            b = new Vector4(1, 2, 3,2);
            c = new Vector4();
            Vector4.Normalize(ref b, out c);
            Assert.AreEqual(b.X, 1);
            Assert.AreEqual(b.Y, 2);
            Assert.AreEqual(c.X, 1.0 / Math.Sqrt(18));
            Assert.AreEqual(c.Y, 2.0 / Math.Sqrt(18));

        }

        [TestMethod]
        public void NormalizeTestException1()
        {
            try
            {
                new Vector4().Normalize();
            }
            catch (DivideByZeroException)
            {
                return;
            }
            Assert.Fail();
        }

        [TestMethod]
        public void NormalizeTestException2()
        {
            try
            {
                Vector4.Normalize(new Vector4());
            }
            catch (DivideByZeroException)
            {
                return;
            }
            Assert.Fail();
        }
        [TestMethod]
        public void NormalizeTestException3()
        {
            try
            {
                Vector4 a = new Vector4();
                Vector4 b = new Vector4();
                Vector4.Normalize(ref a, out b);
            }
            catch (DivideByZeroException)
            {
                return;
            }
            Assert.Fail();
        }

       

        [TestMethod]
        public void MinMaxTest()
        {
            Vector4 a = new Vector4(3, 4, -5, 7);
            Vector4 b = new Vector4(8, 1, 2,9);
            Assert.AreEqual(new Vector4(3, 1, -5,7), Vector4.Min(a, b));
            Assert.AreEqual(new Vector4(8, 4, 2, 9), Vector4.Max(a, b));
            Assert.AreEqual(new Vector4(3, 1, -5,7), a.Min(b));
            Assert.AreEqual(new Vector4(8, 4, 2, 9), a.Max(b));
        }

      
        [TestMethod]
        public void AlmostEqualTest()
        {
            Vector4 a = new Vector4(0.0000001, 0.0000002, 0.0000003, 0.0000004);
            Vector4 b = new Vector4(0.0000005, 0.0000002, 0.0000003, 0.0000004);
            // Test static
            Assert.AreEqual(true, Vector4.AlmostEqual(a, b));
            Assert.AreEqual(false, Vector4.AlmostEqual(a, b, 1E-8));
            // Test x
            Assert.AreEqual(false, a == b);
            Assert.AreEqual(true, a.AlmostEqual(b));
            Assert.AreEqual(false, a.AlmostEqual(b, 1E-8));
            // Test y
            b = new Vector4(0.0000001, 0.0000005, 0.0000003, 0.0000004);
            Assert.AreEqual(false, a == b);
            Assert.AreEqual(true, a.AlmostEqual(b));
            Assert.AreEqual(false, a.AlmostEqual(b, 1E-8));
            // Test z
            b = new Vector4(0.0000001, 0.0000002, 0.0000005, 0.0000004);
            Assert.AreEqual(false, a == b);
            Assert.AreEqual(true, a.AlmostEqual(b));
            Assert.AreEqual(false, a.AlmostEqual(b, 1E-8));
            // Test w
            b = new Vector4(0.0000001, 0.0000002, 0.0000004, 0.0000005);
            Assert.AreEqual(false, a == b);
            Assert.AreEqual(true, a.AlmostEqual(b));
            Assert.AreEqual(false, a.AlmostEqual(b, 1E-8));
            // Test same
            b = new Vector4(0.0000001, 0.0000002, 0.0000003, 0.0000004);
            Assert.AreEqual(true, a == b);
            Assert.AreEqual(true, a.AlmostEqual(b));
            Assert.AreEqual(true, a.AlmostEqual(b, 1E-8));
        }

    }
}
