using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using JPLOPS.MathExtensions;
using JPLOPS.Test;

namespace MathExtensionsTest
{
    [TestClass]
    public class MathETest
    {
        [TestMethod]
        public void TestClamp()
        {
            Assert.AreEqual(4, MathE.Clamp((byte)3, (byte)4, (byte)5));
            Assert.AreEqual(5, MathE.Clamp((byte)7, (byte)4, (byte)5));

            Assert.AreEqual(-4, MathE.Clamp((int)-5, (int)-4, (int)5));
            Assert.AreEqual(5, MathE.Clamp((int)7, (int)4, (int)5));

            Assert.AreEqual(-4, MathE.Clamp((long)-5, (long)-4, (long)5));
            Assert.AreEqual(5, MathE.Clamp((long)7, (long)4, (long)5));

            Assert.AreEqual(-4, MathE.Clamp((float)-5, (float)-4, (float)5));
            Assert.AreEqual(5, MathE.Clamp((float)7, (float)4, (float)5));

            Assert.AreEqual(-4, MathE.Clamp((double)-5, (double)-4, (double)5));
            Assert.AreEqual(5, MathE.Clamp((double)7, (double)4, (double)5));
        }

        [TestMethod]
        public void TestClamp01()
        {
            Assert.AreEqual(1, MathE.Clamp01(5));
            Assert.AreEqual(0, MathE.Clamp01(-2));
            Assert.AreEqual(1, MathE.Clamp01(1.03));
            Assert.AreEqual(0.3, MathE.Clamp01(0.3));
            Assert.AreEqual(0.3f, MathE.Clamp01(0.3f));
        }

        [TestMethod]
        public void TestLerp()
        {
            Assert.AreEqual(2, MathE.Lerp(1f, 3f, 0.5f));
            AssertE.AreSimilar(17.4, MathE.Lerp(7f, 20f, 0.8f), 0.00001);
            AssertE.AreSimilar(9.6, MathE.Lerp(20f, 7f, 0.8f), 0.00001);
            AssertE.AreSimilar(-1.6, MathE.Lerp(20f, -7f, 0.8f), 0.00001);
            AssertE.AreSimilar(-16.1, MathE.Lerp(-20f, -7f, 0.3f), 0.00001);

            Assert.AreEqual(2, MathE.Lerp(1d, 3d, 0.5d));
            AssertE.AreSimilar(17.4, MathE.Lerp(7d, 20d, 0.8d), 0.00001);
            AssertE.AreSimilar(9.6, MathE.Lerp(20d, 7d, 0.8d), 0.00001);
            AssertE.AreSimilar(-1.6, MathE.Lerp(20d, -7d, 0.8d), 0.00001);
            AssertE.AreSimilar(-16.1, MathE.Lerp(-20d, -7d, 0.3d), 0.00001);
        }

        [TestMethod]
        public void TestMinMaxArray()
        {
            Assert.AreEqual(-7, MathE.Min(new int[] { -3, 21, 4, -7, 2, 0 }));
            Assert.AreEqual(21, MathE.Max(new int[] { -3, 21, 4, -7, 2, 0 }));
            Assert.AreEqual(-7, MathE.Min(new float[] { -3, 21, 4, -7, 2, 0 }));
            Assert.AreEqual(21, MathE.Max(new float[] { -3, 21, 4, -7, 2, 0 }));
            Assert.AreEqual(-7, MathE.Min(new double[] { -3, 21, 4, -7, 2, 0 }));
            Assert.AreEqual(21, MathE.Max(new double[] { -3, 21, 4, -7, 2, 0 }));
        }

