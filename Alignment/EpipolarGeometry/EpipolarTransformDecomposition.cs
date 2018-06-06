using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.MathExtensions;
using MathNet.Numerics.LinearAlgebra;

namespace OPS.Alignment
{
    public class EpipolarTransformDecomposition
    {
        /// <summary>
        /// Enumerate all possible rigid transforms corresponding to a fundamental matrix.
        /// </summary>
        public static IEnumerable<Matrix<double>> PossibleTransforms(FundamentalMatrix f)
        {
            var F = f.matrix.ToMathNet(dimension: 3).Transpose();
            var svd = F.Svd(computeVectors: true);

            Matrix<double> W = CreateMatrix.Dense<double>(3, 3);
            W[0, 1] = -1;
            W[1, 0] = 1;
            W[2, 2] = 1;

            var tC = svd.U.Column(2);
            var translation = tC.ToXna();

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
        /// Compute the "best" rigid transform corresponding to a fundamental matrix, where best
        /// means resulting in the most 3D points being in front of both cameras
        /// </summary>
        /// <param name="f">Fundamental matrix</param>
        /// <param name="modelPoints">Points in model image</param>
        /// <param name="dataPoints">Points in data image</param>
        /// <returns>Matrix from model frame to data frame</returns>
        public static Matrix ExtractTransform(FundamentalMatrix f, Vector2[] modelPoints, Vector2[] dataPoints, out bool[] mask)
        {
            if (modelPoints.Length != dataPoints.Length)
            {
                throw new ArgumentException("Must have equal number of model and data points");
            }
            Matrix bestTransform = new Matrix();
            int bestPositiveDepth = -1;

            mask = null;
            foreach (var mat in PossibleTransforms(f))
            {
                var r1 = mat.Row(0).SubVector(0, 3);
                var r3 = mat.Row(2).SubVector(0, 3);
                var t = mat.Column(3).SubVector(0, 3);

                bool[] thisMask = new bool[modelPoints.Length];

                int positiveDepth = 0;
                for (int i = 0; i < modelPoints.Length; i++)
                {
                    var pm = modelPoints[i];
                    var pd = dataPoints[i];

                    double z = (r1 - pd.X * r3).DotProduct(t) /(r1 - pd.X * r3).DotProduct(CreateVector.DenseOfArray(new[] { pm.X, pm.Y, 1 }));
                    if (z >= 0)
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

        public static Matrix ExtractTransform(AlignmentScene scene, ImagePairCorrespondence match)
        {
            if (match.FundamentalMatrix == null)
            {
                throw new Exception("Match must have computed fundamental matrix");
            }

            var modelPoints = match.DataToModel.Select(d2m => scene.DetectedFeatures[match.ModelImage][d2m.Value].Location).ToArray();
            var dataFeatures = match.DataToModel.Select(d2m => scene.DetectedFeatures[match.DataImage][d2m.Key].Location).ToArray();
            return ExtractTransform(match.FundamentalMatrix, modelPoints, dataFeatures, out bool[] ignoredMask);
        }
    }
}
