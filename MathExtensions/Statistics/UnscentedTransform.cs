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

        public delegate Vector<double> UnaryFunctor(Vector<double> input);
        public delegate Vector<double> BinaryFunctor(Vector<double> x, Vector<double> y);

        /// <summary>
        /// Approximate the distribution of <paramref name="func"/>(<paramref name="x"/>) with the unscented transform.
        /// </summary>
        /// <param name="x">Input probablity distribution</param>
        /// <param name="func">Function to apply</param>
        /// <returns>GaussianND over the codomain of <paramref name="func"/></returns>
        public static GaussianND Transform(GaussianND x, UnaryFunctor func)
        {
            return new GaussianND(SigmaPoints(x).Select(pt => func(pt)));
        }

        /// <summary>
        /// Approximate the distribution of <paramref name="func"/>(<paramref name="x"/>, <paramref name="y"/>) with the unscented transform.
        /// </summary>
        /// <param name="x">Input probablity distribution, assumed independent from y</param>
        /// <param name="y">Input probablity distribution, assumed independent from x</param>
        /// <param name="func">Function to apply</param>
        /// <returns>GaussianND over the codomain of <paramref name="func"/></returns>
        public static GaussianND Transform(GaussianND x, GaussianND y, BinaryFunctor func)
        {
            GaussianND joint = GaussianND.IndependentJoint(x, y);
            return Transform(joint, vec =>
            {
                var xVec = vec.SubVector(0, x.N);
                var yVec = vec.SubVector(x.N, y.N);
                return func(xVec, yVec);
            });
        }
    }
}
