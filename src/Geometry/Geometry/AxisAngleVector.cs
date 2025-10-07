using System;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;


namespace JPLOPS.Geometry
{
    /// <summary>
    /// Represents a 3D rotation as an axis of rotation multiplied by an angle.
    /// </summary>
    public struct AxisAngleVector
    {
        public Vector3 AxisAngle;

        public AxisAngleVector(Vector3 axisAngle)
        {
            this.AxisAngle = axisAngle;
        }
        public AxisAngleVector(double x, double y, double z)
        {
            this.AxisAngle = new Vector3(x, y, z);
        }
        public AxisAngleVector(Vector3 axis, double angle)
        {
            this.AxisAngle = axis * angle / axis.Length();
        }
        public AxisAngleVector(Quaternion rotation)
        {
            double angle = 2 * Math.Acos(rotation.W);
            if (angle < 1e-7)
            {
                this.AxisAngle = Vector3.Zero;
            }
            else
            {
                this.AxisAngle = Vector3.Normalize(new Vector3(rotation.X, rotation.Y, rotation.Z)) * angle;
            }
        }

        [JsonIgnore]
        public Vector3 Axis
        {
            get
            {
                if (AxisAngle.LengthSquared() < 1e-7) return Vector3.Zero;
                return AxisAngle / AxisAngle.Length();
            }
        }

        [JsonIgnore]
        public double Angle
        {
            get
            {
                return AxisAngle.Length();
            }
        }

        public double this[int idx]
        {
            get
            {
                switch (idx)
                {
                    case 0: return AxisAngle.X;
                    case 1: return AxisAngle.Y;
                    case 2: return AxisAngle.Z;
                    default: throw new IndexOutOfRangeException();
                }
            }
            set
            {
                switch (idx)
                {
                    case 0: AxisAngle.X = value; break;
                    case 1: AxisAngle.Y = value; break;
                    case 2: AxisAngle.Z = value; break;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }

        public double X
        {
            get { return AxisAngle.X; }
            set { AxisAngle.X = value; }
        }

        public double Y
        {
            get { return AxisAngle.Y; }
            set { AxisAngle.Y = value; }
        }

        public double Z
        {
            get { return AxisAngle.Z; }
            set { AxisAngle.Z = value; }
        }

        public Quaternion ToQuaternion()
        {
            return Quaternion.CreateFromAxisAngle(Axis, Angle);
        }

        public Matrix ToMatrix()
        {
            return Matrix.CreateFromAxisAngle(Axis, Angle);
        }

        public Vector3 Transform(Vector3 point)
        {
            return Vector3.Transform(point, ToQuaternion());
        }

        /// <summary>
        /// Compute the jacobian matrix for a point transformed by this vector.
        /// </summary>
        /// <param name="point">Point to compute jacobian at</param>
        public Matrix Jacobian(Vector3 point)
        {
            // \theta = \sqrt{r_x^2 + r_y^2 + r_z^2}
            // \forall a \in\{x,y,z\} :
            // u_{a} = \frac{r_{a}}{\theta}
            var p = point;
            var r = AxisAngle;
            var u = Axis;
            var theta = Angle;


            double cosTheta = Math.Cos(theta);
            double sinTheta = Math.Sin(theta);
            var thetaSquared = (theta * theta);

            // \newcommand{\pder}[2]{\frac{\partial#1}{\partial#2}}
            // \pder{\theta}{r_{a}} = \frac{r_{a}}{\theta}
            // \pder{}{r_{a}}\sin(\theta) = \pder{\theta}{r_{a}} \cos(\theta)
            // \pder{}{r_{a}}\cos(\theta) = -\pder{\theta}{r_{a}} \sin(\theta)
            Vector3 dTheta_dR = r / theta;
            Vector3 dSinTheta_dR = dTheta_dR * cosTheta;
            Vector3 dCosTheta_dR = -dTheta_dR * sinTheta;

            // u_{a} = \frac{r_{a}}{\theta}
            // \pder{u_{a}}{r_{a}} = \frac{\theta - u_{a} \pder{\theta}{r_{a}}}{\theta^2}
            // \pder{u_{a}}{r_{b \ne a}} = -\frac{u_{a} \pder{\theta}{r_{b}}}{\theta^2}
            Vector3 dU_dRX = new Vector3(
                (theta - r.X * dTheta_dR.X) / thetaSquared,
                -(r.X * dTheta_dR.Y) / thetaSquared,
                -(r.X * dTheta_dR.Z) / thetaSquared
                );
            Vector3 dU_dRY = new Vector3(
                -(r.Y * dTheta_dR.X) / thetaSquared,
                (theta - r.Y * dTheta_dR.Y) / thetaSquared,
                -(r.Y * dTheta_dR.Z) / thetaSquared
                );
            Vector3 dU_dRZ = new Vector3(
                -(r.Z * dTheta_dR.X) / thetaSquared,
                -(r.Z * dTheta_dR.Y) / thetaSquared,
                (theta - r.Z * dTheta_dR.Z) / thetaSquared
                );

            // Rodrigrues rotation formula:
            // p\prime = p \cos{\theta} + (p \times u)\sin{\theta} + p(p \cdot u)(1 - \cos{\theta})
            // \alpha = \cos{\theta} + (p \cdot u)(1 - \cos{\theta})
            // p\prime = p \alpha + (p \times u)\sin{\theta}
            Vector3 pCrossU = Vector3.Cross(p, u);
            Vector3 dPCrossU_dRX = Vector3.Cross(new Vector3(1, 0, 0), u) + Vector3.Cross(p, dU_dRX);
            Vector3 dPCrossU_dRY = Vector3.Cross(new Vector3(0, 1, 0), u) + Vector3.Cross(p, dU_dRY);
            Vector3 dPCrossU_dRZ = Vector3.Cross(new Vector3(0, 0, 1), u) + Vector3.Cross(p, dU_dRZ);

            double pDotU = Vector3.Dot(p, u);
            Vector3 dPDotU_dR = new Vector3(
                Vector3.Dot(p, dU_dRX),
                Vector3.Dot(p, dU_dRY),
                Vector3.Dot(p, dU_dRZ)
                );

            double alpha = cosTheta + pDotU * (1 - cosTheta);
            Vector3 dAlpha_dR = dCosTheta_dR + dPDotU_dR * (1 - cosTheta) - dCosTheta_dR * pDotU;

            // \pder{p\prime}{r} = \pder{p}{r} \alpha + \pder{alpha}{r} p + \pder{}{r}(p \times u) \sin{\theta} + (p \times u)\pder{}{r}\sin{\theta}
            // = \pder{alpha}{r} p + \pder{}{r}(p \times u) \sin{\theta} + (p \times u)\pder{}{r}\sin{\theta}
            Vector3 dP_dRX = dAlpha_dR.X * p + dPCrossU_dRX * sinTheta + pCrossU * dSinTheta_dR.X;
            Vector3 dP_dRY = dAlpha_dR.Y * p + dPCrossU_dRY * sinTheta + pCrossU * dSinTheta_dR.Y;
            Vector3 dP_dRZ = dAlpha_dR.Z * p + dPCrossU_dRZ * sinTheta + pCrossU * dSinTheta_dR.Z;

            Matrix res = Matrix.Identity * 0;
            for (int i = 0; i < 3; i++)
            {
                res[i, 0] = dP_dRX[i];
                res[i, 1] = dP_dRY[i];
                res[i, 2] = dP_dRZ[i];
            }
            return res;
        }

        public static AxisAngleVector Zero
        {
            get { return new AxisAngleVector(Vector3.Zero); }
        }
    }
}
