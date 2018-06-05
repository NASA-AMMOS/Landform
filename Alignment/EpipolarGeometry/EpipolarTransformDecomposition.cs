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
        public static IEnumerable<Matrix> PossibleTransforms(FundamentalMatrix f)
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

            yield return R1.Transpose().ToXna() * Matrix.CreateTranslation(translation);
            yield return R2.Transpose().ToXna() * Matrix.CreateTranslation(translation);
            yield return R1.Transpose().ToXna() * Matrix.CreateTranslation(-translation);
            yield return R2.Transpose().ToXna() * Matrix.CreateTranslation(-translation);
        }

        public static Matrix ExtractTransform(FundamentalMatrix f, Vector2[] modelPoints, Vector2[] dataPoints)
        {
            if (modelPoints.Length != dataPoints.Length)
            {
                throw new ArgumentException("Must have equal number of model and data points");
            }
            Matrix bestTransform = new Matrix();
            int bestPositiveDepth = -1;

            foreach (var mat in PossibleTransforms(f))
            {
                var mM = mat.ToMathNet();
                var r1 = mM.Row(0).SubVector(0, 3);
                var r3 = mM.Row(2).SubVector(0, 3);
                var t = mat.Translation.ToMathNet();

                int positiveDepth = 0;
                for (int i = 0; i < modelPoints.Length; i++)
                {
                    var pm = modelPoints[i];
                    var pd = dataPoints[i];

                    double z = (r1 - pd.X * r3).DotProduct(t) /(r1 - pd.X * r3).DotProduct(CreateVector.DenseOfArray(new[] { pm.X, pm.Y, 1 }));
                    if (z >= 0) positiveDepth++;
                }

                if (positiveDepth > bestPositiveDepth)
                {
                    bestPositiveDepth = positiveDepth;
                    bestTransform = mat;
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

            var modelPoints = match.DataToModel.Select(d2m => scene.Context.DetectedFeatures[match.ModelImage][d2m.Value].Location).ToArray();
            var dataFeatures = match.DataToModel.Select(d2m => scene.Context.DetectedFeatures[match.DataImage][d2m.Key].Location).ToArray();
            return ExtractTransform(match.FundamentalMatrix, modelPoints, dataFeatures);
        }
    }
}
