using System;
using UnityEngine;
using DotNetMatrix;

public class Procrustes
{
    public const float SVDTolerance = 1e-3f;

    /**
     * Calculate optimal translation, rotation, and scale to transform all movingPts[i] to best match fixedPts[i].
     *
     * http://scicomp.stackexchange.com/questions/6878/fitting-one-set-of-points-to-another-by-a-rigid-motion
     * http://www.codeproject.com/Articles/5835/DotNetMatrix-Simple-Matrix-Library-for-NET
     *
     * For scaling calculation see procrustes.m in matlab stats toolbox.
     *
     * Without scaling this is orthogonal (really *orthonormal*) Procrustes analysis.  With scaling this is extended
     * orthogonal Procrustes analysis (EOP).
     *
     * The input points are assumed to all be in the same coordinate frame.
     *
     * For example, to move a GameObject containing the moving points relative to a GameObject containing the fixed
     * points, first transform them all into world frame (or the frame of any GameObject).
     *
     * You can then apply the computed translation, rotation, and scale using the Procrustes.ApplyTo() function.
     **/
    public static void Calculate(Vector3[] movingPts, Vector3[] fixedPts, out float rmsResidual,
                                 out Vector3 translation, out Quaternion rotation, out float scale,
                                 bool calcTranslation = true, bool calcRotation = true, bool calcScale = false,
                                 float svdTolerance = SVDTolerance)
    {
        rmsResidual = 0;
        translation = Vector3.zero;
        rotation = Quaternion.identity;
        scale = 1.0f;

        if (movingPts.Length != fixedPts.Length)
            throw new System.ArgumentException("must supply same number of points in both sets");

        int numPoints = movingPts.Length;

        if (numPoints < 3)
            throw new System.ArgumentException("must supply at least 3 points in each set");

        Vector3 movingCtr = Vector3.zero, fixedCtr = Vector3.zero;

        if (calcTranslation)
        {
            for (int i = 0; i < numPoints; ++i) { movingCtr += movingPts[i]; fixedCtr += fixedPts[i]; }
            movingCtr /= numPoints; fixedCtr /= numPoints;
        }

        if (calcRotation || calcScale)
        {
            //center point sets on origin
            Vector3[] movingPtsCentered = new Vector3[numPoints], fixedPtsCentered = new Vector3[numPoints];
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
                    movingPtsNorm += movingPtsCentered[j][i]*movingPtsCentered[j][i];
                    fixedPtsNorm += fixedPtsCentered[j][i]*fixedPtsCentered[j][i];
                }
            }
            movingPtsNorm = System.Math.Sqrt(movingPtsNorm);
            fixedPtsNorm = System.Math.Sqrt(fixedPtsNorm);

            GeneralMatrix A = new GeneralMatrix(3, numPoints); //3xN
            GeneralMatrix B = new GeneralMatrix(3, numPoints); //3xN

            for (int j = 0; j < numPoints; ++j)
            {
                for (int i = 0; i < 3; ++i)
                {
                    A.SetElement(i, j, movingPtsCentered[j][i] / movingPtsNorm);
                    B.SetElement(i, j, fixedPtsCentered[j][i] / fixedPtsNorm);
                }
            }

            GeneralMatrix C = B.Multiply(A.Transpose()); //[3xN]x[Nx3] = 3x3

            SingularValueDecomposition svd = C.SVD();

            if (svd.ErrorInfo != 0)
            {
                throw new InvalidOperationException("SVD failed, aborting");
            }

//            Debug.Log("singular values: " +
//                      svd.SingularValues[0] + " " + svd.SingularValues[1] + " " + svd.SingularValues[2]);

            if (svd.SingularValues[1] < svdTolerance)
            {
                throw new InvalidOperationException("SVD ambiguous, aborting");
            }

            GeneralMatrix R = svd.GetU() * svd.GetV().Transpose();

            if (R.Determinant() < 0)
            {
                GeneralMatrix I = GeneralMatrix.Identity(3, 3);
                I.SetElement(2, 2, -1); // flip handedness if solution was inverted
                R = svd.GetU() * I * svd.GetV().Transpose();
            }

            Vector3 forward;
            forward.x = (float)R.GetElement(0, 2);
            forward.y = (float)R.GetElement(1, 2);
            forward.z = (float)R.GetElement(2, 2);

            Vector3 up;
            up.x = (float)R.GetElement(0, 1);
            up.y = (float)R.GetElement(1, 1);
            up.z = (float)R.GetElement(2, 1);

            rotation = Quaternion.LookRotation(forward, up);

            if (calcScale)
            {
                double dscale = 0;
                for (int i = 0; i < 3; ++i) dscale += svd.SingularValues[i];
                dscale *= fixedPtsNorm / movingPtsNorm;
                scale = (float)dscale;
            }
        }

        translation = fixedCtr - rotation * movingCtr * scale;

        for (int i = 0; i < numPoints; ++i)
        {
            rmsResidual += (fixedPts[i] - (translation + rotation * movingPts[i] * scale)).sqrMagnitude;
        }
        rmsResidual = Mathf.Sqrt(rmsResidual/numPoints);
    }
    }
}
