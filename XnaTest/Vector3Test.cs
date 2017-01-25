using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;

namespace XnaTest
{
    [TestClass]
    public class Vector3Test
    {
        [TestMethod]
        public void ConstructorTest()
        {
            Vector3 a = new Vector3();
            Assert.AreEqual(0, a.X);
            Assert.AreEqual(0, a.Y);
            Assert.AreEqual(0, a.Z);

            Vector3 b = new Vector3(1, 2, 3);
            Assert.AreEqual(1, b.X);
            Assert.AreEqual(2, b.Y);
            Assert.AreEqual(3, b.Z);

            Vector3 c = new Vector3(b);
            Assert.AreEqual(1, c.X);
            Assert.AreEqual(2, c.Y);
            Assert.AreEqual(3, c.Z);
        }

        [TestMethod]
        public void SetTest()
        {
            Vector3 a = new Vector3();
            Assert.AreEqual(0, a.X);
            Assert.AreEqual(0, a.Y);
            Assert.AreEqual(0, a.Z);
            a.Set(3, 4, 5);
            Assert.AreEqual(3, a.X);
            Assert.AreEqual(4, a.Y);
            Assert.AreEqual(5, a.Z);
        }

        [TestMethod]
        public void MagnitudeTest()
        {
            Vector3 a = new Vector3(8, -9, 2);
            Assert.AreEqual(Math.Sqrt(8 * 8 + 9 * 9 + 2 * 2), a.Length());

        }

        [TestMethod]
        public void SqrdMagnitudeTest()
        {
            Vector3 a = new Vector3(8, -9, 2);
            Assert.AreEqual(8 * 8 + 9 * 9 + 2 * 2, a.LengthSquared());

        }

        [TestMethod]
        public void NormalizeTest()
        {
            Vector3 a = new Vector3(8, -9, 2);
            a.Normalize();
            Assert.AreEqual(true, Math.Abs(1-a.Length()) < 0.0001);

            Vector3 b = new Vector3(1, 2, 3);
            b.Normalize();
            Assert.AreEqual(b.X, 1.0 / Math.Sqrt(14));
            Assert.AreEqual(b.Y, 2.0 / Math.Sqrt(14));

            b = new Vector3(1, 2, 3);
            Vector3 c = Vector3.Normalize(b);
            Assert.AreEqual(b.X, 1);
            Assert.AreEqual(b.Y, 2);
            Assert.AreEqual(c.X, 1.0 / Math.Sqrt(14));
            Assert.AreEqual(c.Y, 2.0 / Math.Sqrt(14));

            b = new Vector3(1, 2, 3);
            c = new Vector3();
            Vector3.Normalize(ref b, out c);
            Assert.AreEqual(b.X, 1);
            Assert.AreEqual(b.Y, 2);
            Assert.AreEqual(c.X, 1.0 / Math.Sqrt(14));
            Assert.AreEqual(c.Y, 2.0 / Math.Sqrt(14));

        }

        [TestMethod]
        public void NormalizeTestException1()
        {
            try
            {
                new Vector3().Normalize();
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
                Vector3.Normalize(new Vector3());
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
                Vector3 a = new Vector3();
                Vector3 b = new Vector3();
                Vector3.Normalize(ref a, out b);
            }
            catch (DivideByZeroException)
            {
                return;
            }
            Assert.Fail();
        }

        [TestMethod]
        public void NormalizedTest()
        {
            Vector3 a = new Vector3(1, 2, 0);
            Vector3 b = Vector3.Normalize(a);
            Assert.AreEqual(1.0, a.X);
            Assert.AreEqual(2.0, a.Y);
            Assert.AreEqual(0, a.Z);
            Assert.AreEqual(true, Math.Abs(1 - b.LengthSquared()) < 0.0001);
            Assert.AreEqual(b.X, 1.0 / Math.Sqrt(5));
            Assert.AreEqual(b.Y, 2.0 / Math.Sqrt(5));
            Assert.AreEqual(b.Z, 0);
        }

        [TestMethod]
        public void DotTest()
        {
            Vector3 a = new Vector3(2, 4, 7);
            Vector3 b = new Vector3(7, 3, 9);
            double c = a.Dot(b);
            double d = b.Dot(a);
            Assert.AreEqual(89, c);
            Assert.AreEqual(89, d);
        }

        [TestMethod]
        public void CrossTest()
        {
            Vector3 a = new Vector3(2, 4, 7);
            Vector3 b = new Vector3(7, 3, 9);
            Vector3 c = a.Cross(b);
            Vector3 d = b.Cross(a);
            Assert.AreEqual(new Vector3(15,31,-22), c);
            Assert.AreEqual(new Vector3(-15, -31, 22), d);
        }


        [TestMethod]
        public void MinMaxTest()
        {
            Vector3 a = new Vector3(3, 4, -5);
            Vector3 b = new Vector3(8, 1, 2);
            Assert.AreEqual(new Vector3(3, 1, -5), Vector3.Min(a, b));
            Assert.AreEqual(new Vector3(8, 4, 2), Vector3.Max(a, b));
            Assert.AreEqual(new Vector3(3, 1, -5), a.Min(b));
            Assert.AreEqual(new Vector3(8, 4, 2), a.Max(b));
        }

