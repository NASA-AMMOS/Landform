using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using JPLOPS.MathExtensions;
using MathNet.Numerics.LinearAlgebra;

namespace JPLOPS.ImageFeatures
{
    public class EpipolarTransformDecomposition
    {
        /// <summary>
        /// Enumerate all possible rigid transforms corresponding to an essential matrix.
        /// </summary>
        public static IEnumerable<Matrix<double>> PossibleTransforms(EpipolarMatrix f)
        {
            var F = f.matrix.ToMathNet(dimension: 3).Transpose();
            var svd = F.Svd(computeVectors: true);

            Matrix<double> W = CreateMatrix.Dense<double>(3, 3);
            W[0, 1] = -1;
            W[1, 0] = 1;
            W[2, 2] = 1;

            var tC = svd.U.Column(2);
            var translation = tC.ToXna();
            var honk = svd.W;

            var R1 = svd.U * W * svd.VT;
            var R2 = svd.U * W.Transpose() * svd.VT;

            if (R1.Determinant() < 0) R1 = -R1;
            if (R2.Determinant() < 0) R2 = -R2;

            Func<Matrix<double>, Vector<double>, Matrix<double>> combine = (rot, t) =>
            {
                var res = CreateMatrix.Dense<double>(4, 4);
                res.SetSubMatrix(0, 3, 0, 3, rot);
                res.SetColumn(3, 0, 3, t);
                return res;
            };

            yield return combine(R1, tC);
            yield return combine(R1, -tC);
            yield return combine(R2, tC);
            yield return combine(R2, -tC);
        }

        /// <summary>
        /// Compute the "best" rigid transform corresponding to an essential matrix, where best
        /// means resulting in the most 3D points being in front of both cameras
        /// </summary>
        /// <param name="e">Essential matrix</param>
        /// <param name="modelPoints">Points in model image</param>
        /// <param name="dataPoints">Points in data image</param>
        /// <returns>Matrix from model frame to data frame</returns>
        public static Matrix ExtractTransform(EpipolarMatrix e, Vector2[] modelPoints, Vector2[] dataPoints, out bool[] mask)
        {
            if (modelPoints.Length != dataPoints.Length)
            {
                throw new ArgumentException("Must have equal number of model and data points");
            }
            Matrix bestTransform = new Matrix();
            int bestPositiveDepth = -1;

            mask = null;
            foreach (var mat in PossibleTransforms(e))
            {
                bool[] thisMask = new bool[modelPoints.Length];

                Matrix<double> A = CreateMatrix.Dense<double>(4, 4);

                var pointPos = new[] { modelPoints, dataPoints };
                var projMats = new[] { CreateMatrix.DenseIdentity<double>(4).SubMatrix(0, 3, 0, 4), mat.SubMatrix(0, 3, 0, 4) };
                int positiveDepth = 0;
                for (int i = 0; i < modelPoints.Length; i++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        var pt = pointPos[j][i];
                        var proj = projMats[j];
                        A.SetRow(j * 2 + 0, pt.X * (proj.Row(2) - proj.Row(0)));
                        A.SetRow(j * 2 + 1, pt.Y * (proj.Row(2) - proj.Row(1)));
                    }

                    var aSvd = A.Svd();
                    var dingus = aSvd.S;
                    var homogenous = aSvd.VT.Column(3);
                    homogenous /= homogenous[3];
                    var projModel = projMats[0] * homogenous;
                    var projData = projMats[1] * homogenous;

                    bool keep = (projModel[2] >= 0 && projData[2] >= 0);
                    if (keep)
                    {
                        positiveDepth++;
                        thisMask[i] = true;
                    }
                    else
                    {
                        thisMask[i] = false;
                    }
                }

                if (positiveDepth > bestPositiveDepth)
                {
                    bestPositiveDepth = positiveDepth;
                    bestTransform = mat.Transpose().ToXna();
                    mask = thisMask;
                }
            }

            return bestTransform;
        }
    }
}
