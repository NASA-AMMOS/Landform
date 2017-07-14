using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OPS.Alignment
{
    public class GTM : IMatchFilter
    {
        public int MaxIterations;
        public bool RefineStep;
        public int K;
        public int counter;
        public bool Optimized;

        public GTM(int k = 5, bool optimized = true)
        {
            K = k;
            Optimized = optimized;
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

            ImagePairCorrespondence debugMatches;
            if (Optimized)
            {
                Debug.WriteLine("Filtering with new optimized GTM...");
                GTMOptimized gtmO = new GTMOptimized(matches, P, PPrime, K);
                matches = gtmO.Filter();
            }
            else
            {
                int outlier;
                Debug.WriteLine("Filtering with old GTM...");
                double[][] DistP = ComputeDistanceMatrix(P);
                double[][] DistPPrime = ComputeDistanceMatrix(PPrime);
                double MedianP = ComputeMedian(DistP);
                double MedianPPrime = ComputeMedian(DistPPrime);
                ImageFeature[] Q = (ImageFeature[])P.Clone();
                ImageFeature[] QPrime = (ImageFeature[])PPrime.Clone();
                HashSet<int> outliers = new HashSet<int>();

                double[][] AP = BuildMedianKNNGraph(DistP, K, MedianP, outliers);
                double[][] APPrime = BuildMedianKNNGraph(DistPPrime, K, MedianPPrime, outliers);

                List<int> debugOutliers1 = new List<int>();
                counter = 0;
                while (!GraphEqual(AP, APPrime))
                {
                    counter++;
                    outlier = FindOutlier(AP, APPrime, outliers);
                    debugOutliers1.Add(outlier);
                    outliers.Add(outlier);
                    AP = BuildMedianKNNGraph(DistP, K, MedianP, outliers);
                    APPrime = BuildMedianKNNGraph(DistPPrime, K, MedianPPrime, outliers);
                }

                List<double> rowsums = AP.Select(x => x.Sum()).ToList();
                List<double> rowsums1 = APPrime.Select(x => x.Sum()).ToList();
                Debug.WriteLine(rowsums.Where(x => x == 5).Count());
                Debug.WriteLine(rowsums1.Where(x => x == 5).Count());
                //for (int x = 0; x < rowsums.Count; x++)
                //{
                //    if (rowsums[x] == K)
                //        Debug.WriteLine(x + ": little neighbors in rowsums");
                //    if (rowsums1[x] == K)
                //        Debug.WriteLine(x + ": little neighbors in rowsums1");
                //}

                writeListToFile(@"C:\Users\charchut\Desktop\outliersOldOld", debugOutliers1);

                Q = RemoveDisconnectedVertices(Q, AP);
                QPrime = RemoveDisconnectedVertices(QPrime, APPrime);

                if (Q.Length != QPrime.Length)
                    throw new Exception("Matched features not equal in length.");

                KeyValuePair<int, int>[] goodMatches2 = ConstructFinalMatches(Q.Length);

                Debug.WriteLine("Number of residual matches: " + goodMatches2.Length + " after " + counter + " iterations of GTM");

                matches = new ImagePairCorrespondence(
                    matches.ModelImage, matches.DataImage,
                    Q, QPrime,
                    goodMatches2);
            }

            return matches;
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
        /// Constructs the final matches as i:i mappings for i in [0, length).
        /// </summary>
        /// <returns>The final matches.</returns>
        /// <param name="length">Length of mapping.</param>
        public static KeyValuePair<int, int>[] ConstructFinalMatches(int length)
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
        public static double ComputeMedian(double[][] arr, bool linear = false)
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
        public ImageFeature[] RemoveDisconnectedVertices(ImageFeature[] features, HashSet<int> outliers)
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
                    int index = distances1[ki].Index;
                    if (distances1[ki].Value <= median)
                    {
                        result[i][index] = 1;
                        result[index][i] = 1;
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
        static double MedianOfMedians(List<double> A, int i)
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
        public static double[][] ComputeDistanceMatrix(ImageFeature[] features)
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

        public static void writeListToFile<T>(string filename, List<T> list)
        {
            using (StreamWriter writer = new StreamWriter(new FileStream(filename, FileMode.Create)))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    writer.WriteLine(i + ": " + list[i]);
                }    
            }
        }
    }
}

