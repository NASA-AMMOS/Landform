using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using log4net;
using Emgu.CV.Structure;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Factorization;
using MathNet.Numerics.Statistics;
using JPLOPS.Util;
using JPLOPS.Imaging.Emgu;

namespace JPLOPS.ImageFeatures
{
    /// <summary>
    /// PCA Training Class.
    /// </summary>
    public class PCATrain
    {
        string gpcafile;
        Vector<float> mean;
        Evd<float> eigs;
        Matrix<float> eigvecs;
        Matrix<float> principalEigVecs;
        Vector<double> principalEigVals;


        private static readonly ILog logger = LogManager.GetLogger(typeof(PCATrain));

        /// <param name="filename">Filename where the trained eigenspace is to be stored.</param>
        public PCATrain(string filename)
        {
            gpcafile = filename;
        }

        /// <summary>
        /// Computes the eigenspace.
        /// </summary>
        /// <param name="gradients">Gradients calculated from training set image data.</param>
        void ComputeEigenspace(List<float[]> gradients)
        {
            Matrix<float> data = Matrix<float>.Build.Dense(gradients.Count, PCAConstants.PATCH_LEN); // inf x 3042

            // convert list of gradient vectors into data matrix of size inf x 3042
            for (int i = 0; i < gradients.Count(); i++)
            {
                data.SetRow(i, Vector<float>.Build.Dense(gradients[i]));
            }
            
            // Calculate column-wise mean
            mean = ColumnWiseMean(data); // should be length 3042

            // calculate covariance matrix
            logger.Info("Calculating covariance matrix...");
            Matrix<float> covar = CovarianceMatrix(data);

            // eigen decomposition
            logger.Info("Calculating eigen decomposition...");
            eigs = covar.Evd();
            eigvecs = eigs.EigenVectors;
            Vector<double> eigvals = eigs.EigenValues.Real();
            OrderEigenvectors(eigvecs, eigvals);

            principalEigVecs = eigvecs.SubMatrix(0, eigvecs.RowCount, 0, PCAConstants.N);
            principalEigVals = eigs.EigenValues.Real().SubVector(0, PCAConstants.N);

            logger.Info(principalEigVecs);
            logger.Info(principalEigVals);

            WriteEigenvectorsToFile(gpcafile + ".txt", true);
        }

        void OrderEigenvectors(Matrix<float> eigvecs, Vector<double> eigvals)
        {
            Dictionary<double, Vector<float>> vectorDict = new Dictionary<double, Vector<float>>();
            eigvecs.EnumerateColumnsIndexed().ToList().ForEach(x => vectorDict[eigvals[x.Item1]] = x.Item2);

            IOrderedEnumerable<double> eigvalOrder = eigvals.OrderBy(x => -Math.Abs(x));
            List<double> eigvalList = eigvalOrder.ToList();
            eigvals.SetValues(eigvalOrder.ToArray());

            for (int i = 0; i < eigvecs.ColumnCount; i++)
            {
                eigvecs.SetColumn(i, vectorDict[eigvalList[i]]);
            }
        }

        /// <summary>
        /// Train PCA with images in path.
        /// </summary>
        /// <param name="path">Path to training image files.</param>
        public void Train(string path)
        {
            string[] imageFiles = Directory.GetFiles(path, "*.png");
            object obj = new object();
            List<float[]> gradients = new List<float[]>();
            CoreLimitedParallel.For(0, imageFiles.Count(), i =>
                {
                    float[][] grads = CalculateGradients(imageFiles[i]);
                    lock (obj)
                    {
                    gradients.AddRange(CalculateGradients(imageFiles[i]));
                    }
                });
            
            ComputeEigenspace(gradients);
        }

