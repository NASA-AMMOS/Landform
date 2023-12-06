using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;

namespace XnaTest
{
    [TestClass]
    public class Vector2Test
    {
        [TestMethod]
        public void ConstructorTest()
        {
            Vector2 a = new Vector2();
            Assert.AreEqual(0, a.X);
            Assert.AreEqual(0, a.Y);

            Vector2 b = new Vector2(1, 2);
            Assert.AreEqual(1, b.X);
            Assert.AreEqual(2, b.Y);
            Assert.AreEqual(1, b.U);
            Assert.AreEqual(2, b.V);

            Vector2 c = new Vector2(b);
            Assert.AreEqual(1, c.X);
            Assert.AreEqual(2, c.Y);
        }

        [TestMethod]
        public void SetTest()
        {
            Vector2 a = new Vector2();
            Assert.AreEqual(0, a.X);
            Assert.AreEqual(0, a.Y);
            a.Set(3, 4);
            Assert.AreEqual(3, a.X);
            Assert.AreEqual(4, a.Y);
        }

        [TestMethod]
        public void MagnitudeTest()
        {
            Vector2 a = new Vector2(8, -9);
            Assert.AreEqual(Math.Sqrt(8 * 8 + 9 * 9), a.Length());

        }

        [TestMethod]
        public void SqrdMagnitudeTest()
        {
            Vector2 a = new Vector2(8, -9);
            Assert.AreEqual(8 * 8 + 9 * 9, a.LengthSquared());

        }

        [TestMethod]
        public void NormalizeTest()
        {
            Vector2 a = new Vector2(8, -9);
            a.Normalize();
            Assert.AreEqual(1, a.Length());

            Vector2 b = new Vector2(1, 2);
            b.Normalize();
            Assert.AreEqual(b.X, 1.0 / Math.Sqrt(5));
            Assert.AreEqual(b.Y, 2.0 / Math.Sqrt(5));

            b = new Vector2(1, 2);
            Vector2 c = Vector2.Normalize(b);
            Assert.AreEqual(b.X, 1);
            Assert.AreEqual(b.Y, 2);
            Assert.AreEqual(c.X, 1.0 / Math.Sqrt(5));
            Assert.AreEqual(c.Y, 2.0 / Math.Sqrt(5));

            b = new Vector2(1, 2);
            c = new Vector2();
            Vector2.Normalize(ref b, out c);
            Assert.AreEqual(b.X, 1);
            Assert.AreEqual(b.Y, 2);
            Assert.AreEqual(c.X, 1.0 / Math.Sqrt(5));
            Assert.AreEqual(c.Y, 2.0 / Math.Sqrt(5));
            
        }

