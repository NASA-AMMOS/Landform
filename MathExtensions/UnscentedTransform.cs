using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.MathExtensions
{
    public class UnscentedTransform
    {
        /// <summary>
        /// Compute a set of 2n points with mean and covariance equal to <paramref name="distrib"/>
        /// </summary>
        public static IEnumerable<Vector<double>> SigmaPoints(GaussianND distrib)
        {
            var mean = distrib.Mean;
            var covariance = distrib.Covariance;

            // If covariance is zero, just return the one point
            if (covariance.IsZero())
            {
                yield return mean;
                yield break;
            }

            Matrix<double> nX = covariance * covariance.RowCount;
            var svd = nX.Svd();
            var U = svd.U;
            var VT = svd.VT;
            var sqrtS = svd.S.PointwiseSqrt();

            var sqrtNX = U * CreateMatrix.Diagonal<double>(sqrtS.ToArray()) * VT;
            for (int i = 0; i < sqrtNX.ColumnCount; i++)
            {
                var column = sqrtNX.Column(i);
                yield return mean + column;
                yield return mean - column;
            }
        }

        public delegate Vector<double> Functor(Vector<double> input);

        /// <summary>
        /// Approximate the distribution of <paramref name="func"/>(<paramref name="x"/>) with the unscented transform.
        /// </summary>
        /// <param name="x">Input probablity distribution</param>
        /// <param name="func">Function to apply</param>
        /// <returns>GaussianND</returns>
        public static GaussianND Transform(GaussianND x, Functor func)
        {
            return new GaussianND(SigmaPoints(x).Select(pt => func(pt)));
        }
    }
}