        /// <summary>
        /// Calculates the gradients for the given image and appends them to running gradients list.
        /// </summary>
        /// <returns>The updated gradients lsit.</returns>
        /// <param name="imageFile">Image file.</param>
        /// <param name="gradients">Running list of gradients.</param>
        float[][] CalculateGradients(string imageFile)
        {
            Imaging.Image modelImage = Imaging.Image.Load(imageFile);
            Emgu.CV.Image<Gray, float> grayModelImage = modelImage.ToEmguGrayscale().Convert<Gray, float>();
            List<PCASIFTFeature> featuresA = new PCASIFTDetector().Detect(modelImage, null).Cast<PCASIFTFeature>().ToList();
            List<PCASIFTFeature> PCAKeypoints = GetPatches(grayModelImage, featuresA, PCAConstants.PATCH_SIZE);
            return PCAUtil.GetGradients(featuresA);
        }

        /// <summary>
        /// Calculates the covariance matrix for a given dataset.
        /// </summary>
        /// <returns>The covariance matrix.</returns>
        /// <param name="data">Dataset.</param>
        public static Matrix<float> CovarianceMatrix(Matrix<float> data)
        {
            Matrix<float> result = Matrix<float>.Build.Dense(data.ColumnCount, data.ColumnCount);
            Vector<float>[] A = new Vector<float>[data.ColumnCount];
            Vector<float>[] B = new Vector<float>[data.ColumnCount];

            Vector<float> vec;

            CoreLimitedParallel.For(0, data.ColumnCount, i =>
            {
                vec = data.Column(i);
                B[i] = (vec.Subtract((float)vec.Mean()));
                A[i] = (B[i].Conjugate());
                result[i, i] = (A[i].PointwiseMultiply(B[i])).Sum();
            });

            float coeff = 1f / (data.RowCount - 1);

            CoreLimitedParallel.For(0, data.ColumnCount, i =>
            {
                float resultNum;
                Vector<float> vecA, vecB;
                for (int j = i; j < data.ColumnCount; j++)
                {
                    resultNum = 0;
                    vecA = A[i];
                    vecB = B[i];

                    for (int k = 0; k < vecA.Count; k++)
                    {
                        resultNum += vecA[k] * vecB[k];
                    }

                    resultNum *= coeff;
                    result[i, j] = resultNum;
                    result[j, i] = resultNum;
                }
            });

            return result;
        }

		/// <summary>
		/// Calculates the column-wise mean of a <see cref="T:MathNet.Numerics.LinearAlgebra"/> matrix.
		/// </summary>
		/// <returns>The column-wise mean as vector of length input.columnCount.</returns>
		/// <param name="input">Input matrix.</param>
		Vector<float> ColumnWiseMean(Matrix<float> input)
        {
            Vector<float> result = Vector<float>.Build.Dense(input.ColumnCount);
            Debug.Assert(input.ColumnCount == PCAConstants.PATCH_LEN);

            for (int i = 0; i < input.ColumnCount; i++)
            {
                result[i] = (float)input.Column(i).Mean();
            }

            return result;
        }

        /// <summary>
        /// Writes the eigenvectors and mean to file.
        /// </summary>
        /// <param name="filename">Filename of location where the eigenvectors and mean are to be saved.</param>
        void WriteEigenvectorsToFile(string filename, bool readable = false)
        {
            logger.Info("Writing eigenvectors to " + filename);

            if (!readable)
            {
                using (BinaryWriter writer = new BinaryWriter(new FileStream(filename, FileMode.Create)))
                {
                    // mean should be of length 3042
                    for (int a = 0; a < 3042; a++)
                    {
                        writer.Write(mean[a]);
                    }

                    // eigvecs should be 3042x36
                    for (int i = 0; i < 3042; i++)
                    {
                        for (int j = 0; j < PCAConstants.N; j++)
                        {
                            writer.Write(principalEigVecs[i, j]);
                        }
                    }
                }
            }
            else
            {
                using (StreamWriter writer = new StreamWriter(new FileStream(filename, FileMode.Create)))
                {
                    // mean should be of length 3042
                    for (int a = 0; a < 3042; a++)
                    {
                        writer.WriteLine("  " + mean[a]);
                    }

                    // eigvecs should be 3042x36
                    for (int i = 0; i < 3042; i++)
                    {
                        for (int j = 0; j < PCAConstants.N; j++)
                        {
                            writer.Write("  " + principalEigVecs[i, j]);
                        }
                        writer.WriteLine();
                    }
                }
            }

            logger.Info("Wrote to " + filename);
        }

