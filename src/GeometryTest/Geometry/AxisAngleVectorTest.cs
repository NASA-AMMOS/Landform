using Xunit;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using System;

namespace GeometryTest
{
    public class AxisAngleVectorTest
    {
        [Fact]
        public void TestAxisAngleVector()
        {
            // Test identity == 0 == identity
            Quaternion identity = Quaternion.Identity;
            AxisAngleVector v = new AxisAngleVector(identity);
            Assert.Equal(0, v.Angle);
            Assert.Equal(Vector3.Zero, v.AxisAngle);

            Quaternion roundTripped = v.ToQuaternion();
            Assert.Equal(identity, roundTripped);

            Matrix asMatrix = v.ToMatrix();
            Assert.Equal(Matrix.Identity, asMatrix);

            // rotation of pi/2 around -Z axis
            Matrix testMat = new Matrix(
                0, -1,  0,  0,
                1,  0,  0,  0,
                0,  0,  1,  0,
                0,  0,  0,  1
                );
            AxisAngleVector asVec = new AxisAngleVector(Quaternion.CreateFromRotationMatrix(testMat));
            Assert.Equal(Vector3.Forward, asVec.Axis);
            Assert.Equal(Math.PI / 2, asVec.Angle);
        }
    }
}
