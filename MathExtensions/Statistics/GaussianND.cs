using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

using Xna = Microsoft.Xna.Framework;

namespace OPS.MathExtensions
{
    public class GaussianND
    {
        public readonly Vector<double> Mean;
        public readonly Matrix<double> Covariance;
        public readonly int N;

        /// <summary>
        /// Mean value as an XNA Vector3.
        /// </summary>
        public Xna.Vector3 XnaMean
        {
            get
            {
                return Mean.ToXna();
            }
        }
        /// <summary>
        /// Covariance matrix as an XNA Matrix.
        /// </summary>
        public Xna.Matrix XnaCovariance
        {
            get
            {
                return Covariance.ToXna();
            }
        }

        /// <summary>
        /// Return a joint distribution of two assumed-independent distributions.
        /// </summary>
        /// <param name="one"></param>
        /// <param name="two"></param>
        /// <returns></returns>
        public static GaussianND IndependentJoint(GaussianND one, GaussianND two)
        {
            int NP = one.N + two.N;
            var mean = new DenseVector(NP);
            var covariance = new SparseMatrix(NP);
            mean.SetSubVector(0, one.N, one.Mean);
            mean.SetSubVector(one.N, two.N, two.Mean);
            covariance.SetSubMatrix(0, 0, one.Covariance);
            covariance.SetSubMatrix(one.N, one.N, two.Covariance);
            return new GaussianND(mean, covariance);
        }

        /// <summary>
        /// Construct with a given mean and covariance.
        /// </summary>
        /// <param name="mean">Mean vector</param>
        /// <param name="covariance">Covariance matrix</param>
        public GaussianND(Vector<double> mean, Matrix<double> covariance)
        {
            if (mean.Count != covariance.RowCount) throw new Exception("Size mismatch");
            Mean = mean;
            Covariance = covariance;
            N = mean.Count;
        }

        /// <summary>
        /// Construct with a given mean and covariance.
        /// </summary>
        /// <param name="mean">Mean vector</param>
        /// <param name="covariance">Covariance matrix</param>
        public GaussianND(Xna.Vector3 mean, Xna.Matrix covariance)
        {
            Mean = mean.ToMathNet();
            Covariance = covariance.ToMathNet(3);
            N = 3;
        }

        /// <summary>
        /// Construct from a set of sampled points
        /// </summary>
        /// <param name="points">Set of points to compute distribution from</param>
        /// <param name="population">If true, `points` is an exhaustive sampling</param>
        public GaussianND(IEnumerable<Vector<double>> points, bool population = false)
        {
            bool first = true;
            List<Vector<double>> myPts = new List<Vector<double>>();
            foreach (var v in points)
            {
                myPts.Add(v);
                if (first)
                {
                    Mean = v;
                    first = false;
                }
                else
                {
                    Mean += v;
                }
            }
            Mean /= myPts.Count;

            Covariance = new DenseMatrix(Mean.Count);
            if (myPts.Count == 1) return;

            for (int i = 0; i < myPts.Count; i++)
            {
                var v = myPts[i];
                var offset = v - Mean;
                for (int j = 0; j < Mean.Count; j++)
                {
                    for (int k = 0; k < Mean.Count; k++)
                    {
                        Covariance[j, k] += offset[j] * offset[k];
                    }
                }
            }
            if (population)
            {
                Covariance *= 1.0 / (myPts.Count);
            }
            else
            {
                Covariance *= 1.0 / (myPts.Count - 1.0);
            }
            N = Mean.Count;
        }
        
        protected Matrix<double> inverseCovariance;
        protected bool haveInverseCovariance = false;
        public Matrix<double> InverseCovariance
        {
            get
            {
                if (!haveInverseCovariance)
                {
                    inverseCovariance = Covariance.Inverse();
                    haveInverseCovariance = true;
                }
                return inverseCovariance;
            }
        }
        public double MahalanobisDistanceSquared(Vector<double> point)
        {
            var meanOffset = point - Mean;
            return meanOffset.DotProduct(InverseCovariance * meanOffset);
        }
        public double MahalanobisDistance(Vector<double> point)
        {
            return Math.Sqrt(MahalanobisDistanceSquared(point));
        }

        public static GaussianND operator+(GaussianND lhs, GaussianND rhs)
        {
            if (lhs.N != rhs.N) throw new InvalidOperationException("Dimension mismatch");
            return new GaussianND(lhs.Mean + rhs.Mean, lhs.Covariance + rhs.Covariance);
        }

        public static GaussianND operator +(GaussianND lhs, Vector<double> rhs)
        {
            if (lhs.N != rhs.Count) throw new InvalidOperationException("Dimension mismatch");
            return new GaussianND(lhs.Mean + rhs, lhs.Covariance);
        }
        public static GaussianND operator +(Vector<double> lhs, GaussianND rhs)
        {
            if (lhs.Count != rhs.N) throw new InvalidOperationException("Dimension mismatch");
            return new GaussianND(lhs + rhs.Mean, rhs.Covariance);
        }

        public static GaussianND operator *(Matrix<double> lhs, GaussianND rhs)
        {
            if (lhs.ColumnCount != rhs.N) throw new InvalidOperationException("Dimension mismatch");
            return new GaussianND(lhs * rhs.Mean, lhs * rhs.Covariance * lhs.Transpose());
        }
    }
}
