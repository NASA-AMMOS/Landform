using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;
using Microsoft.Xna.Framework;

//ported from AsteroidViz/Assets/Scripts/Procrustes.cs for Unity by marty vona
//SHA: 321c19b84ddadbf2d7376860e8ccb062381df38b

namespace OPS.Pipeline.Alignment
{
    public class Procrustes
    {

        public const double SVDTolerance = 1e-3f;

        /**
            * Calculate optimal translation, rotation, and scale to transform all movingPts[i] to best match fixedPts[i].
            * http://scicomp.stackexchange.com/questions/6878/fitting-one-set-of-points-to-another-by-a-rigid-motion
            * For scaling calculation see procrustes.m in matlab stats toolbox.
            * Without scaling this is orthogonal (really *orthonormal*) Procrustes analysis.  With scaling this is extended
            * orthogonal Procrustes analysis (EOP).
            * The input points are assumed to all be in the same coordinate frame.
            **/

        public static void Calculate(Vector3[] movingPts, Vector3[] fixedPts, out double rmsResidual,
                                        out Vector3 translation, out Quaternion rotation, out double scale,
                                        bool calcTranslation = true, bool calcRotation = true, bool calcScale = false,
                                        double svdTolerance = SVDTolerance)
        {
            rmsResidual = 0;
            translation = Vector3.Zero;
            rotation = Quaternion.Identity;
            scale = 1.0f;

            if (movingPts.Length != fixedPts.Length)
                throw new System.ArgumentException("must supply same number of points in both sets");

            int numPoints = movingPts.Length;

            if (numPoints < 3)
                throw new System.ArgumentException("must supply at least 3 points in each set");

            Vector3 movingCtr = Vector3.Zero, fixedCtr = Vector3.Zero;

            if (calcTranslation)
            {
                for (int i = 0; i < numPoints; ++i)
                {
                    movingCtr += movingPts[i];
                    fixedCtr += fixedPts[i];
                }

                movingCtr /= numPoints;
                fixedCtr /= numPoints;
            }

            if (calcRotation || calcScale)
            {
                //center point sets on origin
                Vector3[] movingPtsCentered = new Vector3[numPoints];
                Vector3[] fixedPtsCentered = new Vector3[numPoints];
                for (int i = 0; i < numPoints; ++i)
                {
                    movingPtsCentered[i] = movingPts[i] - movingCtr;
                    fixedPtsCentered[i] = fixedPts[i] - fixedCtr;
                }

                double movingPtsNorm = 0, fixedPtsNorm = 0;
                for (int j = 0; j < numPoints; ++j)
                {
                    for (int i = 0; i < 3; ++i)
                    {
                        movingPtsNorm += movingPtsCentered[j][i] * movingPtsCentered[j][i];
                        fixedPtsNorm += fixedPtsCentered[j][i] * fixedPtsCentered[j][i];
                    }
                }

                movingPtsNorm = Math.Sqrt(movingPtsNorm);
                fixedPtsNorm = Math.Sqrt(fixedPtsNorm);

                MathNet.Numerics.LinearAlgebra.Double.DenseMatrix A = new MathNet.Numerics.LinearAlgebra.Double.DenseMatrix(3, numPoints); //moving
                MathNet.Numerics.LinearAlgebra.Double.DenseMatrix B = new MathNet.Numerics.LinearAlgebra.Double.DenseMatrix(3, numPoints); //fixed

                for (int j = 0; j < numPoints; ++j)
                {
                    for (int i = 0; i < 3; ++i)
                    {
                        A[i, j] = movingPtsCentered[j][i] / movingPtsNorm;
                        B[i, j] = fixedPtsCentered[j][i] / fixedPtsNorm;
                    }
                }

                MathNet.Numerics.LinearAlgebra.Matrix<double> C = B.Multiply(A.Transpose()); //[3xN]x[Nx3] = 3x3
                var svd = C.Svd();

                if (svd.S[1] < svdTolerance)
                {
                    throw new InvalidOperationException("SVD ambiguous, aborting");
                }

                MathNet.Numerics.LinearAlgebra.Matrix<double> R = svd.U * svd.VT;

                if (R.Determinant() < 0)
                {
                    MathNet.Numerics.LinearAlgebra.Double.DiagonalMatrix I = MathNet.Numerics.LinearAlgebra.Double.DiagonalMatrix.CreateIdentity(3);
                    I[2, 2] = -1; // flip handedness if solution was inverted
                    R = svd.U * I * svd.VT;
                }

                if (calcRotation)
                {
                    Vector3 zAxis;
                    zAxis.X = R[0, 2];
                    zAxis.Y = R[1, 2];
                    zAxis.Z = R[2, 2];

                    Vector3 yAxis;
                    yAxis.X = R[0, 1];
                    yAxis.Y = R[1, 1];
                    yAxis.Z = R[2, 1];

                    Vector3 xAxis = Vector3.Normalize(Vector3.Cross(yAxis,zAxis));
                    yAxis = Vector3.Normalize(Vector3.Cross(zAxis,xAxis));

                    //build row major matrix from basis vectors
                    Matrix rotMat = new Matrix(xAxis.X, xAxis.Y, xAxis.Z, 0,
                                                yAxis.X, yAxis.Y, yAxis.Z, 0,
                                                zAxis.X, zAxis.Y, zAxis.Z, 0,
                                                0, 0, 0, 1);
                    rotation = Quaternion.CreateFromRotationMatrix(rotMat);
                }

                if (calcScale)
                {
                    double dscale = 0;
                    for (int i = 0; i < 3; ++i) dscale += svd.S[i];
                    dscale *= fixedPtsNorm / movingPtsNorm;
                    scale = dscale;
                }
            }

            translation = fixedCtr - Vector3.Transform(movingCtr, rotation) * scale;

            for (int i = 0; i < numPoints; ++i)
            {
                rmsResidual += (fixedPts[i] - (translation + Vector3.Transform(movingPts[i], rotation) * scale)).LengthSquared();
            }
            rmsResidual = Math.Sqrt(rmsResidual / numPoints);
        }
    }
}
