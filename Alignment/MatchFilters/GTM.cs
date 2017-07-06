﻿﻿using Microsoft.Xna.Framework;
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
        public int K;

        public GTM(int k = 5)
        {
            K = k;
        }

        public ImagePairCorrespondence Filter(ImagePairCorrespondence matches)
        {
            KeyValuePair<int, int>[] pairs = matches.DataToModel;
            ImageFeature[] modelFeat = matches.ModelFeatures;
            ImageFeature[] dataFeat = matches.DataFeatures;

            ImageFeature zero = new ImageFeature(new Vector2(0, 0), null);
            ImageFeature[] P = new ImageFeature[pairs.Length];
            ImageFeature[] PPrime = new ImageFeature[pairs.Length];

            // Create sets P and PPrime, where P[i] and PPrime[i] are matched features
            for (int i = 0; i < pairs.Length; i++) {
                P[i] = modelFeat[pairs[i].Value];
                PPrime[i] = dataFeat[pairs[i].Key];
            }

            int counter = 0;
            int outlier;


            // DEBUG
            //ImageFeature u1 = new ImageFeature(new Vector2(0, 3), null);
            //ImageFeature u2 = new ImageFeature(new Vector2(1, 2), null);
            //ImageFeature u3 = new ImageFeature(new Vector2(3, 0.9), null);
            //ImageFeature u4 = new ImageFeature(new Vector2(1, 3), null);
            //ImageFeature u5 = new ImageFeature(new Vector2(3, 3), null);
            //ImageFeature u6 = new ImageFeature(new Vector2(2, 4), null);

            //ImageFeature[] testSet = new ImageFeature[] { u1, u2, u3, u4, u5, u6 };
            //double[][] dist = ComputeDistanceMatrix(testSet);
            //int[][] Ot = InitMedianKNNGraphOptimized(dist, 2);
            //List<int>[] It = InitNeighborVector(Ot, 2);
            //List<int> Ct = InitNextVector(testSet.Length);
            //for (int kl = 0; kl < testKNN.Length; kl++)
            //{
            //    for (int oi = 0; oi < testKNN.Length; oi++)
            //    {
            //        Debug.Write(String.Format("{0:0.0}", dist[kl][oi]) + " ");
            //    }
            //    Debug.WriteLine("\n");
            //}
            //Debug.WriteLine("-------");
            //for (int kl = 0; kl < testKNN.Length; kl++)
            //{
            //    for (int oi = 0; oi < testKNN.Length; oi++)
            //    {
            //        Debug.Write(testKNN[kl][oi] + " ");
            //    }
            //    Debug.WriteLine("");
            //}
            //foreach (int num in Ct)
            //{

            //    Debug.WriteLine(num + " ");
            //}

            //Debug.WriteLine("please work");
            // DEBUG


            // <OPTIMIZED
            //double[][] DistP = ComputeDistanceMatrix(P);
            //double[][] DistPPrime = ComputeDistanceMatrix(PPrime);
            //double MedianP = ComputeMedian(DistP);
            //double MedianPPrime = ComputeMedian(DistPPrime);
            //int[][] O = InitMedianKNNGraphOptimized(DistP, MedianP);
            //int[][] OPrime = InitMedianKNNGraphOptimized(DistPPrime, MedianPPrime);
            //List<int>[] I = InitNeighborVector(O);
            //List<int>[] IPrime = InitNeighborVector(OPrime);
            //int[] C = new int[pairs.Length].Select(x => K + 1).ToArray();
            //int[] CPrime = new int[pairs.Length].Select(x => K + 1).ToArray();
            //HashSet<int> outliers = new HashSet<int>();

            //while (!GraphEqual(O, OPrime))
            //{
            //    counter++;
            //    outlier = FindOutlier(O, OPrime, I, IPrime);

            //    if (outliers.Contains(outlier))
            //        break;

            //    outliers.Add(outlier);

            //    RemoveOutlier(outlier, O, OPrime, I, IPrime, C, CPrime);
            //    printIntermediates(O, I, C);
                    
            //}
            //return new ImagePairCorrespondence(matches.ModelImage, matches.DataImage, null, null, new KeyValuePair<int, int>[0]);


            // /OPTIMIZED>

            double[][] DistP = ComputeDistanceMatrix(P);
            double[][] DistPPrime = ComputeDistanceMatrix(PPrime);
            double MedianP = ComputeMedian(DistP);
            double MedianPPrime = ComputeMedian(DistPPrime);
            ImageFeature[] Q = (ImageFeature[])P.Clone();
            ImageFeature[] QPrime = (ImageFeature[])PPrime.Clone();
            HashSet<int> outliers = new HashSet<int>();

            double[][] AP = BuildMedianKNNGraph(DistP, K, MedianP, outliers);
            double[][] APPrime = BuildMedianKNNGraph(DistPPrime, K, MedianPPrime, outliers);


            while (!GraphEqual(AP, APPrime))
            {
                counter++;
                outlier = FindOutlier(AP, APPrime, outliers);

                if (outliers.Contains(outlier))
                {
                    break;
                }
                outliers.Add(outlier);
                //RemoveOutlier(outlier, DistP, DistPPrime, Q, QPrime);
                AP = BuildMedianKNNGraph(DistP, K, MedianP, outliers);
                APPrime = BuildMedianKNNGraph(DistPPrime, K, MedianPPrime, outliers);
            }

            Q = RemoveDisconnectedVertices(Q, AP);
            QPrime = RemoveDisconnectedVertices(QPrime, APPrime);

            if (Q.Length != QPrime.Length)
                throw new Exception("Matched features not equal in length.");

            KeyValuePair<int, int>[] goodMatches = ConstructFinalMatches(Q.Length);
                                                                              
            Debug.WriteLine("Number of residual matches: " + goodMatches.Length + " after " + counter + " iterations of GTM");

            return new ImagePairCorrespondence(
                matches.ModelImage, matches.DataImage,
                Q, QPrime,
                goodMatches);
      }

        private void printIntermediates(int[][] o, List<int>[] i, int[] c)
        {
            foreach (int[] row in o)
            {
                for (int j = 0; j < o.Length; j++)
                {
                    // TODO
                }
            }
        }

        private void RemoveOutlier(int outlier, int[][] o, int[][] oPrime, List<int>[] i, List<int>[] iprime, int[] c, int[] cPrime)
        {
            // Remove from I and IPrime
            i[outlier] = null;
            iprime[outlier] = null;

            foreach (List<int> list in i)
            {
                for (int j = 0; j < list.Count; j++)
                {
                    if (list[j] == outlier)
                        list[j] = -1;
                }
            }

            foreach (List<int> list in iprime)
            {
                for (int j = 0; j < list.Count; j++)
                {
                    if (list[j] == outlier)
                        list[j] = -1;
                }
            }

            // Remove from O and OPrime
            o[outlier][0] = -1;
            oPrime[outlier][0] = -1;

            // Reconnect edges in C and CPrime

            for (int m = 0; m < o.Length; m++)
            {
                int[] row = o[m];
                for (int k = 0; k < K; k++)
                {
                    if (row[k] == outlier)
                    {
                        try
                        {
                            row[k] = i[m][c[m]];
                            c[m]++;
                        }
                        catch (IndexOutOfRangeException e)
                        {
                            continue;
                        }
                    }
                }
            }

            for (int m = 0; m < oPrime.Length; m++)
            {
                int[] row = oPrime[m];
                for (int k = 0; k < K; k++)
                {
                    if (row[k] == outlier)
                    {
                        try
                        {
                            row[k] = i[m][c[m]];
                            c[m]++;
                        }
                        catch (IndexOutOfRangeException e)
                        {
                            continue;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks if the two graphs contain equivalent values.
        /// </summary>
        /// <param name="A"></param>
        /// <param name="AP"></param>
        /// <returns></returns>
        bool GraphEqual(double[][] A, double[][] AP)
        {
            for (int i = 0; i < A.Length; i++)
            {
                for (int j = 0; j < A.Length; j++)
                {
                    if (Math.Abs(A[i][j] - AP[i][j]) > double.Epsilon)
                        return false;
                }
            }
            return true;
        }

        bool GraphEqual(int[][] A, int[][] AP)
        {
            for (int i = 0; i < A.Length; i++)
            {
                for (int j = 0; j < A.Length; j++)
                {
                    if (Math.Abs(A[i][j] - AP[i][j]) > double.Epsilon)
                        return false;
                }
            }
            return true;
        }




        /// <summary>
        /// Constructs the final matches as i:i mappings for i in [0, length).
        /// </summary>
        /// <returns>The final matches.</returns>
        /// <param name="length">Length of mapping.</param>
        KeyValuePair<int, int>[] ConstructFinalMatches(int length)
        {
            KeyValuePair<int, int>[] result = new KeyValuePair<int, int>[length];
            for (int i = 0; i < length; i++) {
                result[i] = new KeyValuePair<int, int>(i, i);
            }
            return result;
        }

        /// <summary>
        /// Computes the median of a 2D-array.
        /// </summary>
        /// <param name="arr">Input array.</param>
        /// <returns>Median of input array.</returns>
        double ComputeMedian(double[][] arr, bool linear = false)
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
            if (linear)
            {
                return MedianOfMedians(A, i);
            }

            A.Sort();
            return A[A.Count / 2];
        }

        /// <summary>
        /// Prunes erroneous features.
        /// </summary>
        /// <param name="features">Input list of features.</param>
        /// <param name="graph">Adjacency matrix representation of graph.</param>
        /// <returns>A copy of pruned features.</returns>
        ImageFeature[] RemoveDisconnectedVertices(ImageFeature[] features, double[][] graph)
        {
            ImageFeature[] result = (ImageFeature[])features.Clone();
            ImageFeature deletedFeat = new ImageFeature(new Vector2(-1, -1), null);
            List<double> rowsums = graph.Select(x => x.Sum()).ToList();
            for (int i = 0; i < rowsums.Count; i++)
            {
                if (Math.Abs(rowsums[i]) < double.Epsilon)
                    result[i] = deletedFeat;
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
                distP[outlier][i] = 0;
                distP[i][outlier] = 0;
                distPPrime[outlier][i] = 0;
                distPPrime[i][outlier] = 0;
            }

            Q[outlier] = new ImageFeature(new Vector2(-1, -1), null);
            QPrime[outlier] = new ImageFeature(new Vector2(-1, -1), null);
        }

        int FindOutlier(double[][] AP, double[][] APPrime, HashSet<int> outliers)
        {
            double[][] R = new double[AP.Length][].Select(x => new double[AP.Length]).ToArray();
            double diff;

            for (int i = 0; i < AP.Length; i++)
            {
                if (outliers.Contains(i)) continue;

                for (int j = 0; j < AP.Length; j++)
                {
                    if (outliers.Contains(j)) continue;
                    diff = Math.Abs(AP[i][j] - APPrime[i][j]);
                    R[i][j] = diff;
                }
            }

            List<double> rowsums = R.Select(x => x.Sum()).ToList();

            return rowsums.IndexOf(rowsums.Max());
        }

        int FindOutlier(int[][] O, int[][] OPrime, List<int>[] I, List<int>[] IPrime)
        {
            HashSet<int> A = new HashSet<int>();
            HashSet<int> APrime = new HashSet<int>();
            int maxcount = -1;
            IEnumerable<int> possibleOutliers;
            int outlier = -1;

            for (int i = 0; i < O.Length; i++)
            {
                foreach (int num in I[i])
                    A.Add(num);
                foreach (int num in IPrime[i])
                    APrime.Add(num);

                for (int k = 0; k < K; k++)
                {
                    A.Add(O[i][k]);
                    APrime.Add(OPrime[i][k]);
                }

                possibleOutliers = A.Except(APrime).Union(APrime.Except(A));
                if (possibleOutliers.Where(number => number > -1).Count() > maxcount)
                {
                    maxcount = possibleOutliers.Count();
                    outlier = possibleOutliers.First();
                }
                A.Clear();
                APrime.Clear();
            }

            return outlier;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="dist"></param>
        /// <param name="median"></param>
        /// <returns></returns>
        int[][] InitMedianKNNGraphOptimized(double[][] dist, double median)
        {
            int[][] res = new int[dist.Length][].Select(x => new int[dist.Length]).ToArray();

            for (int i = 0; i < dist.Length; i++) {
                int j = 0;
                res[i] = dist[i].Select(x => new { Value = x, Index = j++ })
                                .Where(y => y.Value > 0)
                                .OrderBy(v => v.Value)
                                .Select(w => w.Index)
                                .Concat(new int[] { -2 }).ToArray();
            }

            return res;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="knnGraph"></param>
        /// <returns></returns>
        List<int>[] InitNeighborVector(int[][] knnGraph, int k = 5)
        {
            List<int>[] res = new List<int>[knnGraph.Length].Select(x => new List<int>()).ToArray();
            int[] vertexCount = new int[knnGraph.Length];

            for (int i = 0; i < knnGraph.Length; i++)
            {
                for (int j = 0; j < k; j++)
                {
                    res[knnGraph[i][j]].Add(i);
                }
            }
            return res;
        }

        List<int> InitNextVector(int len)
        {
            return Enumerable.Repeat(K + 1, len).ToList();
        }

        /// <summary>
        /// Brute force implementation of adjacency graph building.
        /// </summary>
        /// <param name="dist"></param>
        /// <param name="k"></param>
        /// <param name="median"></param>
        /// <returns></returns>
        double[][] BuildMedianKNNGraph(double[][] dist, int k, double median, HashSet<int> outliers)
        {
            double[][] result = new double[dist.Length][].Select(x => new double[dist.Length]).ToArray();

            for (int i = 0; i < result.Length; i++)
            {

                if (outliers.Contains(i)) continue;
                int count = 0;
                var distances1 = dist[i].Select(x => new { Value = x, Index = count++ })
                                        .Where(y => y.Value > 0 && !outliers.Contains(y.Index)) // not itself or outlier
                                        .OrderBy(v => v.Value).ToArray();

                for (int ki = 0; ki < k; ki++)
                {
                    try
                    {
                        int index = distances1[ki].Index;
                        if (distances1[ki].Value <= median)
                        {
                            result[i][index] = 1;
                            result[index][i] = 1;
                        }
                    }
                    catch (Exception e)
                    {
                        break;
                    }
                }
            }

            return result;
        }

        double[][] BuildMedianKNNGraph(double[][] dist, int k)
        {
            double[][] result = new double[dist.Length][].Select(x => new double[dist.Length]).ToArray();

            for (int i = 0; i < result.Length; i++)
            {
                int count = 0;

                var distances1 = dist[i].Select(x => new { Value = x, Index = count++ })
                                       .Where(y => y.Value > 0) // not itself
                                       .OrderBy(v => v.Value).ToList();

                for (int ki = 0; ki < k; ki++)
                {
                    int index = 0;
                    try
                    {
                        index = distances1[ki].Index;
                        result[i][index] = 1;
                        result[index][i] = 1;
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
            if (A.Count == 1) return A[0];
            List<List<double>> sublists = new List<List<double>>();
            List<double> medians = new List<double>();
            double pivot;
            int k;

            // Break into sublists
            for (int j = 0; j < A.Count; j += 5)
            {
                k = j + 5 > A.Count - 1 ? A.Count - 1 : j + 5; // checking array bounds
                if (j == k) continue;
                sublists.Add(new List<double>(A.GetRange(j, k - j)));
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
            List<double> high = A.Where(x => x >= pivot).ToList();

            int m = low.Count;
            if (i < m) return MedianOfMedians(low, i);
            if  (i > m) return MedianOfMedians(high, i - m - 1);
            return pivot;
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

