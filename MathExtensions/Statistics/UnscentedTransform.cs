using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.MathExtensions
{
    // See "The Scaled Unscented Transformation"
    // https://www.cs.unc.edu/~welch/kalman/media/pdf/ACC02-IEEE1357.PDF
    public class UnscentedTransform
    {
        /// <summary>
        /// Compute a set of 2n points with mean and covariance equal to <paramref name="distrib"/>
        /// </summary>
        public static IEnumerable<Vector<double>> SigmaPoints(GaussianND distrib, double lambda = 0)
        {
            var mean = distrib.Mean;
            var covariance = distrib.Covariance;

            yield return mean;

            // If covariance is zero, just return the one point
            if (covariance.IsZero())
            {
                yield break;
            }

            Matrix<double> nX = covariance * (covariance.RowCount + lambda);

            // TODO: try using Cholesky instead, see if more go fast (#96)
            var svd = nX.Svd();
            var U = svd.U;
            var VT = svd.VT;
            var sqrtS = svd.S.PointwiseSqrt();
            var sqrtNX = U * CreateMatrix.Diagonal(sqrtS.ToArray()) * VT;

            for (int i = 0; i < sqrtNX.ColumnCount; i++)
            {
                var column = sqrtNX.Column(i);
                yield return mean + column;
                yield return mean - column;
            }
        }

        public delegate Vector<double> UnaryFunctor(Vector<double> input);
        public delegate Vector<double> BinaryFunctor(Vector<double> x, Vector<double> y);

        /// <summary>
        /// Approximate the distribution of <paramref name="func"/>(<paramref name="x"/>) with the unscented transform.
        /// </summary>
        /// <param name="x">Input probablity distribution</param>
        /// <param name="func">Function to apply</param>
        /// <returns>GaussianND over the codomain of <paramref name="func"/></returns>
        public static GaussianND Transform(GaussianND x, UnaryFunctor func, double k=3, double a=0.5)
        {
            double lambda = (a * a) * (x.N + k) - x.N;
            List<Vector<double>> sigmaPoints = SigmaPoints(x, lambda).Select(pt => func(pt)).ToList();
            List<double> meanWeights = new List<double>(sigmaPoints.Count);
            List<double> covarianceWeights = new List<double>(sigmaPoints.Count);

            List<double[]> sigmaPointArrs = sigmaPoints.Select(pt => pt.ToArray()).ToList();

            double firstMeanWeight = lambda / (x.N + lambda);
            meanWeights.Add(firstMeanWeight);
            covarianceWeights.Add(firstMeanWeight + (1 - a * a + 2));
            for (int i = 1; i < sigmaPoints.Count; i++)
            {
                double weight = 1 / (2 * (x.N + lambda));
                meanWeights.Add(weight);
                covarianceWeights.Add(weight);
            }
            return new GaussianND(sigmaPoints, meanWeights, covarianceWeights);
        }

        /// <summary>
        /// Approximate the distribution of <paramref name="func"/>(<paramref name="x"/>, <paramref name="y"/>) with the unscented transform.
        /// </summary>
        /// <param name="x">Input probablity distribution, assumed independent from y</param>
        /// <param name="y">Input probablity distribution, assumed independent from x</param>
        /// <param name="func">Function to apply</param>
        /// <returns>GaussianND over the codomain of <paramref name="func"/></returns>
        public static GaussianND Transform(GaussianND x, GaussianND y, BinaryFunctor func, double k=3, double a=0.5)
        {
            GaussianND joint = GaussianND.IndependentJoint(x, y);
            return Transform(joint, vec =>
            {
                var xVec = vec.SubVector(0, x.N);
                var yVec = vec.SubVector(x.N, y.N);
                return func(xVec, yVec);
            }, k, a);
        }
    }
}
