using System;
using Microsoft.Xna.Framework;
using MathNet.Numerics.LinearAlgebra;
using JPLOPS.MathExtensions;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// A probability distribution for a 3D rigid transformation.
    /// </summary>
    public class UncertainRigidTransform
    {
        /// <summary>
        /// Underlying 6D normal distribution.
        /// </summary>
        /// <remarks>
        /// The transformation is parameterized as a 6D vector with the following interpretation:
        /// [t_x, t_y, t_z, r_x, r_y, r_z] 
        /// t_[xyz]: Translation
        /// r_[xyz]: Rotation as <see cref="AxisAngleVector"/> 
        /// 
        /// Rotation is applied before translation.
        /// </remarks>
        public readonly GaussianND Distribution;

        /// <summary>
        /// If False, this transform is an exact value.
        /// </summary>
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

        /// <summary>
        /// Construct from a known mean transform and covariance.
        /// </summary>
        /// <param name="mean">Mean transform as a 6D vector, omit or null for identity</param>
        /// <param name="covariance">6D parameter covariance matrix, omit or null for no uncertainty</param>
        public UncertainRigidTransform(Vector<double> mean = null, Matrix<double> covariance = null)
        {
            Distribution = new GaussianND(mean ?? CreateVector.Dense<double>(6),
                                          covariance ?? CreateMatrix.Dense<double>(6, 6));
        }

        /// <summary>
        /// Construct from a known mean transform and covariance.
        /// </summary>
        /// <param name="mean">Mean transform</param>
        /// <param name="covariance">6D parameter covariance matrix, omit or null for no uncertainty</param>
        public UncertainRigidTransform(Matrix mean, Matrix<double> covariance = null) : this(ToVector(mean), covariance)
        { }

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
            // These cases would produce the same results using UnscentedTransform, but are
            // short-circuited here for performance.
            if (!lhs.Uncertain && !rhs.Uncertain)
            {
                return new UncertainRigidTransform(lhs.Mean * rhs.Mean);
            }
            else if (!rhs.Uncertain)
            {
                return lhs * rhs.Mean;
            }
            else if (!lhs.Uncertain)
            {
                return lhs.Mean * rhs;
            }

            return new UncertainRigidTransform(MathExtensions.UnscentedTransform.Transform(lhs.Distribution, rhs.Distribution, (Vector<double> lhsVec, Vector<double> rhsVec) =>
            {
                return ToVector(ToMatrix(lhsVec) * ToMatrix(rhsVec));
            }));
        }

        /// <summary>
        /// Compose with a known transformation.
        /// </summary>
        public static UncertainRigidTransform operator *(UncertainRigidTransform lhs, Matrix rhs)
        {
            return new UncertainRigidTransform(lhs.UnscentedTransform(lhsMat =>
            {
                return ToVector(lhsMat * rhs);
            }));
        }

        /// <summary>
        /// Compose with a known transformation.
        /// </summary>
        public static UncertainRigidTransform operator *(Matrix lhs, UncertainRigidTransform rhs)
        {
            return new UncertainRigidTransform(rhs.UnscentedTransform(rhsMat =>
            {
                return ToVector(lhs * rhsMat);
            }));
        }

        /// <summary>
        /// <see cref="MathExtensions.UnscentedTransform"/> for functions taking a matrix.
        /// </summary>
        public GaussianND UnscentedTransform(Func<Matrix, Vector<double>> func, double k = 3.0, double a = 0.5)
        {
            return MathExtensions.UnscentedTransform.Transform(Distribution, (Vector<double> vec) =>
            {
                return func(ToMatrix(vec));
            }, k, a);
        }

        /// <summary>
        /// Compute a probability distribution for the result of transforming a point.
        /// </summary>
        /// <param name="point">Point in 3D space</param>
        /// <returns>Gaussian3D</returns>
        public GaussianND TransformPoint(Vector3 point)
        {
            return UnscentedTransform(mat =>
            {
                return Vector3.Transform(point, mat).ToMathNet();
            });
        }


        /// <summary>
        /// Compute a probability distribution for the result of transforming an uncertain point.
        /// </summary>
        /// <param name="point">Point distribution in 3D space</param>
        /// <returns>Gaussian3D</returns>
        public GaussianND TransformPoint(GaussianND point)
        {
            return MathExtensions.UnscentedTransform.Transform(Distribution, point, (Vector<double> poseVec, Vector<double> pointVec) =>
            {
                return Vector3.Transform(new Vector3(pointVec.ToArray()), ToMatrix(poseVec)).ToMathNet();
            });
        }

        /// <summary>
        /// Compute the distribution of this transform's inverse.
        /// </summary>
        [System.Obsolete("Use TimesInverse unless you really mean this.")]
        public UncertainRigidTransform Inverse()
        {
            return new UncertainRigidTransform(UnscentedTransform(mat => ToVector(Matrix.Invert(mat))));
        }

        /// <summary>
        /// Compute the distribution of this right-multiplied by the inverse of another transform.
        /// </summary>
        public UncertainRigidTransform TimesInverse(UncertainRigidTransform rhs)
        {
            var distrib = MathExtensions.UnscentedTransform.Transform(Distribution, rhs.Distribution, (lhsVec, rhsVec) =>
            {
                return ToVector(ToMatrix(lhsVec) * Matrix.Invert(ToMatrix(rhsVec)));
            });
            return new UncertainRigidTransform(distrib);
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

        public Vector3 MeanTranslation
        {
            get
            {
                var vec6 = Distribution.Mean;
                return new Vector3(vec6[0], vec6[1], vec6[2]);
            }
        }

        public Quaternion MeanRotation
        {
            get
            {
                var vec6 = Distribution.Mean;
                return new AxisAngleVector(vec6[3], vec6[4], vec6[5]).ToQuaternion();
            }
        }

        // Helper vector <-> matrix functions

        private static Matrix ToMatrix(Vector3 translation, AxisAngleVector axisAngle)
        {
            return axisAngle.ToMatrix() * Matrix.CreateTranslation(translation);
        }
        public static Matrix ToMatrix(Vector<double> vec6)
        {
            return ToMatrix(new Vector3(vec6[0], vec6[1], vec6[2]), new AxisAngleVector(vec6[3], vec6[4], vec6[5]));
        }
        public static Vector<double> ToVector(Matrix mat)
        {
            Vector3 translation, scale;
            Quaternion rotation;
            mat.Decompose(out scale, out rotation, out translation);
            AxisAngleVector axisAngle = new AxisAngleVector(rotation);
            return CreateVector.Dense<double>(new double[] { translation.X, translation.Y, translation.Z, axisAngle.X, axisAngle.Y, axisAngle.Z });
        }

        public override string ToString()
        {
            return Distribution.ToString();
        }

        public override int GetHashCode()
        {
            return Distribution.GetHashCode();
        }
        
        public override bool Equals(object obj)
        {
            if (!(obj is UncertainRigidTransform))
            {
                return false;
            }
            var other = (UncertainRigidTransform)obj;
            return Distribution.Equals(other.Distribution);
        }
        
        public static bool operator ==(UncertainRigidTransform lhs, UncertainRigidTransform rhs)
        {
            return ReferenceEquals(lhs, rhs) || (!ReferenceEquals(lhs, null) && lhs.Equals(rhs));
        }

        public static bool operator !=(UncertainRigidTransform lhs, UncertainRigidTransform rhs)
        {
            return !(lhs == rhs);
        }
    }
}
