using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV.UI;
using Emgu.CV.Util;
using Emgu.Util;
using Emgu.CV.CvEnum;
using Microsoft.Xna.Framework;
using Emgu.CV.XFeatures2D;
using Emgu.CV.Features2D;
using System.Drawing;
using OPS.Imaging.Emgu;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Emgu.CV.Structure;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Factorization;
using MathNet.Numerics.Statistics;

namespace OPS.Alignment
{
    public class PCA_Train
    {
        private const int n = 36;
        private const int patchsize = 39;
        private const int patchlen = 3042; // = patchsize * patchsize * 2;
        string gpcafile;
        Vector<float> mean;// { get; set; }
        Evd<float> eigs;// { get; set; }
        Matrix<float> eigvecs;// { get; set; }

        public PCA_Train(string filename)
        {
            gpcafile = filename;
            ////List<PCA_Keypoint> keypoints = PCA_KeypointDetector.readPatchesFromFile(filename);
            ////Image<Gray, float> data = keypoints[0].patch;
            //Matrix<float> data = null;
            //using (BinaryReader reader = new BinaryReader(new FileStream(filename, FileMode.Open)))
            //{
            //    double numKeypoints = reader.ReadSingle();
            //    data = Matrix<float>.Build.Dense(patchlen, (int)numKeypoints);
            //    int count = 0;
            //    while (reader.BaseStream.Position != reader.BaseStream.Length)
            //    {
            //        for (int i = 0; i < patchlen; i++)
            //        {
            //            data[i, count++] = reader.ReadSingle();
            //        }
            //        //Matrix<float> newData = new Matrix<float>(patchlen, 1);
            //        //for (int i = 0; i < patchlen; i++)
            //        //{
            //        //    newData[i, 0] = reader.ReadSingle();
            //        //}
            //        //if (data == null)
            //        //{
            //        //    data = newData.Clone();
            //        //}
            //        //else
            //        //{
            //        //    data = data.ConcateHorizontal(newData);
            //        //}
            //    }
            //}


            //mean = Vector<float>.Build.Dense(patchlen);
            ////eigs = Matrix<float>.Build.Dense(patchlen, patchlen);

            //Matrix<float> covar = Matrix<float>.Build.Dense(patchlen, patchlen);
            //try
            //{
            //    //CvInvoke.CalcCovarMatrix(data, covar, mean, CovarMethod.Normal | CovarMethod.Cols, DepthType.Cv32F);
            //    Debug.WriteLine(covar.ToString());
            //}
            //catch (Exception e)
            //{
            //    Debug.WriteLine(e, e.StackTrace);
            //}
            ////Matrix<float> eigvals = new Matrix<float>(patchlen, 1);
            ////CvInvoke.Eigen(covar, eigvals);
            ////covar = covar / (data.Cols - 1); // purpose of this line?
            //Debug.WriteLine("DONE?");
            //writeEigsToFile("C:\\Users\\charchut\\Downloads\\eigs.txt");
        }

        void processGradients(List<float[]> gradients)
        {
            Matrix<float> data = Matrix<float>.Build.Dense(gradients.Count(), patchlen);

            // convert list of gradient vectors into data matrix
            for (int i = 0; i < gradients.Count(); i++)
            {
                data.SetRow(i, Vector<float>.Build.Dense(gradients[i]));
            }

            // row-wise mean
            mean = columnWiseMean(data);

            // calculate covariance matrix
            Matrix<float> covar = covarianceMatrix(data);

            // eigendecomposition
            eigs = covar.Evd();
            eigvecs = eigs.EigenVectors;
            Matrix<float> principalVecs = Matrix<float>.Build.Dense(eigvecs.RowCount, n);
            principalVecs = eigvecs.SubMatrix(0, eigvecs.RowCount, 0, n);
            Vector<double> principalVals = Vector<double>.Build.Dense(n);
            principalVals = eigs.EigenValues.Real().SubVector(0, n);

            Debug.WriteLine(principalVecs);
            Debug.WriteLine(principalVals);

            writeEigsToFile(gpcafile);
        }

        public void Train(string path)
        {
            string[] imageFiles = Directory.GetFiles(path, "*.jpg");
            List<float[]> gradients = new List<float[]>();

            for (int i = 0; i < imageFiles.Length; i++)
            {
                gradients = processImage(imageFiles[i], gradients);
            }

            processGradients(gradients);
        }

        private List<float[]> processImage(string imageFile, List<float[]> gradients)
        {
            Emgu.CV.Image<Gray, byte> modelImage = Imaging.Image.Load(imageFile).ToEmguGrayscale();
            Emgu.CV.Image<Gray, float> grayModelImage = modelImage.Convert<Gray, float>();
            SIFT sift = new SIFT();
            MKeyPoint[] mKeypoints = sift.Detect(modelImage);
            List<PCA_Keypoint> PCAKeypoints = PCA_KeypointDetector.getPatches(grayModelImage, mKeypoints, patchsize + 2);
            gradients.AddRange(PCA_KeypointDetector.getGradients(PCAKeypoints));

            return gradients;
        }

        Matrix<float> covarianceMatrix(Matrix<float> data)
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

        Vector<float> columnWiseMean(Matrix<float> input)
        {
            Vector<float> result = Vector<float>.Build.Dense(input.ColumnCount);

            for (int i = 0; i < input.ColumnCount; i++)
            {
                result[i] = (float)input.Column(i).Mean();
            }

            return result;
        }

        private void writeEigsToFile(string filename)
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

            // write to text file for debugging
            //using (StreamWriter writer = new StreamWriter(new FileStream(filename, FileMode.Create)))
            //{
            //    StringBuilder builder = new StringBuilder();
            //    for (int a = 0; a < patchsize + 1; a++)
            //    {
            //        //writer.WriteLine(mean[a, 0]);
            //        builder.AppendLine(mean[a].ToString());
            //    }

            //    for (int i = 0; i < patchsize; i++)
            //    {
            //        for (int j = 0; j < patchsize; j++)
            //        {
            //            //writer.WriteLine(eigvecs[i, j]);
            //            builder.AppendLine(eigvecs[i, j].ToString());
            //        }
            //    }
            //    writer.Write(builder.ToString());
            //}
            Debug.WriteLine("Wrote to " + filename);
        }
    }
}
