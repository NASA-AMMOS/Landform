using System;
using Xunit;
using Microsoft.Xna.Framework;

namespace XnaTest
{
    public class Vector2Test
    {
        [Fact]
        public void ConstructorTest()
        {
            Vector2 a = new Vector2();
            Assert.Equal(0, a.X);
            Assert.Equal(0, a.Y);

            Vector2 b = new Vector2(1, 2);
            Assert.Equal(1, b.X);
            Assert.Equal(2, b.Y);
            Assert.Equal(1, b.U);
            Assert.Equal(2, b.V);

            Vector2 c = new Vector2(b);
            Assert.Equal(1, c.X);
            Assert.Equal(2, c.Y);
        }

        [Fact]
        public void SetTest()
        {
            Vector2 a = new Vector2();
            Assert.Equal(0, a.X);
            Assert.Equal(0, a.Y);
            a.Set(3, 4);
            Assert.Equal(3, a.X);
            Assert.Equal(4, a.Y);
        }

        [Fact]
        public void MagnitudeTest()
        {
            Vector2 a = new Vector2(8, -9);
            Assert.Equal(Math.Sqrt(8 * 8 + 9 * 9), a.Length());

        }

        [Fact]
        public void SqrdMagnitudeTest()
        {
            Vector2 a = new Vector2(8, -9);
            Assert.Equal(8 * 8 + 9 * 9, a.LengthSquared());

        }

        [Fact]
        public void NormalizeTest()
        {
            Vector2 a = new Vector2(8, -9);
            a.Normalize();
            Assert.Equal(1, a.Length());

            Vector2 b = new Vector2(1, 2);
            b.Normalize();
            Assert.Equal(b.X, 1.0 / Math.Sqrt(5));
            Assert.Equal(b.Y, 2.0 / Math.Sqrt(5));

            b = new Vector2(1, 2);
            Vector2 c = Vector2.Normalize(b);
            Assert.Equal(1, b.X);
            Assert.Equal(2, b.Y);
            Assert.Equal(c.X, 1.0 / Math.Sqrt(5));
            Assert.Equal(c.Y, 2.0 / Math.Sqrt(5));

            b = new Vector2(1, 2);
            c = new Vector2();
            Vector2.Normalize(ref b, out c);
            Assert.Equal(1, b.X);
            Assert.Equal(2, b.Y);
            Assert.Equal(c.X, 1.0 / Math.Sqrt(5));
            Assert.Equal(c.Y, 2.0 / Math.Sqrt(5));
            
        }

        [Fact]
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

        [Fact]
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
        [Fact]
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

        [Fact]
        public void NormalizedTest()
        {
            Vector2 a = new Vector2(1, 2);
            Vector2 b = Vector2.Normalize(a);
            Assert.Equal(1.0, a.X);
            Assert.Equal(2.0, a.Y);
            Assert.Equal(b.X, 1.0 / Math.Sqrt(5));
            Assert.Equal(b.Y, 2.0 / Math.Sqrt(5));
        }

        [Fact]
        public void DotTest()
        {
            Vector2 a = new Vector2(2, 4);
            Vector2 b = new Vector2(7,3);
            double c = a.Dot(b);
            double d = b.Dot(a);
            Assert.Equal(26, c);
            Assert.Equal(26, d);
        }

        [Fact]
        public void AddTest()
        {
            Vector2 a = new Vector2(1, 2);
            Vector2 b = new Vector2(3, -8);
            Vector2 c = a + b;
            Assert.Equal(4, c.X);
            Assert.Equal(-6, c.Y);
        }

        [Fact]
        public void SubTest()
        {
            Vector2 a = new Vector2(1, 2);
            Vector2 b = new Vector2(3, -8);
            Vector2 c = a - b;
            Assert.Equal(-2, c.X);
            Assert.Equal(10, c.Y);
            Vector2 d = b - a;
            Assert.Equal(2, d.X);
            Assert.Equal(-10, d.Y);
        }

        [Fact]
        public void MinMaxTest()
        {
            Vector2 a = new Vector2(4, -5);
            Vector2 b = new Vector2(1, 2);
            Assert.Equal(new Vector2(1, -5), Vector2.Min(a, b));
            Assert.Equal(new Vector2(4, 2), Vector2.Max(a, b));
            Assert.Equal(new Vector2(1, -5), a.Min(b));
            Assert.Equal(new Vector2(4, 2), a.Max(b));

        }
        
        [Fact]
        public void NegTest()
        {
            Vector2 a = new Vector2(1, -2);
            Vector2 b = -a;
            Assert.Equal(-1, b.X);
            Assert.Equal(2, b.Y);
        }

        [Fact]
        public void ScaleTest()
        {
            Vector2 a = new Vector2(3, -2);
            Vector2 b = a * 3;
            Assert.Equal(9, b.X);
            Assert.Equal(-6, b.Y);
            Vector2 c = -4 * a;
            Assert.Equal(-12, c.X);
            Assert.Equal(8, c.Y);
        }

        [Fact]
        public void DivideTest()
        {
            Vector2 a = new Vector2(3, -2);
            Vector2 b = a / 4;
            Assert.Equal(3.0/4, b.X);
            Assert.Equal(-2.0/4, b.Y);
        }

        [Fact]
        public void NotEqualTest()
        {
            Vector2 a = new Vector2(3, -2);
            Vector2 b = new Vector2(3, 0);
            Vector2 c = new Vector2(0, -2);
            Vector2 d = new Vector2(3, -2);
            Assert.True( a !=b);
            Assert.True( a != c);
            Assert.False( a != d);
        }

        [Fact]
        public void EqualTest()
        {
            Vector2 a = new Vector2(3, -2);
            Vector2 b = new Vector2(3, 0);
            Vector2 c = new Vector2(0, -2);
            Vector2 d = new Vector2(3, -2);
            Assert.False( a == b);
            Assert.False( a == c);
            Assert.True( a == d);
        }

        [Fact]
        public void AlmostEqualTest()
        {
            Vector2 a = new Vector2(0.0000001, 0.0000002);
            Vector2 b = new Vector2(0.0000002, 0.0000002); 
            // Test static
            Assert.True( Vector2.AlmostEqual(a, b));
            Assert.False( Vector2.AlmostEqual(a, b, 1E-8));
            // Test x           
            Assert.False( a == b);
            Assert.True( a.AlmostEqual(b));
            Assert.False( a.AlmostEqual(b, 1E-8));
            // Test y
            b = new Vector2(0.0000001, 0.0000003);
            Assert.False( a == b);
            Assert.True( a.AlmostEqual(b));
            Assert.False( a.AlmostEqual(b, 1E-8));
            // Test same
            b = new Vector2(0.0000001, 0.0000002);
            Assert.True( a == b);
            Assert.True( a.AlmostEqual(b));
            Assert.True( a.AlmostEqual(b, 1E-8));
           
        }

        [Fact]
        public void AssignmentTest()
        {
            Vector2 a = new Vector2(3, -2);
            Vector2 b = new Vector2(4, 0);
            b = a;
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
            b.X = 7;
            Assert.Equal(7, b.X);
            Assert.Equal(3, a.X);
        }

    }
}