        [TestMethod]
        public void TestFloorPowerOf2()
        {
            try
            {
                Assert.AreEqual(0, MathE.FloorPowerOf2(-1));
                Assert.Fail();
            } catch
            {

            }
            Assert.AreEqual(0, MathE.FloorPowerOf2(0));
            Assert.AreEqual(0, MathE.FloorPowerOf2(0.5));
            Assert.AreEqual(1, MathE.FloorPowerOf2(1));
            Assert.AreEqual(1, MathE.FloorPowerOf2(1.2));
            Assert.AreEqual(1, MathE.FloorPowerOf2(1.9));
            Assert.AreEqual(2, MathE.FloorPowerOf2(2));
            Assert.AreEqual(2, MathE.FloorPowerOf2(3));
            Assert.AreEqual(2, MathE.FloorPowerOf2(3.9));
            Assert.AreEqual(4, MathE.FloorPowerOf2(4));
            Assert.AreEqual(4, MathE.FloorPowerOf2(5));
            Assert.AreEqual(4, MathE.FloorPowerOf2(6));
            Assert.AreEqual(4, MathE.FloorPowerOf2(7));
            Assert.AreEqual(8, MathE.FloorPowerOf2(8));
            Assert.AreEqual(Int16.MaxValue+1, MathE.FloorPowerOf2(Int16.MaxValue + 10.0));
            Assert.AreEqual((Int32.MaxValue + 1.0) / 2, MathE.FloorPowerOf2(Int32.MaxValue));
            try
            {
                Assert.AreEqual(0, MathE.FloorPowerOf2(Int32.MaxValue + 1.0));
                Assert.Fail();
            }
            catch
            {

            }
        }

        [TestMethod]
        public void TestCeilPowerOf2()
        {
            try
            {
                Assert.AreEqual(0, MathE.FloorPowerOf2(-1));
                Assert.Fail();
            }
            catch
            {

            }
            Assert.AreEqual(0, MathE.CeilPowerOf2(0));
            Assert.AreEqual(1, MathE.CeilPowerOf2(0.5));
            Assert.AreEqual(1, MathE.CeilPowerOf2(1));
            Assert.AreEqual(2, MathE.CeilPowerOf2(1.2));
            Assert.AreEqual(2, MathE.CeilPowerOf2(1.9));
            Assert.AreEqual(2, MathE.CeilPowerOf2(2));
            Assert.AreEqual(4, MathE.CeilPowerOf2(3));
            Assert.AreEqual(4, MathE.CeilPowerOf2(3.9));
            Assert.AreEqual(4, MathE.CeilPowerOf2(4));
            Assert.AreEqual(8, MathE.CeilPowerOf2(5));
            Assert.AreEqual(8, MathE.CeilPowerOf2(6));
            Assert.AreEqual(8, MathE.CeilPowerOf2(7));
            Assert.AreEqual(8, MathE.CeilPowerOf2(8));
            Assert.AreEqual((Int16.MaxValue + 1) * 2, MathE.CeilPowerOf2(Int16.MaxValue + 10.0));
            Assert.AreEqual((Int32.MaxValue / 2) + 1, MathE.CeilPowerOf2(Int32.MaxValue / 2));
            try
            {
                Assert.AreEqual(0, MathE.CeilPowerOf2(Int32.MaxValue/2 + 1));
                Assert.Fail();
            }
            catch
            {

            }
        }

        [TestMethod]
        public void TestToYawPitchRoll()
        {
            double y = 0, p = 0, r = 0;
            var q = Quaternion.CreateFromYawPitchRoll(y, p, r);
            var ypr = q.ToYawPitchRoll();
            AssertE.AreSimilar(y, ypr.X);
            AssertE.AreSimilar(p, ypr.Y);
            AssertE.AreSimilar(r, ypr.Z);
            
            y = 0.5 * Math.PI; p = 0; r = 0;
            q = Quaternion.CreateFromYawPitchRoll(y, p, r);
            ypr = q.ToYawPitchRoll();
            AssertE.AreSimilar(y, ypr.X);
            AssertE.AreSimilar(p, ypr.Y);
            AssertE.AreSimilar(r, ypr.Z);
            
            y = 0; p = 0.5 * Math.PI; r = 0;
            q = Quaternion.CreateFromYawPitchRoll(y, p, r);
            ypr = q.ToYawPitchRoll();
            AssertE.AreSimilar(y, ypr.X);
            AssertE.AreSimilar(p, ypr.Y);
            AssertE.AreSimilar(r, ypr.Z);
            
            y = 0; p = 0; r = 0.5 * Math.PI;
            q = Quaternion.CreateFromYawPitchRoll(y, p, r);
            ypr = q.ToYawPitchRoll();
            AssertE.AreSimilar(y, ypr.X);
            AssertE.AreSimilar(p, ypr.Y);
            AssertE.AreSimilar(r, ypr.Z);
            
            y = 0.1; p = 0.2; r = 0.3;
            q = Quaternion.CreateFromYawPitchRoll(y, p, r);
            ypr = q.ToYawPitchRoll();
            AssertE.AreSimilar(y, ypr.X);
            AssertE.AreSimilar(p, ypr.Y);
            AssertE.AreSimilar(r, ypr.Z);

            y = -0.1; p = -0.2; r = -0.3;
            q = Quaternion.CreateFromYawPitchRoll(y, p, r);
            ypr = q.ToYawPitchRoll();
            AssertE.AreSimilar(y, ypr.X);
            AssertE.AreSimilar(p, ypr.Y);
            AssertE.AreSimilar(r, ypr.Z);
            
            y = -0.1; p = 0.2; r = -0.3;
            q = Quaternion.CreateFromYawPitchRoll(y, p, r);
            ypr = q.ToYawPitchRoll();
            AssertE.AreSimilar(y, ypr.X);
            AssertE.AreSimilar(p, ypr.Y);
            AssertE.AreSimilar(r, ypr.Z);
            
            y = 0.1; p = -0.2; r = 0.3;
            q = Quaternion.CreateFromYawPitchRoll(y, p, r);
            ypr = q.ToYawPitchRoll();
            AssertE.AreSimilar(y, ypr.X);
            AssertE.AreSimilar(p, ypr.Y);
            AssertE.AreSimilar(r, ypr.Z);
        }

