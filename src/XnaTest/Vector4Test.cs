using System;
using Xunit;
using Microsoft.Xna.Framework;

namespace XnaTest
{
    public class Vector4Test
    {
        [Fact]
        public void ConstructorTest()
        {
            Vector4 b = new Vector4(1, 2, 3,4);
            Assert.Equal(1, b.X);
            Assert.Equal(2, b.Y);
            Assert.Equal(3, b.Z);
            Assert.Equal(3, b.Z);

            Vector4 c = new Vector4(b);
            Assert.Equal(1, c.X);
            Assert.Equal(2, c.Y);
            Assert.Equal(3, c.Z);
            Assert.Equal(4, c.W);
        }

        [Fact]
        public void SetTest()
        {
            Vector4 a = new Vector4();
            Assert.Equal(0, a.X);
            Assert.Equal(0, a.Y);
            Assert.Equal(0, a.Z);
            Assert.Equal(0, a.W);
            a.Set(3, 4, 5,6);
            Assert.Equal(3, a.X);
            Assert.Equal(4, a.Y);
            Assert.Equal(5, a.Z);
            Assert.Equal(6, a.W);
        }

   
        [Fact]
        public void NormalizeTest()
        {
            Vector4 a = new Vector4(8, -9, 2, 2);
            a.Normalize();
            Assert.True( Math.Abs(1-a.Length()) < 0.0001);

            Vector4 b = new Vector4(1, 2, 3, 2);
            b.Normalize();
            Assert.Equal(b.X, 1.0 / Math.Sqrt(18));
            Assert.Equal(b.Y, 2.0 / Math.Sqrt(18));

            b = new Vector4(1, 2, 3, 2);
            Vector4 c = Vector4.Normalize(b);
            Assert.Equal(1, b.X);
            Assert.Equal(2, b.Y);
            Assert.Equal(c.X, 1.0 / Math.Sqrt(18));
            Assert.Equal(c.Y, 2.0 / Math.Sqrt(18));

            b = new Vector4(1, 2, 3,2);
            c = new Vector4();
            Vector4.Normalize(ref b, out c);
            Assert.Equal(1, b.X);
            Assert.Equal(2, b.Y);
            Assert.Equal(c.X, 1.0 / Math.Sqrt(18));
            Assert.Equal(c.Y, 2.0 / Math.Sqrt(18));

        }

        [Fact]
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

        [Fact]
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
        [Fact]
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

       

        [Fact]
        public void MinMaxTest()
        {
            Vector4 a = new Vector4(3, 4, -5, 7);
            Vector4 b = new Vector4(8, 1, 2,9);
            Assert.Equal(new Vector4(3, 1, -5,7), Vector4.Min(a, b));
            Assert.Equal(new Vector4(8, 4, 2, 9), Vector4.Max(a, b));
            Assert.Equal(new Vector4(3, 1, -5,7), a.Min(b));
            Assert.Equal(new Vector4(8, 4, 2, 9), a.Max(b));
        }

      
        [Fact]
        public void AlmostEqualTest()
        {
            Vector4 a = new Vector4(0.0000001, 0.0000002, 0.0000003, 0.0000004);
            Vector4 b = new Vector4(0.0000005, 0.0000002, 0.0000003, 0.0000004);
            // Test static
            Assert.True( Vector4.AlmostEqual(a, b));
            Assert.False( Vector4.AlmostEqual(a, b, 1E-8));
            // Test x
            Assert.False( a == b);
            Assert.True( a.AlmostEqual(b));
            Assert.False( a.AlmostEqual(b, 1E-8));
            // Test y
            b = new Vector4(0.0000001, 0.0000005, 0.0000003, 0.0000004);
            Assert.False( a == b);
            Assert.True( a.AlmostEqual(b));
            Assert.False( a.AlmostEqual(b, 1E-8));
            // Test z
            b = new Vector4(0.0000001, 0.0000002, 0.0000005, 0.0000004);
            Assert.False( a == b);
            Assert.True( a.AlmostEqual(b));
            Assert.False( a.AlmostEqual(b, 1E-8));
            // Test w
            b = new Vector4(0.0000001, 0.0000002, 0.0000004, 0.0000005);
            Assert.False( a == b);
            Assert.True( a.AlmostEqual(b));
            Assert.False( a.AlmostEqual(b, 1E-8));
            // Test same
            b = new Vector4(0.0000001, 0.0000002, 0.0000003, 0.0000004);
            Assert.True( a == b);
            Assert.True( a.AlmostEqual(b));
            Assert.True( a.AlmostEqual(b, 1E-8));
        }

    }
}
