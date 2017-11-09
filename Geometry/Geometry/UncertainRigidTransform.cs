using OPS.MathExtensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;

namespace OPS.Geometry
{
    public class UncertainRigidTransform
    {
        public readonly GaussianND Distribution;

        public bool Uncertain
        {
            get { return !Distribution.Covariance.IsZero(); }
        }

        /// <summary>
        /// Construct from a full 6D probablility distribution.
        /// </summary>
        public UncertainRigidTransform(GaussianND distribution)
        {
            if (distribution.N != 6) throw new ArgumentException("Transform distribution not 6D");
            Distribution = distribution;
        }

        public UncertainRigidTransform(Matrix mean, Matrix<double> covariance)
        {
            var meanVec = ToVector(mean);
            Distribution = new GaussianND(meanVec, covariance);
        }

        /// <summary>
        /// Construct from assumed-independent translation and rotation distributions.
        /// </summary>
        public UncertainRigidTransform(GaussianND translation, GaussianND axisAngle)
        {
            if (translation.N != 3 || axisAngle.N != 3) throw new ArgumentException("Independent translation and rotation distributions must be 3D");
            Distribution = GaussianND.IndependentJoint(translation, axisAngle);
        }

        /// <summary>
        /// Compose two transformations.
        /// </summary>
        public static UncertainRigidTransform operator *(UncertainRigidTransform lhs, UncertainRigidTransform rhs)
        {
            var uberDistrib = GaussianND.IndependentJoint(lhs.Distribution, rhs.Distribution);
            return new UncertainRigidTransform(UnscentedTransform.Transform(uberDistrib, uberVec =>
            {
                var lhsVec = uberVec.SubVector(0, 6);
                var rhsVec = uberVec.SubVector(6, 6);
                return ToVector(ToMatrix(lhsVec) * ToMatrix(rhsVec));
            }));
        }

        /// <summary>
        /// Compose with a known transformation.
        /// </summary>
        public static UncertainRigidTransform operator *(UncertainRigidTransform lhs, Matrix rhs)
        {
            return new UncertainRigidTransform(lhs.Transform(lhsMat =>
            {
                return ToVector(lhsMat * rhs);
            }));
        }

        /// <summary>
        /// Compose with a known transformation.
        /// </summary>
        public static UncertainRigidTransform operator *(Matrix lhs, UncertainRigidTransform rhs)
        {
            return new UncertainRigidTransform(rhs.Transform(rhsMat =>
            {
                return ToVector(lhs * rhsMat);
            }));
        }

        public GaussianND Transform(Func<Matrix, Vector<double>> func)
        {
            return UnscentedTransform.Transform(Distribution, vec =>
            {
                return func(ToMatrix(vec));
            });
        }

        /// <summary>
        /// Compute a probability distribution for the result of transforming a point.
        /// </summary>
        /// <param name="point">Point in 3D space</param>
        /// <returns>Gaussian3D</returns>
        public GaussianND TransformPoint(Vector3 point)
        {
            return Transform(mat =>
            {
                return Vector3.Transform(point, mat).ToMathNet();
            });
        }

        /// <summary>
        /// Compute a probability distribution for the result of transforming a direction.
        /// </summary>
        /// <param name="point">Point in 3D space</param>
        /// <returns>Gaussian3D</returns>
        public GaussianND TransformNormal(Vector3 point)
        {
            // Translation has no effect on vector transformations - just extract the rotation
            var mean = Distribution.Mean.SubVector(3, 3);
            var cov = Distribution.Covariance.SubMatrix(3, 3, 3, 3);
            GaussianND axisAngle = new GaussianND(mean, cov);
            return UnscentedTransform.Transform(axisAngle, rot =>
            {
                return new AxisAngleVector(rot.ToXna()).Transform(point).ToMathNet();
            });
        }

        /// <summary>
        /// Compute the distribution of this transform's inverse.
        /// </summary>
        public UncertainRigidTransform Inverse()
        {
            return new UncertainRigidTransform(Transform(mat => ToVector(Matrix.Invert(mat))));
        }

        /// <summary>
        /// Mean value of this transform as a matrix
        /// </summary>
        public Matrix Mean
        {
            get
            {
                return ToMatrix(Distribution.Mean);
            }
        }

        private static Matrix ToMatrix(Vector<double> vec6)
        {
            return ToMatrix(new Vector3(vec6[0], vec6[1], vec6[2]), new AxisAngleVector(vec6[3], vec6[4], vec6[5]));
        }
        private static Matrix ToMatrix(Vector3 translation, AxisAngleVector axisAngle)
        {
            return axisAngle.ToMatrix() * Matrix.CreateTranslation(translation);
        }
        private static Vector<double> ToVector(Matrix mat)
        {
            Vector3 translation, scale;
            Quaternion rotation;
            mat.Decompose(out scale, out rotation, out translation);
            AxisAngleVector axisAngle = new AxisAngleVector(rotation);
            return CreateVector.Dense<double>(new double[] { translation.X, translation.Y, translation.Y, axisAngle.X, axisAngle.Y, axisAngle.Z });
        }
    }
}
