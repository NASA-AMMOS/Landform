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
using System.Threading.Tasks;
using System;

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
        Matrix<float> principalEigVecs;
        Vector<double> principalEigVals;

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
            Matrix<float> data = Matrix<float>.Build.Dense(gradients.Count, patchlen); // inf x 3042

            // convert list of gradient vectors into data matrix of size inf x 3042
            for (int i = 0; i < gradients.Count(); i++)
            {
                data.SetRow(i, Vector<float>.Build.Dense(gradients[i]));
            }
            
            // Calculate column-wise mean
            mean = ColumnWiseMean(data); // should be length 3042

            // calculate covariance matrix
            Trace.WriteLine("Calculating covariance matrix...");
            Matrix<float> covar = CovarianceMatrix(data);

            // eigen decomposition
            Trace.WriteLine("Calculating eigen decomposition...");
            eigs = covar.Evd();
            eigvecs = eigs.EigenVectors;
            Vector<double> eigvals = eigs.EigenValues.Real();
            ReOrderEigenvectorMatrix(eigvecs, eigvals);

            principalEigVecs = eigvecs.SubMatrix(0, eigvecs.RowCount, 0, n);
            principalEigVals = eigs.EigenValues.Real().SubVector(0, n);

            Trace.WriteLine(principalEigVecs);
            Trace.WriteLine(principalEigVals);

            WriteEigenvectorsToFile(gpcafile + ".txt");
        }

        void ReOrderEigenvectorMatrix(Matrix<float> eigvecs, Vector<double> eigvals)
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
            List<float[]> gradients = new List<float[]>();

            Parallel.For(0, imageFiles.Count(), i => { 
                gradients.AddRange(CalculateGradients(imageFiles[i]));
            });

            
            ComputeEigenspace(gradients);
        }

        /// <summary>
        /// Calculates the gradients for the given image and appends them to running gradients list.
        /// </summary>
        /// <returns>The updated gradients lsit.</returns>
        /// <param name="imageFile">Image file.</param>
        /// <param name="gradients">Running list of gradients.</param>
        List<float[]> CalculateGradients(string imageFile)
        {
            List<float[]> gradients = new List<float[]>();
            Emgu.CV.Image<Gray, byte> modelImage = Imaging.Image.Load(imageFile).ToEmguGrayscale();
            Emgu.CV.Image<Gray, float> grayModelImage = modelImage.Convert<Gray, float>();
            SIFT sift = new SIFT();
            MKeyPoint[] mKeypoints = sift.Detect(modelImage);
            List<PCA_Keypoint> PCAKeypoints = PCA_KeypointDetector.GetPatches(grayModelImage, mKeypoints, patchsize + 2);
            gradients.AddRange(PCA_KeypointDetector.GetGradients(PCAKeypoints));

            return gradients;
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

            Parallel.For(0, data.ColumnCount, i =>
            {
                vec = data.Column(i);
                B[i] = (vec.Subtract((float)vec.Mean()));
                A[i] = (B[i].Conjugate());
                result[i, i] = (A[i].PointwiseMultiply(B[i])).Sum();
            });

            float resultNum;
            float coeff = 1f / (data.RowCount - 1);

            Parallel.For(0, data.ColumnCount, i =>
            {
                for (int j = i; j < data.ColumnCount; j++)
                {
                    resultNum = (A[i].PointwiseMultiply(B[j])).Sum() * coeff;
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
            Debug.Assert(input.ColumnCount == patchlen);

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
            Trace.WriteLine("Writing eigenvectors to " + filename);
            using (BinaryWriter writer = new BinaryWriter(new FileStream(filename, FileMode.Create)))
            {
                // mean should be of length 3042
                for (int a = 0; a < 3042; a++)
                {
                    if (float.IsNaN(mean[a]))
                    {
                        Trace.WriteLine("NaN in mean :(");
                    }
                    writer.Write(mean[a]);
                }

                // eigvecs should be 3042x36
                for (int i = 0; i < 3042; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        writer.Write(principalEigVecs[i, j]);
                        if (float.IsNaN(principalEigVecs[i, j]))
                        {
                            Trace.WriteLine("NaN in eigvecs :(");
                        }
                    }
                }
            }

            Trace.WriteLine("Wrote to " + filename);
        }
    }
}
