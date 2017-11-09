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

        static void Compute(List<Vector<double>> points, List<double> meanWeights, List<double> covarianceWeights, out Vector<double> Mean, out Matrix<double> Covariance)
        {
            Mean = null;
            for (int i = 0; i < points.Count; i++)
            {
                var v = points[i];
                if (i == 0)
                {
                    Mean = v * covarianceWeights[i];
                }
                else
                {
                    Mean += v * covarianceWeights[i];
                }
            }

            Covariance = new DenseMatrix(Mean.Count);
            if (points.Count == 1) return;

            for (int i = 0; i < points.Count; i++)
            {
                var v = points[i];
                var offset = (v - Mean) * covarianceWeights[i];
                for (int j = 0; j < Mean.Count; j++)
                {
                    for (int k = 0; k < Mean.Count; k++)
                    {
                        Covariance[j, k] += offset[j] * offset[k];
                    }
                }
            }
        }

        /// <summary>
        /// Compute from a set of samples and associated weights
        /// </summary>
        /// <param name="points">Sample points</param>
        /// <param name="meanWeights">Per-sample weight for computing mean (typically 1/N)</param>
        /// <param name="covarianceWeights">Per-sample weight for computing covariance (typically 1/(N-1))</param>
        public GaussianND(List<Vector<double>> points, List<double> meanWeights, List<double> covarianceWeights)
        {
            Compute(points, meanWeights, covarianceWeights, out Mean, out Covariance);
            N = Mean.Count;
        }

        /// <summary>
        /// Construct from a set of sampled points
        /// </summary>
        /// <param name="points">Set of points to compute distribution from</param>
        /// <param name="population">If true, `points` is an exhaustive sampling</param>
        public GaussianND(IEnumerable<Vector<double>> points, bool population = false)
        {
            List<Vector<double>> pointList = points.ToList();
            double meanWeight = 1.0 / pointList.Count;
            double covarianceWeight = population ? (1.0 / pointList.Count) : (1.0 / (pointList.Count - 1));
            List<double> meanWeights = Enumerable.Repeat(meanWeight, pointList.Count).ToList();
            List<double> covarianceWeights = Enumerable.Repeat(covarianceWeight, pointList.Count).ToList();
            Compute(pointList, meanWeights, covarianceWeights, out Mean, out Covariance);
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