        [TestMethod]
        public void AddTest()
        {
            Vector3 a = new Vector3(1, 2,5);
            Vector3 b = new Vector3(3, -8,9);
            Vector3 c = a + b;
            Assert.AreEqual(4, c.X);
            Assert.AreEqual(-6, c.Y);
            Assert.AreEqual(14, c.Z);
        }

        [TestMethod]
        public void SubTest()
        {
            Vector3 a = new Vector3(1, 2,5);
            Vector3 b = new Vector3(3, -8, 9);

            Vector3 c = a - b;
            Assert.AreEqual(-2, c.X);
            Assert.AreEqual(10, c.Y);
            Assert.AreEqual(-4, c.Z);
            Vector3 d = b - a;
            Assert.AreEqual(2, d.X);
            Assert.AreEqual(-10, d.Y);
            Assert.AreEqual(4, d.Z);
        }

        [TestMethod]
        public void NegTest()
        {
            Vector3 a = new Vector3(1, -2,8);
            Vector3 b = -a;
            Assert.AreEqual(-1, b.X);
            Assert.AreEqual(2, b.Y);
            Assert.AreEqual(-8, b.Z);
        }

        [TestMethod]
        public void ScaleTest()
        {
            Vector3 a = new Vector3(3, -2,4);
            Vector3 b = a * 3;
            Assert.AreEqual(9, b.X);
            Assert.AreEqual(-6, b.Y);
            Assert.AreEqual(12, b.Z);
            Vector3 c = -4 * a;
            Assert.AreEqual(-12, c.X);
            Assert.AreEqual(8, c.Y);
            Assert.AreEqual(-16, c.Z);
        }

        [TestMethod]
        public void DivideTest()
        {
            Vector3 a = new Vector3(3, -2,8);
            Vector3 b = a / 4;
            Assert.AreEqual(3.0 / 4, b.X);
            Assert.AreEqual(-2.0 / 4, b.Y);
            Assert.AreEqual(2.0, b.Z);

        }

        [TestMethod]
        public void NotEqualTest()
        {
            Vector3 a = new Vector3(3, -2, 1);
            Vector3 b = new Vector3(3, 0,  1);
            Vector3 c = new Vector3(0, -2, 1);
            Vector3 d = new Vector3(3, -2, 1);
            Vector3 e = new Vector3(3, -2, 5);
            Assert.AreEqual(true, a != b);
            Assert.AreEqual(true, a != c);
            Assert.AreEqual(false, a != d);
            Assert.AreEqual(true, a != e);
        }

        [TestMethod]
        public void EqualTest()
        {
            Vector3 a = new Vector3(3, -2, 1);
            Vector3 b = new Vector3(3, 0, 1);
            Vector3 c = new Vector3(0, -2, 1);
            Vector3 d = new Vector3(3, -2, 1);
            Vector3 e = new Vector3(3, -2, 5);
            Assert.AreEqual(false, a == b);
            Assert.AreEqual(false, a == c);
            Assert.AreEqual(true, a == d);
            Assert.AreEqual(false, a == e);
        }

        [TestMethod]
        public void AlmostEqualTest()
        {
            Vector3 a = new Vector3(0.0000001, 0.0000002, 0.0000003);
            Vector3 b = new Vector3(0.0000004, 0.0000002, 0.0000003);
            // Test static
            Assert.AreEqual(true, Vector3.AlmostEqual(a, b));
            Assert.AreEqual(false, Vector3.AlmostEqual(a, b, 1E-8));
            // Test x
            Assert.AreEqual(false, a == b);
            Assert.AreEqual(true, a.AlmostEqual(b));
            Assert.AreEqual(false, a.AlmostEqual(b, 1E-8));
            // Test y
            b = new Vector3(0.0000001, 0.0000004, 0.0000003);
            Assert.AreEqual(false, a == b);
            Assert.AreEqual(true, a.AlmostEqual(b));
            Assert.AreEqual(false, a.AlmostEqual(b, 1E-8));
            // Test z
            b = new Vector3(0.0000001, 0.0000002, 0.0000004);
            Assert.AreEqual(false, a == b);
            Assert.AreEqual(true, a.AlmostEqual(b));
            Assert.AreEqual(false, a.AlmostEqual(b, 1E-8));
            // Test same
            b = new Vector3(0.0000001, 0.0000002, 0.0000003);
            Assert.AreEqual(true, a == b);
            Assert.AreEqual(true, a.AlmostEqual(b));
            Assert.AreEqual(true, a.AlmostEqual(b, 1E-8));
        }

        [TestMethod]
        public void AssignmentTest()
        {
            Vector3 a = new Vector3(3, -2, 5);
            Vector3 b = new Vector3(4, 0, 9);
            b = a;
            Assert.AreEqual(a.X, b.X);
            Assert.AreEqual(a.Y, b.Y);
            Assert.AreEqual(a.Z, b.Z);
            b.X = 7;
            Assert.AreEqual(7, b.X);
            Assert.AreEqual(3, a.X);
        }

        [TestMethod]
        public void RGBTest()
        {
            Vector3 a = new Vector3(3, -2, 5);

            Assert.AreEqual(3, a.R);
            Assert.AreEqual(-2, a.G);
            Assert.AreEqual(5, a.B);
            a.R = 12;
            a.G = 11;
            a.B = 14;
            Assert.AreEqual(12, a.R);
            Assert.AreEqual(11, a.G);
            Assert.AreEqual(14, a.B);


        }
    }
}