        [TestMethod]
        public void NormalizeTestException1()
        {
            try
            {
                new Vector2().Normalize();
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
                Vector2.Normalize(new Vector2());
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
                Vector2 a = new Vector2();
                Vector2 b = new Vector2();
                Vector2.Normalize(ref a, out b);
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
            Vector2 a = new Vector2(1, 2);
            Vector2 b = Vector2.Normalize(a);
            Assert.AreEqual(1.0, a.X);
            Assert.AreEqual(2.0, a.Y);
            Assert.AreEqual(b.X, 1.0 / Math.Sqrt(5));
            Assert.AreEqual(b.Y, 2.0 / Math.Sqrt(5));
        }

        [TestMethod]
        public void DotTest()
        {
            Vector2 a = new Vector2(2, 4);
            Vector2 b = new Vector2(7,3);
            double c = a.Dot(b);
            double d = b.Dot(a);
            Assert.AreEqual(26, c);
            Assert.AreEqual(26, d);
        }

        [TestMethod]
        public void AddTest()
        {
            Vector2 a = new Vector2(1, 2);
            Vector2 b = new Vector2(3, -8);
            Vector2 c = a + b;
            Assert.AreEqual(4, c.X);
            Assert.AreEqual(-6, c.Y);
        }

        [TestMethod]
        public void SubTest()
        {
            Vector2 a = new Vector2(1, 2);
            Vector2 b = new Vector2(3, -8);
            Vector2 c = a - b;
            Assert.AreEqual(-2, c.X);
            Assert.AreEqual(10, c.Y);
            Vector2 d = b - a;
            Assert.AreEqual(2, d.X);
            Assert.AreEqual(-10, d.Y);
        }

        [TestMethod]
        public void MinMaxTest()
        {
            Vector2 a = new Vector2(4, -5);
            Vector2 b = new Vector2(1, 2);
            Assert.AreEqual(new Vector2(1, -5), Vector2.Min(a, b));
            Assert.AreEqual(new Vector2(4, 2), Vector2.Max(a, b));
            Assert.AreEqual(new Vector2(1, -5), a.Min(b));
            Assert.AreEqual(new Vector2(4, 2), a.Max(b));

        }
        
        [TestMethod]
        public void NegTest()
        {
            Vector2 a = new Vector2(1, -2);
            Vector2 b = -a;
            Assert.AreEqual(-1, b.X);
            Assert.AreEqual(2, b.Y);
        }

        [TestMethod]
        public void ScaleTest()
        {
            Vector2 a = new Vector2(3, -2);
            Vector2 b = a * 3;
            Assert.AreEqual(9, b.X);
            Assert.AreEqual(-6, b.Y);
            Vector2 c = -4 * a;
            Assert.AreEqual(-12, c.X);
            Assert.AreEqual(8, c.Y);
        }

        [TestMethod]
        public void DivideTest()
        {
            Vector2 a = new Vector2(3, -2);
            Vector2 b = a / 4;
            Assert.AreEqual(3.0/4, b.X);
            Assert.AreEqual(-2.0/4, b.Y);
        }

        [TestMethod]
        public void NotEqualTest()
        {
            Vector2 a = new Vector2(3, -2);
            Vector2 b = new Vector2(3, 0);
            Vector2 c = new Vector2(0, -2);
            Vector2 d = new Vector2(3, -2);
            Assert.AreEqual(true, a !=b);
            Assert.AreEqual(true, a != c);
            Assert.AreEqual(false, a != d);
        }

        [TestMethod]
        public void EqualTest()
        {
            Vector2 a = new Vector2(3, -2);
            Vector2 b = new Vector2(3, 0);
            Vector2 c = new Vector2(0, -2);
            Vector2 d = new Vector2(3, -2);
            Assert.AreEqual(false, a == b);
            Assert.AreEqual(false, a == c);
            Assert.AreEqual(true, a == d);
        }

        [TestMethod]
        public void AlmostEqualTest()
        {
            Vector2 a = new Vector2(0.0000001, 0.0000002);
            Vector2 b = new Vector2(0.0000002, 0.0000002); 
            // Test static
            Assert.AreEqual(true, Vector2.AlmostEqual(a, b));
            Assert.AreEqual(false, Vector2.AlmostEqual(a, b, 1E-8));
            // Test x           
            Assert.AreEqual(false, a == b);
            Assert.AreEqual(true, a.AlmostEqual(b));
            Assert.AreEqual(false, a.AlmostEqual(b, 1E-8));
            // Test y
            b = new Vector2(0.0000001, 0.0000003);
            Assert.AreEqual(false, a == b);
            Assert.AreEqual(true, a.AlmostEqual(b));
            Assert.AreEqual(false, a.AlmostEqual(b, 1E-8));
            // Test same
            b = new Vector2(0.0000001, 0.0000002);
            Assert.AreEqual(true, a == b);
            Assert.AreEqual(true, a.AlmostEqual(b));
            Assert.AreEqual(true, a.AlmostEqual(b, 1E-8));
           
        }

        [TestMethod]
        public void AssignmentTest()
        {
            Vector2 a = new Vector2(3, -2);
            Vector2 b = new Vector2(4, 0);
            b = a;
            Assert.AreEqual(a.X, b.X);
            Assert.AreEqual(a.Y, b.Y);
            b.X = 7;
            Assert.AreEqual(7, b.X);
            Assert.AreEqual(3, a.X);
        }

    }
}
