using Emgu.CV;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    /// <summary>
    /// Based on "A probabilistic criterion to detect rigid point matches between two images and estimate the fundamental matrix", L. Moisan, B. Stival
    /// More info at http://www.math-info.univ-paris5.fr/~moisan/epipolar/
    /// </summary>
    public class MoisanStivalEpipolar
    {
        public Vector2[] ModelPoints, DataPoints;
        public Vector2 ModelSize, DataSize;

        List<int> bestPoints;
        double[] epipolarError;
        double logAlpha0;
        Random random;

        double overallMinEps, overallMinAlpha;
        int[] overallMinPoints;
        bool[] overallMinPointsGood;
        Matrix<float> overallMinF;

        float[] log_cn, log_c7;
        double log_e0;

        double modelNorm, dataNorm;

        /// <summary>
        /// Initialize the procedure with a set of potentially corresponding points.
        /// The nth entry of modelPoints should correspond to the nth entry of dataPoints.
        /// </summary>
        /// <param name="modelPoints">Points in frame of "model" image</param>
        /// <param name="dataPoints">Points in frame of "data" image</param>
        /// <param name="modelSize">Dimensions (in same units as modelPoints) of "model" image</param>
        /// <param name="dataSize">Dimensions (in same units as dataPoints) of "data" image</param>
        public MoisanStivalEpipolar(Vector2[] modelPoints, Vector2[] dataPoints, Vector2 modelSize, Vector2 dataSize)
        {
            this.ModelPoints = modelPoints;
            this.DataPoints = dataPoints;
            this.ModelSize = modelSize;
            this.DataSize = dataSize;

            log_cn = makelogcombi_n(modelPoints.Length);
            log_c7 = makelogcombi_k(7, modelPoints.Length);
            log_e0 = Math.Log10(3.0 * (modelPoints.Length - 7));
            bestPoints = Enumerable.Range(0, modelPoints.Length).ToList();
            epipolarError = new double[modelPoints.Length];
            random = new Random();

            double nx = dataSize.X,
                   ny = dataSize.Y;
            dataNorm = 1 / Math.Sqrt(dataSize.X * dataSize.Y);
            modelNorm = 1 / Math.Sqrt(modelSize.X * modelSize.Y);
            logAlpha0 = (Math.Log10(2.0) + 0.5 * Math.Log10((double)((nx * nx + ny * ny) * dataNorm * dataNorm)));

            int i;
            for (i = 0; i < modelPoints.Length; i++)
            {
                modelPoints[i] = (modelPoints[i] - modelSize / 2) * modelNorm;
                dataPoints[i] = (dataPoints[i] - dataSize / 2) * dataNorm;
            }

            overallMinEps = double.PositiveInfinity;
            overallMinAlpha = double.PositiveInfinity;
        }

        /// <summary>
        /// Perform one round of the algorithm.
        /// </summary>
        public void IterateOnce(bool refine = false)
        {
            int[] randomPoints;
            if (refine)
            {
                randomPoints = random_p7(overallMinPoints.Length);
                int i;
                for (i = 0; i < 7; i++)
                {
                    randomPoints[i] = overallMinPoints[randomPoints[i]];
                }
            }
            else
            {
                randomPoints = random_p7(ModelPoints.Length);
            }

            foreach (Matrix<float> F in ComputeEpipolarMatrix(randomPoints))
            {
                // Compute epipolar error and sort points by it
                Parallel.For(0, ModelPoints.Length, idx =>
                {
                    epipolarError[idx] = EpipolarError(ModelPoints[idx], F, DataPoints[idx]);
                });
                bestPoints.Sort((i0, i1) => epipolarError[i0].CompareTo(epipolarError[i1]));

                // Find the most meaningful subset
                double minEps = double.PositiveInfinity,
                       minLogAlpha = double.PositiveInfinity;
                int minIdx = -1;
                // Skip best seven points, as their epipolar is zero by definition
                int i;
                for (i = 7; i < ModelPoints.Length; i++)
                {
                    double logAlpha = logAlpha0 + 0.5 * Math.Log10(epipolarError[bestPoints[i]]);
                    double eps = log_e0 + logAlpha * (i - 6) + log_cn[i + 1] + log_c7[i + 1];
                    if (eps < minEps)
                    {
                        minEps = eps;
                        minIdx = i;
                        minLogAlpha = logAlpha;
                    }
                }

                // Store best found
                if (minEps < overallMinEps)
                {
                    overallMinEps = minEps;
                    overallMinAlpha = minLogAlpha;
                    overallMinPoints = bestPoints.Take(minIdx).ToArray();
                    overallMinF = F;
                }
            }
        }

        /// <summary>
        /// Run the procedure until convergence.
        /// </summary>
        /// <param name="maxIters">Maximum number of iterations to run</param>
        /// <param name="refineStep">If true, will use the ORSA step from the paper</param>
        public void Run(int maxIters = 5000, bool refineStep = true)
        {
            bool refine = false;
            int i;
            for (i = 0; i < maxIters; i++)
            {
                IterateOnce(refine);

                if (refineStep && !refine && (
                    (i >= maxIters * 9 / 10) ||
                    (overallMinEps < -10)))
                {
                    refine = true;
                    i = Math.Max(i, maxIters * 9 / 10);
                }
            }
        }

        /// <summary>
        /// Return the indices of all valid correspondences given the current solution.
        /// </summary>
        /// <returns>Indices into (model|data)Points</returns>
        public IEnumerable<int> ComputeInliers()
        {
            overallMinPointsGood = new bool[overallMinPoints.Length];
            Matrix<float> F = overallMinF;
            Matrix<float> FT = overallMinF.Transpose();
            Matrix<float> W = new Matrix<float>(3, 1);
            Matrix<float> U = new Matrix<float>(3, 3);
            Matrix<float> V = new Matrix<float>(3, 3);
            CvInvoke.SVDecomp(F, W, U, V, Emgu.CV.CvEnum.SvdFlag.Default);
            Matrix<float> VT = V.Transpose();

            Matrix<float> bestRotation = null;
            Vector3 bestTranslation = Vector3.Zero;
            int bestPositiveCount = 0;

            foreach (var transform in PossibleTransforms(W, U, VT))
            {
                int positiveCount = overallMinPoints.Count(idx => PositiveDepth(transform.Key, transform.Value, ModelPoints[idx], DataPoints[idx]));
                if (positiveCount > bestPositiveCount)
                {
                    bestPositiveCount = positiveCount;
                    bestRotation = transform.Key;
                    bestTranslation = transform.Value;
                }
            }

            foreach (int idx in overallMinPoints)
            {
                if (PositiveDepth(bestRotation, bestTranslation, ModelPoints[idx], DataPoints[idx]))
                {
                    yield return idx;
                }
            }
        }

        /// <summary>
        /// If true, resulting set of correspondences is epsilon-meaningful
        /// </summary>
        public bool Meaningful
        {
            get
            {
                return overallMinEps < 0;
            }
        }

        /// <summary>
        /// Compute the epipolar line in the "model" image corresponding to
        /// a given point in the "data" image.
        /// </summary>
        public Vector3 ModelEpiLine(Vector2 dataPt)
        {
            Vector3 epiline = Transform((dataPt - DataSize / 2) * dataNorm, overallMinF.Transpose());
            Vector3 imageSpace = new Vector3(
                epiline.X * modelNorm,
                epiline.Y * modelNorm,
                epiline.Z - modelNorm * (epiline.X * ModelSize.X + epiline.Y * ModelSize.Y) / 2
                );
            return imageSpace;
        }

        /// <summary>
        /// Compute the epipolar line in the "data" image corresponding to
        /// a given point in the "model" image.
        /// </summary>
        public Vector3 DataEpiLine(Vector2 modelPt)
        {
            Vector3 epiline = Transform((modelPt - ModelSize / 2) * modelNorm, overallMinF);
            Vector3 imageSpace = new Vector3(
                epiline.X * dataNorm,
                epiline.Y * dataNorm,
                epiline.Z - dataNorm * (epiline.X * DataSize.X + epiline.Y * DataSize.Y) / 2
                );
            return imageSpace;
        }

        /// <summary>
        /// Compute the epipolar error for a point match given a fundamental matrix.
        /// </summary>
        /// <param name="p0">Point 0</param>
        /// <param name="F">3x3 fundamental matrix</param>
        /// <param name="p1">Point 1</param>
        double EpipolarError(Vector2 p0, Matrix<float> F, Vector2 p1)
        {
            Vector3 transformed = Transform(p0, F);
            double a = transformed.X,
                  b = transformed.Y,
                  c = transformed.Z;
            double d = (a * p1.X) + (b * p1.Y) + c;
            return d * d / (a * a + b * b);
        }

        /// <summary>
        /// Compute the F-rigidity of a subset of point matches.
        /// </summary>
        /// <param name="F">3x3 fundamental matrix</param>
        /// <param name="useMatch">Match mask</param>
        /// <returns></returns>
        double Rigidity(Matrix<float> F, bool[] useMatch)
        {
            double maxModelError = double.NegativeInfinity,
                   maxDataError = double.NegativeInfinity;
            Matrix<float> FT = F.Transpose();
            int i;
            for (i = 0; i < ModelPoints.Length; i++)
            {
                if (useMatch != null && !useMatch[i]) continue;
                double dataError = EpipolarError(ModelPoints[i], F, DataPoints[i]);
                if (dataError > maxDataError) maxDataError = dataError;
                double modelError = EpipolarError(DataPoints[i], FT, ModelPoints[i]);
                if (modelError > maxModelError) maxModelError = modelError;
            }
            Vector2 size;
            if (maxModelError > maxDataError)
            {
                size = ModelSize;
            }
            else
            {
                size = DataSize;
            }
            double D = 2 * size.X + 2 * size.Y;
            double A = size.X * size.Y;
            return (2 * D / A) * Math.Max(maxModelError, maxDataError);
        }

        /// <summary>
        /// Find any valid epipolar matrices associated with a set of seven
        /// correspondences.
        /// </summary>
        /// <param name="indices">Seven indices into (model|data)Points</param>
        /// <returns>0 or more matrices</returns>
        IEnumerable<Matrix<float>> ComputeEpipolarMatrix(int[] indices)//Vector2[] m1, Vector2[] m2)
        {
            Matrix<float> m1_cv = new Matrix<float>(7, 2),
                          m2_cv = new Matrix<float>(7, 2);
            int i;
            for (i = 0; i < 7; i++)
            {
                m1_cv[i, 0] = (float)ModelPoints[indices[i]].X;
                m1_cv[i, 1] = (float)ModelPoints[indices[i]].Y;
                m2_cv[i, 0] = (float)DataPoints[indices[i]].X;
                m2_cv[i, 1] = (float)DataPoints[indices[i]].Y;
            }
            Mat _F = new Mat();
            CvInvoke.FindFundamentalMat(m1_cv, m2_cv, _F, Emgu.CV.CvEnum.FmType.SevenPoint);
            int numMatrices = _F.Rows / 3;
            if (numMatrices < 1) yield break;

            Matrix<float> F = new Matrix<float>(_F.Rows, _F.Cols);
            _F.ConvertTo(F, Emgu.CV.CvEnum.DepthType.Cv32F);
            for (i = 0; i < numMatrices; i++)
            {
                Matrix<float> Fp = new Matrix<float>(3, 3);
                bool hasData = false;
                int row, col;
                for (row = 0; row < 3; row++)
                {
                    for (col = 0; col < 3; col++)
                    {
                        Fp[row, col] = F[3 * i + row, col];
                        if (Math.Abs(Fp[row, col]) > 1e-11)
                        {
                            hasData = true;
                        }
                    }
                }
                if (hasData)
                {
                    yield return Fp;
                }
            }
        }

        // end public facing

        IEnumerable<KeyValuePair<Matrix<float>, Vector3>> PossibleTransforms(Matrix<float> Wigma, Matrix<float> U, Matrix<float> VT)
        {
            Matrix<float> W = new Matrix<float>(3, 3);
            W[0, 1] = -1;
            W[1, 0] = 1;
            W[2, 2] = 1;
            Vector3 translation = new Vector3(U[2, 0], U[2, 1], U[2, 2]);

            yield return new KeyValuePair<Matrix<float>, Vector3>(
                U * W * VT,
                translation
                );
            yield return new KeyValuePair<Matrix<float>, Vector3>(
                U * W * VT,
                -translation
                );
            yield return new KeyValuePair<Matrix<float>, Vector3>(
                U * W.Transpose() * VT,
                translation
                );
            yield return new KeyValuePair<Matrix<float>, Vector3>(
                U * W.Transpose() * VT,
                -translation
                );
        }

        bool PositiveDepth(Matrix<float> rotation, Vector3 translation, Vector2 modelPos, Vector2 dataPos)
        {
            Vector3 r1 = new Vector3(rotation[0, 0], rotation[0, 1], rotation[0, 2]),
                    r3 = new Vector3(rotation[2, 0], rotation[2, 1], rotation[2, 2]);
            double z = Vector3.Dot((r1 - dataPos.X * r3), translation) / Vector3.Dot((r1 - dataPos.X * r3), new Vector3(modelPos, 1));
            if (z < 0) return false;
            return true;
        }

        static Vector3 Transform(Vector3 p0, Matrix<float> F)
        {
            // HACK: For some reason Emgu is stomping all over the stack
            // when performing this multiplication in a threaded context.
            //Matrix<float> transformed = F * new Matrix<float>(new float[] { (float)p0.X, (float)p0.Y, (float)p0.Z });
            //return new Vector3(
            //    transformed[0, 0],
            //    transformed[1, 0],
            //    transformed[2, 0]);
            float[,] _F = F.Data;
            return new Vector3(
                 p0.X * _F[0, 0] + p0.Y * _F[0, 1] + p0.Z * _F[0, 2],
                 p0.X * _F[1, 0] + p0.Y * _F[1, 1] + p0.Z * _F[1, 2],
                 p0.X * _F[2, 0] + p0.Y * _F[2, 1] + p0.Z * _F[2, 2]
                 );
        }

        static Vector3 Transform(Vector2 p0, Matrix<float> F)
        {
            return Transform(new Vector3(p0, 1), F);
        }

        Vector2 NormToModel(Vector2 normalizedCoords)
        {
            return (normalizedCoords / modelNorm) + ModelSize / 2;
        }
        Vector2 NormToData(Vector2 normalizedCoords)
        {
            return (normalizedCoords / dataNorm) + DataSize / 2;
        }

        // Following functions from http://www.math-info.univ-paris5.fr/~moisan/epipolar/stereomatch.c

        /* logarithm (base 10) of binomial coefficient */
        float logcombi(int k, int n)
        {
            double r;
            int i;

            if (k >= n || k <= 0) return (0.0f);
            if (n - k < k) k = n - k;
            r = 0.0;
            for (i = 1; i <= k; i++)
                r += Math.Log10((double)(n - i + 1)) - Math.Log10((double)i);

            return ((float)r);
        }

        /* tabulate logcombi(.,n) */
        float[] makelogcombi_n(int n)
        {
            float[] l = new float[n + 1];
            int k;
            for (k = 0; k <= n; k++) l[k] = logcombi(k, n);

            return (l);
        }

        /* tabulate logcombi(k,.) */
        float[] makelogcombi_k(int k, int nmax)
        {
            float[] l = new float[nmax + 1];
            int n;
            for (n = 0; n <= nmax; n++) l[n] = logcombi(k, n);

            return l;
        }

        /* get a (sorted) random 7-uple of 0..n-1 */
        int[] random_p7(int n)
        {
            int[] k = new int[7];
            int i;
            for (i = 0; i < 7; i++)
            {
                int r = random.Next(n - i);
                int j;
                for (j = 0; j < i && r >= k[j]; j++)
                {
                    r++;
                }
                int j0 = j;
                for (j = i; j > j0; j--) k[j] = k[j - 1];
                k[j0] = r;
            }
            return k;
        }
    }
}
