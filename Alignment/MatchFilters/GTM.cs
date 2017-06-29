using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace OPS.Alignment
{
    public class GTM : IMatchFilter
    {
        public int MaxIterations;
        public bool RefineStep;
        public GTM(int maxIterations = 5000, bool refineStep = true)
        {
            MaxIterations = maxIterations;
            RefineStep = refineStep;
        }

        public ImagePairCorrespondence Filter(ImagePairCorrespondence matches)
        {
            // assumption: matched model features and data features occur in the same order of corresponding
            //             ImageCorrespondence fields, need to fix this using DataToModel???
            KeyValuePair<int, int>[] pairs = matches.DataToModel;

            int K = 5, outlier;
            ImageFeature[] P = matches.ModelFeatures;
            ImageFeature[] PPrime = matches.DataFeatures;
            double[][] DistP = ComputeDistanceMatrix(P);
            double[][] DistPPrime = ComputeDistanceMatrix(PPrime);
            double MedianP = ComputeMedian(DistP);
            double MedianPPrime = ComputeMedian(DistPPrime);
            ImageFeature[] Q = (ImageFeature[])P.Clone();
            ImageFeature[] QPrime = (ImageFeature[])PPrime.Clone();

            double[][] AP = BuildMedianKNNGraph(DistP, K, MedianP);
            double[][] APPrime = BuildMedianKNNGraph(DistPPrime, K, MedianPPrime);

            while (!AP.SequenceEqual(APPrime))
            {
                outlier = FindOutlier(AP, APPrime);
                RemoveOutlier(outlier, DistP, DistPPrime, Q, QPrime);
                AP = BuildMedianKNNGraph(DistP, K, MedianP);
                APPrime = BuildMedianKNNGraph(DistPPrime, K, MedianPPrime);
            }

            Q = RemoveDisconnectedVertices(Q, AP);
            QPrime = RemoveDisconnectedVertices(QPrime, APPrime);

            System.Diagnostics.Debug.WriteLine("Number of residual matches: " + goodMatches.Count);

            return new ImagePairCorrespondence(
                matches.ModelImage, matches.DataImage,
                matches.ModelFeatures, matches.DataFeatures,
                goodMatches);
        
        }

        /// <summary>
        /// Computes the median of a 2D-array in linear time.
        /// </summary>
        /// <param name="arr">Input array.</param>
        /// <returns>Median of input array.</returns>
        double ComputeMedian(double[][] arr)
        {
            List<double> A = new List<double>();
            
            // Flatten the array before beginning the algorithm.
            for (int j = 0; j < arr.Length; j++)
            {
                for (int k = j + 1; k < arr.Length; k++)
                {
                    A.Add(arr[j][k]);
                    A.Add(arr[k][j]);
                }
            }

            int i = A.Count / 2;
            return MedianOfMedians(A, i);
        }

        /// <summary>
        /// Prunes erroneous features.
        /// </summary>
        /// <param name="features">Input list of features.</param>
        /// <param name="graph">Adjacency matrix representation of graph.</param>
        /// <returns>A copy of pruned features.</returns>
        private ImageFeature[] RemoveDisconnectedVertices(ImageFeature[] features, double[][] graph)
        {
            ImageFeature[] result = (ImageFeature[])features.Clone();
            ImageFeature deletedFeat = new ImageFeature(new Vector2(-1, -1), null);
            List<double> rowsums = graph.Select(x => x.Sum()).ToList();
            for (int i = 0; i < rowsums.Count; i++)
            {
                if (rowsums[i] == 0)
                {
                    result[i] = deletedFeat;
                }
            }

            Vector2 deleted = new Vector2(-1, -1);
            return result.Where(x => !x.Location.Equals(deleted)).ToArray();
        }

        /// <summary>
        /// Removes an outlier from both adjacency graphs, as well as lists of features. 
        /// </summary>
        /// <param name="outlier">Index of feature to remove.</param>
        /// <param name="distP">Adjacency matrix of model image features.</param>
        /// <param name="distPPrime">Adjacency matrix of data image features.</param>
        /// <param name="Q">List of features present in model image.</param>
        /// <param name="QPrime">List of features present in data image.</param>
        private void RemoveOutlier(int outlier, double[][] distP, double[][] distPPrime, ImageFeature[] Q, ImageFeature[] QPrime)
        {
            for (int i = 0; i < distP.Length; i++)
            {
                distP[outlier][i] = -1;
                distP[i][outlier] = -1;
                distPPrime[outlier][i] = -1;
                distPPrime[i][outlier] = -1;
            }

            Q[outlier] = new ImageFeature(new Vector2(-1, -1), null);
            QPrime[outlier] = new ImageFeature(new Vector2(-1, -1), null);
        }

        int FindOutlier(double[][] AP, double[][] APPrime)
        {
            double[][] R = new double[AP.Length][].Select(x => new double[AP.Length]).ToArray();
            double diff;

            for (int i = 0; i < AP.Length; i++)
            {
                for (int j = i; j < AP.Length; j++)
                {
                    diff = Math.Abs(AP[i][j] - APPrime[i][j]);
                    R[i][j] = diff;
                    R[j][i] = diff;
                }
            }

            List<double> rowsums = R.Select(x => x.Sum()).ToList();

            return rowsums.IndexOf(rowsums.Max());
        }

        /// <summary>
        /// Brute force implementation of adjacency graph building.
        /// </summary>
        /// <param name="dist"></param>
        /// <param name="k"></param>
        /// <param name="median"></param>
        /// <returns></returns>
        double[][] BuildMedianKNNGraph(double[][] dist, int k, double median)
        {
            double[][] result = new double[dist.Length][].Select(x => new double[dist.Length]).ToArray();

            for (int i = 0; i < result.Length; i++)
            {
                int count = 0;
                var distances = dist[i].Where(w => w > 0).Select(x => new { Value = x, Index = count++ }).OrderBy(v => v.Value).ToList();

                for (int ki = 0; ki < k; ki++)
                {
                    int index = 0;
                    try
                    {
                        index = distances[ki].Index;
                        if (distances[ki].Value <= median)
                        {
                            result[i][index] = 1;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e, e.StackTrace);
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Find the i-th smallest element in a given array.
        /// </summary>
        /// <param name="A">Input array.</param>
        /// <param name="i">Ordinality of desired element.</param>
        /// <returns></returns>
        double MedianOfMedians(List<double> A, int i)
        {
            List<List<double>> sublists = new List<List<double>>();
            List<double> medians = new List<double>();
            double pivot;
            int k;

            // Break into sublists
            for (int j = 0; j < A.Count; j += 5)
            {
                k = j + 5 > A.Count - 1 ? A.Count - 1 : j + 5; // checking array bounds
                sublists.Add(A.GetRange(j, k));
            }

            foreach (List<double> sublist in sublists)
            {
                sublist.Sort();
                medians.Add(sublist[sublist.Count / 2]);
            }

            // Identify pivot
            if (medians.Count <= 5)
            {
                medians.Sort();
                pivot = medians[medians.Count / 2];
            }
            else
            {
                pivot = MedianOfMedians(medians, medians.Count / 2);
            }

            List<double> low = A.Where(x => x < pivot).ToList();
            List<double> high = A.Where(x => x > pivot).ToList();

            int m = low.Count;
            if (i < m) return MedianOfMedians(low, i);
            else if (i > m) return MedianOfMedians(high, i - m - 1);
            else return pivot;
        }

        /// <summary>
        /// Computes adjacency matrix of distances from list of image features.
        /// </summary>
        /// <param name="features">Input list of features.</param>
        /// <returns>Adjacency graph of distances.</returns>
        double[][] ComputeDistanceMatrix(ImageFeature[] features)
        {
            Vector2[] coords = features.Select(v => v.Location).ToArray();
            double[][] result = new double[features.Length][].Select(x => new double[features.Length]).ToArray();

            Vector2 v1, v2;
            double dist;

            for (int j = 0; j < features.Length; j++)
            {
                v2 = coords[j];
                for (int k = j + 1; k < features.Length; k++)
                {
                    v1 = coords[k];

                    // L2 Norm
                    dist = Math.Sqrt(Math.Pow(v1.X - v2.X, 2) + Math.Pow(v1.Y - v2.Y, 2));
                    result[j][k] = dist;
                    result[k][j] = dist;
                }
            }          
            return result;
        }
    }
}

