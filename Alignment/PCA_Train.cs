using System.Collections.Generic;
using System.Linq;
using Emgu.CV.XFeatures2D;
using OPS.Imaging.Emgu;
using System.Diagnostics;
using System.IO;
using Emgu.CV.Structure;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Factorization;
using MathNet.Numerics.Statistics;

namespace OPS.Alignment
{
    /// <summary>
    /// PCA Training Class.
    /// </summary>
    public class PCA_Train
    {
        const int n = 36;
        const int patchsize = 39;
        const int patchlen = 3042; // = patchsize * patchsize * 2;
        string gpcafile;
        Vector<float> mean;
        Evd<float> eigs;
        Matrix<float> eigvecs;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:OPS.Alignment.PCA_Train"/> class.
        /// </summary>
        /// <param name="filename">Filename where the trained eigenspace is to be stored.</param>
        public PCA_Train(string filename)
        {
            gpcafile = filename;
        }

        /// <summary>
        /// Computes the eigenspace.
        /// </summary>
        /// <param name="gradients">Gradients calculated from training set image data.</param>
        void ComputeEigenspace(List<float[]> gradients)
        {
            Matrix<float> data = Matrix<float>.Build.Dense(gradients.Count(), patchlen);

            // convert list of gradient vectors into data matrix
            for (int i = 0; i < gradients.Count(); i++)
            {
                data.SetRow(i, Vector<float>.Build.Dense(gradients[i]));
            }

            // Calculate column-wise mean
            mean = ColumnWiseMean(data);

            // calculate covariance matrix
            Matrix<float> covar = CovarianceMatrix(data);

            // eigendecomposition
            eigs = covar.Evd();
            eigvecs = eigs.EigenVectors;
            Matrix<float> principalVecs = Matrix<float>.Build.Dense(eigvecs.RowCount, n);
            principalVecs = eigvecs.SubMatrix(0, eigvecs.RowCount, 0, n);
            Vector<double> principalVals = Vector<double>.Build.Dense(n);
            principalVals = eigs.EigenValues.Real().SubVector(0, n);

            Debug.WriteLine(principalVecs);
            Debug.WriteLine(principalVals);

            WriteEigenvectorsToFile(gpcafile);
        }

        /// <summary>
        /// Train PCA with images in path.
        /// </summary>
        /// <param name="path">Path to training image files.</param>
        public void Train(string path)
        {
            string[] imageFiles = Directory.GetFiles(path, "*.jpg");
            List<float[]> gradients = new List<float[]>();

            for (int i = 0; i < imageFiles.Length; i++)
            {
                gradients = CalculateGradients(imageFiles[i], gradients);
            }

            ComputeEigenspace(gradients);
        }

        /// <summary>
        /// Calculates the gradients for the given image and appends them to running gradients list.
        /// </summary>
        /// <returns>The updated gradients lsit.</returns>
        /// <param name="imageFile">Image file.</param>
        /// <param name="gradients">Running list of gradients.</param>
        List<float[]> CalculateGradients(string imageFile, List<float[]> gradients)
        {
            Emgu.CV.Image<Gray, byte> modelImage = Imaging.Image.Load(imageFile).ToEmguGrayscale();
            Emgu.CV.Image<Gray, float> grayModelImage = modelImage.Convert<Gray, float>();
            SIFT sift = new SIFT();
            MKeyPoint[] mKeypoints = sift.Detect(modelImage);
            List<PCA_Keypoint> PCAKeypoints = PCA_KeypointDetector.getPatches(grayModelImage, mKeypoints, patchsize + 2);
            gradients.AddRange(PCA_KeypointDetector.getGradients(PCAKeypoints));

            return gradients;
        }

        /// <summary>
        /// Calculates the covariance matrix for a given dataset.
        /// </summary>
        /// <returns>The covariance matrix.</returns>
        /// <param name="data">Dataset.</param>
        Matrix<float> CovarianceMatrix(Matrix<float> data)
        {
            Matrix<float> result = Matrix<float>.Build.Dense(data.ColumnCount, data.ColumnCount);
            Dictionary<int, Vector<float>> A = new Dictionary<int, Vector<float>>();
            Dictionary<int, Vector<float>> B = new Dictionary<int, Vector<float>>();

            // precompute A, B in cov(A, B)
            for (int i = 0; i < data.ColumnCount; i++)
            {
                Vector<float> vec = data.Column(i);
                B[i] = vec.Subtract((float)vec.Mean());
                A[i] = B[i].Conjugate();
            }

            // set values in covariance matrix
            for (int i = 0; i < result.ColumnCount; i++)
            {
                for (int j = 0; j < result.ColumnCount; j++)
                {
                    result[i, j] = (A[i].PointwiseMultiply(B[j])).Sum();
                }
            }

            return result.Multiply(1f/(data.RowCount - 1));
        }

		/// <summary>
		/// Calculates the column-wise mean of a <see cref="T:MathNet.Numerics.LinearAlgebra"/> matrix.
		/// </summary>
		/// <returns>The column-wise mean.</returns>
		/// <param name="input">Input matrix.</param>
		Vector<float> ColumnWiseMean(Matrix<float> input)
        {
            Vector<float> result = Vector<float>.Build.Dense(input.ColumnCount);

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
        void WriteEigenvectorsToFile(string filename)
        {
            Debug.WriteLine("Writing to " + filename);
            using (BinaryWriter writer = new BinaryWriter(new FileStream(filename, FileMode.Create)))
            {
                // mean should be of length 3042
                for (int a = 0; a < patchsize; a++)
                {
                    writer.Write(mean[a]);
                }

                // eigvecs should be 3042x3042
                for (int i = 0; i < patchsize; i++)
                {
                    for (int j = 0; j < patchsize; j++)
                    {
                        writer.Write(eigvecs[i, j]);
                    }
                }
            }

            Debug.WriteLine("Wrote to " + filename);
        }
    }
}
