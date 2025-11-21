using System;
using Xunit;
using Microsoft.Xna.Framework;

namespace XnaTest
{
    public class Vector3Test
    {
        [Fact]
        public void ConstructorTest()
        {
            Vector3 a = new Vector3();
            Assert.Equal(0, a.X);
            Assert.Equal(0, a.Y);
            Assert.Equal(0, a.Z);

            Vector3 b = new Vector3(1, 2, 3);
            Assert.Equal(1, b.X);
            Assert.Equal(2, b.Y);
            Assert.Equal(3, b.Z);

            Vector3 c = new Vector3(b);
            Assert.Equal(1, c.X);
            Assert.Equal(2, c.Y);
            Assert.Equal(3, c.Z);
        }

        [Fact]
        public void SetTest()
        {
            Vector3 a = new Vector3();
            Assert.Equal(0, a.X);
            Assert.Equal(0, a.Y);
            Assert.Equal(0, a.Z);
            a.Set(3, 4, 5);
            Assert.Equal(3, a.X);
            Assert.Equal(4, a.Y);
            Assert.Equal(5, a.Z);
        }

        [Fact]
        public void MagnitudeTest()
        {
            Vector3 a = new Vector3(8, -9, 2);
            Assert.Equal(Math.Sqrt(8 * 8 + 9 * 9 + 2 * 2), a.Length());

        }

        [Fact]
        public void SqrdMagnitudeTest()
        {
            Vector3 a = new Vector3(8, -9, 2);
            Assert.Equal(8 * 8 + 9 * 9 + 2 * 2, a.LengthSquared());

        }

        [Fact]
        public void NormalizeTest()
        {
            Vector3 a = new Vector3(8, -9, 2);
            a.Normalize();
            Assert.True( Math.Abs(1-a.Length()) < 0.0001);

            Vector3 b = new Vector3(1, 2, 3);
            b.Normalize();
            Assert.Equal(b.X, 1.0 / Math.Sqrt(14));
            Assert.Equal(b.Y, 2.0 / Math.Sqrt(14));

            b = new Vector3(1, 2, 3);
            Vector3 c = Vector3.Normalize(b);
            Assert.Equal(1, b.X);
            Assert.Equal(2, b.Y);
            Assert.Equal(c.X, 1.0 / Math.Sqrt(14));
            Assert.Equal(c.Y, 2.0 / Math.Sqrt(14));

            b = new Vector3(1, 2, 3);
            c = new Vector3();
            Vector3.Normalize(ref b, out c);
            Assert.Equal(1, b.X);
            Assert.Equal(2, b.Y);
            Assert.Equal(c.X, 1.0 / Math.Sqrt(14));
            Assert.Equal(c.Y, 2.0 / Math.Sqrt(14));

        }

        [Fact]
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

        [Fact]
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
        [Fact]
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

        [Fact]
        public void NormalizedTest()
        {
            Vector3 a = new Vector3(1, 2, 0);
            Vector3 b = Vector3.Normalize(a);
            Assert.Equal(1.0, a.X);
            Assert.Equal(2.0, a.Y);
            Assert.Equal(0, a.Z);
            Assert.True( Math.Abs(1 - b.LengthSquared()) < 0.0001);
            Assert.Equal(b.X, 1.0 / Math.Sqrt(5));
            Assert.Equal(b.Y, 2.0 / Math.Sqrt(5));
            Assert.Equal(0, b.Z);
        }

        [Fact]
        public void DotTest()
        {
            Vector3 a = new Vector3(2, 4, 7);
            Vector3 b = new Vector3(7, 3, 9);
            double c = a.Dot(b);
            double d = b.Dot(a);
            Assert.Equal(89, c);
            Assert.Equal(89, d);
        }

        [Fact]
        public void CrossTest()
        {
            Vector3 a = new Vector3(2, 4, 7);
            Vector3 b = new Vector3(7, 3, 9);
            Vector3 c = a.Cross(b);
            Vector3 d = b.Cross(a);
            Assert.Equal(new Vector3(15,31,-22), c);
            Assert.Equal(new Vector3(-15, -31, 22), d);
        }


        [Fact]
        public void MinMaxTest()
        {
            Vector3 a = new Vector3(3, 4, -5);
            Vector3 b = new Vector3(8, 1, 2);
            Assert.Equal(new Vector3(3, 1, -5), Vector3.Min(a, b));
            Assert.Equal(new Vector3(8, 4, 2), Vector3.Max(a, b));
            Assert.Equal(new Vector3(3, 1, -5), a.Min(b));
            Assert.Equal(new Vector3(8, 4, 2), a.Max(b));
        }

