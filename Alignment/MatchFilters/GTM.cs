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
        public int counter;

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

            counter = 0;
            int outlier;


            // DEBUG
            // K = 2;
            //ImageFeature u1 = new ImageFeature(new Vector2(0, 4), null);
            //ImageFeature u2 = new ImageFeature(new Vector2(1, 3), null);
            //ImageFeature u3 = new ImageFeature(new Vector2(3, 0), null);
            //ImageFeature u4 = new ImageFeature(new Vector2(1, 4), null);
            //ImageFeature u5 = new ImageFeature(new Vector2(3, 4), null);
            //ImageFeature u6 = new ImageFeature(new Vector2(2, 5), null);

            //ImageFeature[] testSet = new ImageFeature[] { u1, u2, u3, u4, u5, u6 };

            //ImageFeature u1 = new ImageFeature(new Vector2(0, 0), null);
            //ImageFeature u2 = new ImageFeature(new Vector2(0, 1), null);
            //ImageFeature u3 = new ImageFeature(new Vector2(2, 0), null);
            //ImageFeature u4 = new ImageFeature(new Vector2(3, 1), null);

            //ImageFeature[] testSet = new ImageFeature[] { u1, u2, u3, u4 };
            //int[][] At = new int[6][];
            //int[][] Atp = new int[6][];
            //At[0] = new int[] { 4, 2, 3, 5, 6, -2 };
            //At[1] = new int[] { 3, 4, 5, 1, 6, -2 };
            //At[2] = new int[] { 2, 5, 4, 1, 6, -2 };
            //At[3] = new int[] { 2, 3, 5, 1, 6, -2 };
            //At[4] = new int[] { 3, 2, 4, 1, 6, -2 };
            //At[5] = new int[] { 4, 1, 5, 2, 3, -2 };

            //Atp[0] = new int[] { 4, 2, 3, 5, 6, -2 };
            //Atp[1] = new int[] { 3, 4, 6, 5, 1, -2 };
            //Atp[2] = new int[] { 6, 2, 5, 4, 1, -2 };
            //Atp[3] = new int[] { 2, 3, 1, 5, 6, -2 };
            //Atp[4] = new int[] { 6, 3, 2, 4, 1, -2 };
            //Atp[5] = new int[] { 3, 5, 2, 4, 1, -2 };

            //HashSet<int>[] It = new HashSet<int>[6];
            //It[0] = new HashSet<int>(new int[]{ 6 });
            //It[1] = new HashSet<int>(new int[] { 1, 3, 4, 5 });
            //It[2] = new HashSet<int>(new int[] { 2, 4, 5 });
            //It[3] = new HashSet<int>(new int[] { 1, 2, 6 });
            //It[4] = new HashSet<int>(new int[] { 3 });
            //It[5] = new HashSet<int>(new int[] {  });

            //HashSet<int>[] Itp = new HashSet<int>[6];
            //Itp[0] = new HashSet<int>(new int[] {  });
            //Itp[1] = new HashSet<int>(new int[] { 1, 3, 4 });
            //Itp[2] = new HashSet<int>(new int[] { 2, 4, 5, 6 });
            //Itp[3] = new HashSet<int>(new int[] { 1, 2 });
            //Itp[4] = new HashSet<int>(new int[] { 6 });
            //Itp[5] = new HashSet<int>(new int[] { 3, 5 });

            //Debug.WriteLine(FindOutlier(At, Atp, It, Itp, new HashSet<int>()));
            Debug.WriteLine("");

            //Debug.WriteLine(GraphEqual(At, Atp, 2));
            //Debug.WriteLine(GraphEqual(At, Atp, 3));


            //double[][] dist = ComputeDistanceMatrix(testSet);
            //int[][] Ot = InitMedianKNNGraphOptimized(dist, 2);
            //HashSet<int>[] It = InitNeighborVector(Ot, 2);
            //int[] Ct = new int[testSet.Count()].Select(x => K).ToArray();

            //printIntermediates(Ot, It, Ct);
            //RefreshKNNGraph(Ot, Ct, 3);
            //printIntermediates(Ot, It, Ct);
            //RefreshKNNGraph(Ot, Ct, 2);
            //printIntermediates(Ot, It, Ct);
            //for (int kl = 0; kl < testKNN.Length; kl++)
            //{
            //    for (int oi = 0; oi < testKNN.Length; oi++)
            //    {
            //        Debug.Write(String.Format("{0:0.0}", dist[kl][oi]) + " ");
            //    }
            //    Debug.WriteLine("\n");
            //}
            //Debug.WriteLine("-------");
            //for (int kl = 0; kl < Ot.Length; kl++)
            //{
            //    for (int oi = 0; oi < Ot.Length; oi++)
            //    {
            //        Debug.Write(Ot[kl][oi] + 1 + " ");
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
            Debug.WriteLine("Filtering with GTM...");
            counter = 0;
            double[][] DistP = ComputeDistanceMatrix(P);
            double[][] DistPPrime = ComputeDistanceMatrix(PPrime);
            double MedianP = ComputeMedian(DistP);
            double MedianPPrime = ComputeMedian(DistPPrime);
            int[][] O = InitMedianKNNGraphOptimized(DistP, MedianP);
            int[][] OPrime = InitMedianKNNGraphOptimized(DistPPrime, MedianPPrime);
            HashSet<int>[] I = InitNeighborVector(O);
            HashSet<int>[] IPrime = InitNeighborVector(OPrime);
            int[] C = new int[pairs.Length].Select(x => K).ToArray();
            int[] CPrime = new int[pairs.Length].Select(x => K).ToArray();
            HashSet<int> outliers = new HashSet<int>(new int[] { -2 });
            ImageFeature[] Q = (ImageFeature[])P.Clone();
            ImageFeature[] QPrime = (ImageFeature[])PPrime.Clone();

            while (!GraphEqual(O, OPrime))
            {
                counter++;
                outlier = FindOutlier(O, OPrime, I, IPrime, outliers);

                if (outliers.Contains(outlier))
                    Debug.Write("here lol");
                outliers.Add(outlier);
                if (outlier == -1)
                    Debug.WriteLine("what");
                RemoveOutlier(outlier, O, OPrime, I, IPrime, C, CPrime, outliers);
            }

            for (int kk = 0; kk < O.Length; kk++)
            {
                //Debug.WriteLine(O[kk][0] + " " + O[kk][1] + " " + O[kk][2] + " " + O[kk][3] + " " + O[kk][4] + " " + O[kk][5]);
                //Debug.WriteLine(OPrime[kk][0] + " " + OPrime[kk][1] + " " + OPrime[kk][2] + " " + OPrime[kk][3] + " " + OPrime[kk][4] + " " + OPrime[kk][5]);
                //Debug.WriteLine("------");
                if (O[kk][0] == OPrime[kk][0] &&
                    O[kk][1] == OPrime[kk][1] &&
                    O[kk][2] == OPrime[kk][2] &&
                    O[kk][3] == OPrime[kk][3] &&
                    O[kk][4] == OPrime[kk][4] &&
                    O[kk][5] == OPrime[kk][5])
                {
                    outliers.Remove(kk);
                }
            }



            Debug.WriteLine(counter + " iterations");
            Q = RemoveDisconnectedVertices(Q, outliers);
            QPrime = RemoveDisconnectedVertices(QPrime, outliers);

            if (Q.Length != QPrime.Length)
                throw new Exception("Matched features not equal in length.");

            KeyValuePair<int, int>[] goodMatches = ConstructFinalMatches(Q.Length);

            Debug.WriteLine("Number of residual matches: " + goodMatches.Length + " after " + counter + " iterations of GTM");

            return new ImagePairCorrespondence(matches.ModelImage, matches.DataImage, Q, QPrime, goodMatches);


            // /OPTIMIZED>
            //Debug.WriteLine("Filtering with GTM...");
            //double[][] DistP = ComputeDistanceMatrix(P);
            //double[][] DistPPrime = ComputeDistanceMatrix(PPrime);
            //double MedianP = ComputeMedian(DistP);
            //double MedianPPrime = ComputeMedian(DistPPrime);
            //ImageFeature[] Q = (ImageFeature[])P.Clone();
            //ImageFeature[] QPrime = (ImageFeature[])PPrime.Clone();
            //HashSet<int> outliers = new HashSet<int>();

            //double[][] AP = BuildMedianKNNGraph(DistP, K, MedianP, outliers);
            //double[][] APPrime = BuildMedianKNNGraph(DistPPrime, K, MedianPPrime, outliers);


            //while (!GraphEqual(AP, APPrime))
            //{
            //    counter++;
            //    outlier = FindOutlier(AP, APPrime, outliers);

            //    if (outliers.Contains(outlier))
            //    {
            //        break;
            //    }
            //    outliers.Add(outlier);
            //    //RemoveOutlier(outlier, DistP, DistPPrime, Q, QPrime);
            //    AP = BuildMedianKNNGraph(DistP, K, MedianP, outliers);
            //    APPrime = BuildMedianKNNGraph(DistPPrime, K, MedianPPrime, outliers);
            //}

            //Q = RemoveDisconnectedVertices(Q, AP);
            //QPrime = RemoveDisconnectedVertices(QPrime, APPrime);

            //if (Q.Length != QPrime.Length)
            //    throw new Exception("Matched features not equal in length.");

            //KeyValuePair<int, int>[] goodMatches = ConstructFinalMatches(Q.Length);

            //Debug.WriteLine("Number of residual matches: " + goodMatches.Length + " after " + counter + " iterations of GTM");

            //return new ImagePairCorrespondence(
            //    matches.ModelImage, matches.DataImage,
            //    Q, QPrime,
            //    goodMatches);
        }

        private void printIntermediates(int[][] o, HashSet<int>[] i, int[] c, int numrows = 10)
        {
            int count = 0;
            foreach (int[] row in o)
            {
                for (int j = 0; j < Math.Min(o.Length, numrows); j++)
                {
                    Debug.Write(row[j] + 1 + " ");
                }
                Debug.WriteLine("");
                if (count++ > 10) break;
            }
            Debug.WriteLine("---------");
        }

        private void RefreshKNNGraph(int[][] o, int[] c, HashSet<int>[] i, int outlier, int index, HashSet<int> outliers)
        {
            int[] row;
            row = o[index];
            if (row[0] == -1)
                return;

            //if (index == 282)
            //{
            //    Debug.WriteLine("here");
            //}
            int tmpi = c[index];
            for (int j = 0; j < K; j++)
            {
                if (row[j] == outlier)
                {
                    //Debug.WriteLine("outliers contains " + row[tmpi] + ": " + outliers.Contains(row[tmpi]));
                    while (outliers.Contains(row[tmpi]))
                    {
                        // Debug.WriteLine("Trying j: " + tmpi + ", with value: " + row[tmpi]);
                        if (tmpi == o.Length - 1)
                        {
                            // Debug.WriteLine("J: " + j);
                            //Debug.WriteLine("number of outliers identified: " + outliers.Count());
                            tmpi--; // START HERE TOMORROW
                            break;
                        }
                        tmpi++;
                    }
                    row[j] = row[tmpi];
                    c[index] = tmpi + 1;
                    //if (c[index] == 607)
                    //{
                    //    Debug.WriteLine("here");
                    //}
                    if (row[tmpi] == -2) continue;
                    i[row[tmpi]].Add(index);
                    break;
                }
            }
            if (row[tmpi] == -2)
            {
                ShiftRow(row);
                return;
            }
        }

        private void ShiftRow(int[] v)
        {
            IEnumerable<int> temp = v.Where(x => x != -2);
            v = temp.Concat(Enumerable.Repeat(-2, v.Length - temp.Count())).ToArray();
        }

        public void RemoveOutlier(int outlier, int[][] o, int[][] oPrime, HashSet<int>[] i, HashSet<int>[] iPrime, int[] c, int[] cPrime, HashSet<int> outliers)
        {
            // Remove from O and OPrime
            o[outlier][0] = -1;
            oPrime[outlier][0] = -1;

            // Remove from I and IPrime
            foreach (int index in i[outlier])
            {
               
                i[index].Remove(outlier);
                RefreshKNNGraph(o, c, i, outlier, index, outliers);
            }

            foreach (int index in iPrime[outlier])
            {
                
                iPrime[index].Remove(outlier);
                RefreshKNNGraph(oPrime, cPrime, iPrime, outlier, index, outliers);
            }

            i[outlier].Clear();
            iPrime[outlier].Clear();

            for (int j = 0; j < i.Length; j++)
            {
                i[j].Remove(outlier);
                iPrime[j].Remove(outlier);
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

        /// <summary>
        /// Checks if two optimized graphs are equal.
        /// </summary>
        /// <param name="A"></param>
        /// <param name="AP"></param>
        /// <returns></returns>
        public bool GraphEqual(int[][] A, int[][] AP)
        {
            HashSet<int> ARow, APRow;
            for (int i = 0; i < A.Length; i++)
            {

                ARow = new HashSet<int>();
                APRow = new HashSet<int>();

                if (A[i][0] == -1 && AP[i][0] == -1) continue;

                

                for (int k = 0; k < K; k++)
                {
                    ARow.Add(A[i][k]);
                    APRow.Add(AP[i][k]);
                }

                if (!ARow.SetEquals(APRow))
                    return false;
                ARow.Clear();
                APRow.Clear();
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
                    //A.Add(arr[k][j]);
                }
            }

            int i = A.Count / 2;

            if (linear)
            {
                return MedianOfMedians(A, i);
            }

            A.Sort();
            return A[i];
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
        /// Prunes erroneous features.
        /// </summary>
        /// <param name="features">Input list of features.</param>
        /// <param name="graph">Adjacency matrix representation of graph.</param>
        /// <returns>A copy of pruned features.</returns>
        ImageFeature[] RemoveDisconnectedVertices(ImageFeature[] features, HashSet<int> outliers)
        {
            return features.Where((x, i) => !outliers.Contains(i)).ToArray();
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

        public int FindOutlier(int[][] O, int[][] OPrime, HashSet<int>[] I, HashSet<int>[] IPrime, HashSet<int> outliers)
        {
            HashSet<int> A = new HashSet<int>();
            HashSet<int> APrime = new HashSet<int>();
            int maxcount = -1;
            IEnumerable<int> outlierSet;
            int outlier = -1;

            for (int i = 0; i < O.Length; i++)
            {
                if (outliers.Contains(i)) continue;

                foreach (int num in I[i])
                    A.Add(num);
                foreach (int num in IPrime[i])
                    APrime.Add(num);

                for (int k = 0; k < K; k++)
                {
                    A.Add(O[i][k]);
                    APrime.Add(OPrime[i][k]);
                }

                outlierSet = A.Except(APrime).Union(APrime.Except(A));
                int[] outlierSetArr = outlierSet.ToArray();

                if (outlierSet.Count() > maxcount)
                {
                    maxcount = outlierSet.Count();
                    outlier = i;
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
                res[i] = dist[i].Select((x, j) => new { Value = x, Index = j })
                                .Where(y => y.Value > 0 && y.Value <= median)
                                .OrderBy(v => v.Value)
                                .Select(w => w.Index)
                                .ToArray();
                int fill = dist.Length - res[i].Length;
                res[i] = res[i].Concat(Enumerable.Repeat(-2, fill)).ToArray();
            }

            return res;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="knnGraph"></param>
        /// <returns></returns>
        HashSet<int>[] InitNeighborVector(int[][] knnGraph, int k = 5)
        {
            HashSet<int>[] res = new HashSet<int>[knnGraph.Length].Select(x => new HashSet<int>()).ToArray();
            int[] vertexCount = new int[knnGraph.Length];

            for (int i = 0; i < knnGraph.Length - 1; i++)
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
                var distances1 = dist[i].Select((x, j) => new { Value = x, Index = j })
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