        /// <summary>
        /// Creates an image patch for all keypoints of an image.
        /// </summary>
        /// <param name="keypoints">List of keypoints</param>
        /// <param name="octaves">Calculated Gaussian pyramids for all octaves.</param>
        /// <param name="windowsize">Height and width of patch.</param>
        static void ComputeLocalPatches(List<PCASIFTFeature> keypoints, List<List<Emgu.CV.Image<Gray, float>>> octaves, int windowsize)
        {
            for (int i = 0; i < keypoints.Count; i++)
            {
                PCASIFTFeature key = keypoints[i];

                Debug.Assert(key.Octave >= 0 && key.Octave < octaves.Count);
                Debug.Assert(key.IScale >= 0 && key.IScale < octaves[key.Octave].Count);

                int iradius, patchsize;
                double sine, cosine, sizeratio;
                float scale = PCAConstants.SIGMA * (float)Math.Pow(2.0, key.FScale / PCAConstants.SCALES_PER_OCTAVE);
                Emgu.CV.Image<Gray, float> blur = octaves[key.Octave][key.IScale];

                // Sampling window size
                patchsize = (int)(PCAConstants.PATCH_MAG * scale);

                // Make odd
                patchsize = (patchsize / 2) * 2 + 1;

                // Technically a bug fix but should do the trick for now
                if (patchsize < PCAConstants.PATCH_SIZE) patchsize = PCAConstants.PATCH_SIZE;

                sizeratio = patchsize / (float)PCAConstants.PATCH_SIZE;
                key.Patch = new Emgu.CV.Image<Gray, float>(windowsize, windowsize);
                float[,,] data = key.Patch.Data;

                sine = (float)Math.Sin(key.Angle);
                cosine = (float)Math.Cos(key.Angle);

                iradius = windowsize / 2;
                float[,,] blurData = blur.Data;
                int height = blur.Height;
                int width = blur.Width;

                double cpos, rpos;
                for (int y = -iradius; y <= iradius; y++)
                {
                    for (int x = -iradius; x <= iradius; x++)
                    {
                        cpos = (float)(cosine * x * sizeratio + sine * y * sizeratio) + key.SX;
                        rpos = (float)(-sine * x * sizeratio + cosine * y * sizeratio) + key.SY;
                        data[x + iradius, y + iradius, 0] = PCAUtil.GetPixelBilinearInterpolation(blurData, cpos, rpos, height, width);
                    }
                }
            }
        }

        /// <summary>
        /// Gathers patches of size patchsize x patchsize for all keypoints of a given image.
        /// </summary>
        /// <param name="image">Input image.</param>
        /// <param name="keypoints">Keypoints detected with SIFT.</param>
        /// <param name="patchsize">Height and width of patch.</param>
        /// <returns></returns>
        public static List<PCASIFTFeature> GetPatches(Emgu.CV.Image<Gray, float> image, List<PCASIFTFeature> keypoints, int patchsize)
        {
            // 1. Scale image to create base of Gaussian pyramid
            image = PCAUtil.ScaleInitImage(image);

            // 2. Build Gaussian octaves
            List<List<Emgu.CV.Image<Gray, float>>> octaves = PCAUtil.BuildGaussianOctaves(image);

            // 3. Update all keypoint parameters
            PCAUtil.UpdateKeypoints(keypoints);

            // 4. Compute local patches
            ComputeLocalPatches(keypoints, octaves, patchsize);

            return keypoints;
        }

    }
}