        [Fact]
        public void AddTest()
        {
            Vector3 a = new Vector3(1, 2,5);
            Vector3 b = new Vector3(3, -8,9);
            Vector3 c = a + b;
            Assert.Equal(4, c.X);
            Assert.Equal(-6, c.Y);
            Assert.Equal(14, c.Z);
        }

        [Fact]
        public void SubTest()
        {
            Vector3 a = new Vector3(1, 2,5);
            Vector3 b = new Vector3(3, -8, 9);

            Vector3 c = a - b;
            Assert.Equal(-2, c.X);
            Assert.Equal(10, c.Y);
            Assert.Equal(-4, c.Z);
            Vector3 d = b - a;
            Assert.Equal(2, d.X);
            Assert.Equal(-10, d.Y);
            Assert.Equal(4, d.Z);
        }

        [Fact]
        public void NegTest()
        {
            Vector3 a = new Vector3(1, -2,8);
            Vector3 b = -a;
            Assert.Equal(-1, b.X);
            Assert.Equal(2, b.Y);
            Assert.Equal(-8, b.Z);
        }

        [Fact]
        public void ScaleTest()
        {
            Vector3 a = new Vector3(3, -2,4);
            Vector3 b = a * 3;
            Assert.Equal(9, b.X);
            Assert.Equal(-6, b.Y);
            Assert.Equal(12, b.Z);
            Vector3 c = -4 * a;
            Assert.Equal(-12, c.X);
            Assert.Equal(8, c.Y);
            Assert.Equal(-16, c.Z);
        }

        [Fact]
        public void DivideTest()
        {
            Vector3 a = new Vector3(3, -2,8);
            Vector3 b = a / 4;
            Assert.Equal(3.0 / 4, b.X);
            Assert.Equal(-2.0 / 4, b.Y);
            Assert.Equal(2.0, b.Z);

        }

        [Fact]
        public void NotEqualTest()
        {
            Vector3 a = new Vector3(3, -2, 1);
            Vector3 b = new Vector3(3, 0,  1);
            Vector3 c = new Vector3(0, -2, 1);
            Vector3 d = new Vector3(3, -2, 1);
            Vector3 e = new Vector3(3, -2, 5);
            Assert.True( a != b);
            Assert.True( a != c);
            Assert.False( a != d);
            Assert.True( a != e);
        }

        [Fact]
        public void EqualTest()
        {
            Vector3 a = new Vector3(3, -2, 1);
            Vector3 b = new Vector3(3, 0, 1);
            Vector3 c = new Vector3(0, -2, 1);
            Vector3 d = new Vector3(3, -2, 1);
            Vector3 e = new Vector3(3, -2, 5);
            Assert.False( a == b);
            Assert.False( a == c);
            Assert.True( a == d);
            Assert.False( a == e);
        }

        [Fact]
        public void AlmostEqualTest()
        {
            Vector3 a = new Vector3(0.0000001, 0.0000002, 0.0000003);
            Vector3 b = new Vector3(0.0000004, 0.0000002, 0.0000003);
            // Test static
            Assert.True( Vector3.AlmostEqual(a, b));
            Assert.False( Vector3.AlmostEqual(a, b, 1E-8));
            // Test x
            Assert.False( a == b);
            Assert.True( a.AlmostEqual(b));
            Assert.False( a.AlmostEqual(b, 1E-8));
            // Test y
            b = new Vector3(0.0000001, 0.0000004, 0.0000003);
            Assert.False( a == b);
            Assert.True( a.AlmostEqual(b));
            Assert.False( a.AlmostEqual(b, 1E-8));
            // Test z
            b = new Vector3(0.0000001, 0.0000002, 0.0000004);
            Assert.False( a == b);
            Assert.True( a.AlmostEqual(b));
            Assert.False( a.AlmostEqual(b, 1E-8));
            // Test same
            b = new Vector3(0.0000001, 0.0000002, 0.0000003);
            Assert.True( a == b);
            Assert.True( a.AlmostEqual(b));
            Assert.True( a.AlmostEqual(b, 1E-8));
        }

        [Fact]
        public void AssignmentTest()
        {
            Vector3 a = new Vector3(3, -2, 5);
            Vector3 b = new Vector3(4, 0, 9);
            b = a;
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
            Assert.Equal(a.Z, b.Z);
            b.X = 7;
            Assert.Equal(7, b.X);
            Assert.Equal(3, a.X);
        }

        [Fact]
        public void RGBTest()
        {
            Vector3 a = new Vector3(3, -2, 5);

            Assert.Equal(3, a.R);
            Assert.Equal(-2, a.G);
            Assert.Equal(5, a.B);
            a.R = 12;
            a.G = 11;
            a.B = 14;
            Assert.Equal(12, a.R);
            Assert.Equal(11, a.G);
            Assert.Equal(14, a.B);


        }
    }
}