        [TestMethod]
        public void TestToRotationVector()
        {
            Vector3 axis = new Vector3(1, 0, 0);
            double angle = 0;
            var q = Quaternion.CreateFromAxisAngle(axis, angle);
            var r = q.ToRotationVector();
            double a = r.Length();
            if (Vector3.Dot(axis, r) < 0)
            {
                r = -r;
                a = -a;
            }
            AssertE.AreSimilar(angle, a);
            if (a > 1e-9)
            {
                r = Vector3.Normalize(r);
                AssertE.AreSimilar(axis.X, r.X);
                AssertE.AreSimilar(axis.Y, r.Y);
                AssertE.AreSimilar(axis.Z, r.Z);
            }

            axis = new Vector3(1, 0, 0);
            angle = 0.5;
            q = Quaternion.CreateFromAxisAngle(axis, angle);
            r = q.ToRotationVector();
            a = r.Length();
            if (Vector3.Dot(axis, r) < 0)
            {
                r = -r;
                a = -a;
            }
            AssertE.AreSimilar(angle, a);
            if (a > 1e-9)
            {
                r = Vector3.Normalize(r);
                AssertE.AreSimilar(axis.X, r.X);
                AssertE.AreSimilar(axis.Y, r.Y);
                AssertE.AreSimilar(axis.Z, r.Z);
            }

            axis = new Vector3(1, 0, 0);
            angle = -0.1;
            q = Quaternion.CreateFromAxisAngle(axis, angle);
            r = q.ToRotationVector();
            a = r.Length();
            if (Vector3.Dot(axis, r) < 0)
            {
                r = -r;
                a = -a;
            }
            AssertE.AreSimilar(angle, a);
            if (a > 1e-9)
            {
                r = Vector3.Normalize(r);
                AssertE.AreSimilar(axis.X, r.X);
                AssertE.AreSimilar(axis.Y, r.Y);
                AssertE.AreSimilar(axis.Z, r.Z);
            }

            axis = new Vector3(0, 1, 0);
            angle = 0.1;
            q = Quaternion.CreateFromAxisAngle(axis, angle);
            r = q.ToRotationVector();
            a = r.Length();
            if (Vector3.Dot(axis, r) < 0)
            {
                r = -r;
                a = -a;
            }
            AssertE.AreSimilar(angle, a);
            if (a > 1e-9)
            {
                r = Vector3.Normalize(r);
                AssertE.AreSimilar(axis.X, r.X);
                AssertE.AreSimilar(axis.Y, r.Y);
                AssertE.AreSimilar(axis.Z, r.Z);
            }

            axis = Vector3.Normalize(new Vector3(1, 2, 3));
            angle = 4;
            q = Quaternion.CreateFromAxisAngle(axis, angle);
            r = q.ToRotationVector();
            a = r.Length();
            if (Vector3.Dot(axis, r) < 0)
            {
                r = -r;
                a = -a;
            }
            AssertE.AreSimilar(angle, a);
            if (a > 1e-9)
            {
                r = Vector3.Normalize(r);
                AssertE.AreSimilar(axis.X, r.X);
                AssertE.AreSimilar(axis.Y, r.Y);
                AssertE.AreSimilar(axis.Z, r.Z);
            }
        }
    }
}
