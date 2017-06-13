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
        const int n = 36;
        const int patchsize = 39;
        const int patchlen = patchsize * patchsize * 2;
        Matrix<float> mean { get; set; }
        Evd<float> eigs { get; set; }
        Matrix<float> eigvecs { get; set; }

        public PCA_Train(string filename)
        {
            //List<PCA_Keypoint> keypoints = PCA_KeypointDetector.readPatchesFromFile(filename);
            //Image<Gray, float> data = keypoints[0].patch;
            Matrix<float> data = null;
            using (BinaryReader reader = new BinaryReader(new FileStream(filename, FileMode.Open)))
            {
                double numKeypoints = reader.ReadSingle();
                data = Matrix<float>.Build.Dense(patchlen, (int)numKeypoints);
                int count = 0;
                while (reader.BaseStream.Position != reader.BaseStream.Length)
                {
                    for (int i = 0; i < patchlen; i++)
                    {
                        data[i, count++] = reader.ReadSingle();
                    }
                    //Matrix<float> newData = new Matrix<float>(patchlen, 1);
                    //for (int i = 0; i < patchlen; i++)
                    //{
                    //    newData[i, 0] = reader.ReadSingle();
                    //}
                    //if (data == null)
                    //{
                    //    data = newData.Clone();
                    //}
                    //else
                    //{
                    //    data = data.ConcateHorizontal(newData);
                    //}
                }
            }


            mean = Matrix<float>.Build.Dense(patchlen, 1);
            //eigs = Matrix<float>.Build.Dense(patchlen, patchlen);

            Matrix<float> covar = Matrix<float>.Build.Dense(patchlen, patchlen);
            try
            {
                //CvInvoke.CalcCovarMatrix(data, covar, mean, CovarMethod.Normal | CovarMethod.Cols, DepthType.Cv32F);
                Debug.WriteLine(covar.ToString());
            }
            catch (Exception e)
            {
                Debug.WriteLine(e, e.StackTrace);
            }
            //Matrix<float> eigvals = new Matrix<float>(patchlen, 1);
            //CvInvoke.Eigen(covar, eigvals);
            //covar = covar / (data.Cols - 1); // purpose of this line?
            Debug.WriteLine("DONE?");
            writeEigsToFile("C:\\Users\\charchut\\Downloads\\eigs.txt");
        }

        public PCA_Train(List<float[]> gradients)
        {
            Matrix<float> data = Matrix<float>.Build.Dense(patchlen, gradients.Count());

            // convert list of vectors into matrix

            for (int i = 0; i < gradients.Count(); i++)
            {
                data.SetColumn(i, Vector<float>.Build.Dense(gradients[i]));
            }

            data = data.Transpose();
            // test for covariance
            //Matrix<float> test = Matrix<float>.Build.Dense(3, 4);
            //test.SetRow(0, new float[] { 5, 0, 3, 7 });
            //test.SetRow(1, new float[] { 1, -5, 7, 3 });
            //test.SetRow(2, new float[] { 4, 9, 8, 10 });

            //Matrix<float> testCovar = covarianceMatrix(test);
            //Debug.WriteLine(testCovar);

            // row-wise mean
            mean = Matrix<float>.Build.Dense(patchlen, 1);
            mean = rowwiseMean(data);

            // calculate covariance matrix
            Matrix<float> covar = covarianceMatrix(data);

            // eigendecomposition
            eigs = covar.Evd();
            eigvecs = eigs.EigenVectors;
            Debug.WriteLine(eigvecs);
    
        }

        Matrix<float> covarianceMatrix(Matrix<float> data)
        {
            Matrix<float> result = Matrix<float>.Build.Dense(data.ColumnCount, data.ColumnCount);
            Dictionary<int, Vector<float>> A = new Dictionary<int, Vector<float>>();
            Dictionary<int, Vector<float>> B = new Dictionary<int, Vector<float>>();
            Dictionary<int, float> mu = new Dictionary<int, float>();

            // precompute A, B in cov(A, B)
            for (int i = 0; i < data.ColumnCount; i++)
            {
                Vector<float> vec = data.Column(i);
                A[i] = vec.Clone().Subtract((float)vec.Mean()).Conjugate();
                B[i] = vec.Clone().Subtract((float)vec.Mean());
            }

            for (int i = 0; i < result.ColumnCount; i++)
            {
                for (int j = 0; j < result.ColumnCount; j++)
                {
                    result[i, j] = (A[i].PointwiseMultiply(B[j])).Sum();
                }
            }

            return result.Multiply(1f/(data.RowCount - 1));
        }

        Matrix<float> rowwiseMean(Matrix<float> input)
        {
            Matrix<float> result = Matrix<float>.Build.Dense(input.RowCount, 1);

            for (int i = 0; i < input.RowCount; i++)
            {
                result[i, 0] = (float)input.Row(i).Mean();
            }
            return result;
        }


        public void writeEigsToFile(string filename)
        {
            Debug.WriteLine("Writing to " + filename);
            using (BinaryWriter writer = new BinaryWriter(new FileStream(filename, FileMode.Create)))
            {
                for (int a = 0; a < patchsize + 1; a++)
                {
                    writer.Write(mean[a, 0]);
                }

                for (int i = 0; i < patchsize + 1; i++)
                {
                    for (int j = 0; j < patchsize + 1; j++)
                    {
                        writer.Write(eigvecs[i, j]);
                    }
                }
            }

            using (StreamWriter writer = new StreamWriter(new FileStream(filename, FileMode.Create)))
            {
                for (int a = 0; a < patchsize + 1; a++)
                {
                    writer.WriteLine(mean[a, 0]);
                }

                for (int i = 0; i < patchsize + 1; i++)
                {
                    for (int j = 0; j < patchsize + 1; j++)
                    {
                        writer.WriteLine(eigvecs[i, j]);
                    }
                }
            }
        }
    }
}
