using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using System;

namespace GeometryTest
{
    [TestClass]
    public class AxisAngleVectorTest
    {
        [TestMethod]
        public void TestAxisAngleVector()
        {
            // Test identity == 0 == identity
            Quaternion identity = Quaternion.Identity;
            AxisAngleVector v = new AxisAngleVector(identity);
            Assert.AreEqual(0, v.Angle);
            Assert.AreEqual(Vector3.Zero, v.AxisAngle);

            Quaternion roundTripped = v.ToQuaternion();
            Assert.AreEqual(identity, roundTripped);

            Matrix asMatrix = v.ToMatrix();
            Assert.AreEqual(Matrix.Identity, asMatrix);

            // rotation of pi/2 around -Z axis
            Matrix testMat = new Matrix(
                0, -1,  0,  0,
                1,  0,  0,  0,
                0,  0,  1,  0,
                0,  0,  0,  1
                );
            AxisAngleVector asVec = new AxisAngleVector(Quaternion.CreateFromRotationMatrix(testMat));
            Assert.AreEqual(Vector3.Forward, asVec.Axis);
            Assert.AreEqual(asVec.Angle, Math.PI / 2);
        }
    }
}
