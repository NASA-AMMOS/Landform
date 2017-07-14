using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    /// <summary>
    /// 
    /// </summary>
    internal class GTMGraph
    {
        // Number of nearest-neighbors
        int K;

        // Distance matrix.
        public double[][] Dist { get; set; }

        // Medians of flattened P and PPRime matrices.
        public double Median;

        // Graph where the node at (i, j) represents the jth nearest-neighbor of node i.
        public int[][] O { get; set; }

        // Sets where index refers to node (call it A), and values correspond to nodes of which A is part of its KNN.
        public HashSet<int>[] I { get; set; }

        // Indexed by node, where the value corresponds to next node to try when updating a row in the KNN graph.
        public int[] C { get; set; }

        // Arrays of filtered image features.
        public ImageFeature[] Q { get; set; }

        // Set of removed vertices.
        HashSet<int> Outliers { get; set; }

        // Set of falsely identified outliers.
        HashSet<int> FalseOutliers { get; set; }

        // Placeholders for deleted pairs, and absent neighbors, respectively.
        static int REMOVED = -1;
        static int FILLER = -2;

        /// <summary>
        /// Creates a
        /// </summary>
        /// <param name="p"></param>
        /// <param name="outliers"></param>
        /// <param name="falseOutliers"></param>
        /// <param name="k"></param>
        internal GTMGraph(ImageFeature[] p, HashSet<int> outliers, HashSet<int> falseOutliers, int k)
        {
            K = k;
            Dist = ComputeDistanceMatrix(p);
            Median = ComputeMedian(Dist);
            O = InitMedianKNNGraph(Dist, Median);
            I = InitNeighborVector(O);
            C = new int[p.Length].Select(x => K).ToArray();
            Outliers = outliers;
            FalseOutliers = falseOutliers;
            Q = (ImageFeature[])p.Clone();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dist"></param>
        /// <param name="median"></param>
        /// <returns></returns>
        public int[][] InitMedianKNNGraph(double[][] dist, double median)
        {
            int[][] res = new int[dist.Length][].Select(x => new int[dist.Length]).ToArray();

            for (int i = 0; i < dist.Length; i++)
            {
                res[i] = dist[i].Select((x, j) => new { Value = x, Index = j })
                                .Where(y => y.Value > 0 && y.Value <= median)
                                .OrderBy(v => v.Value)
                                .Select(w => w.Index)
                                .ToArray();
                int fill = dist.Length - res[i].Length;
                res[i] = res[i].Concat(Enumerable.Repeat(FILLER, fill)).ToArray();
            }

            return res;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="knnGraph"></param>
        /// <returns></returns>
        public HashSet<int>[] InitNeighborVector(int[][] knnGraph, int k = 5)
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

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public int FindOutlier(GTMGraph data)
        {
            HashSet<int> A = new HashSet<int>();
            HashSet<int> APrime = new HashSet<int>();
            int maxcount = -1;
            IEnumerable<int> outlierSet;
            int outlier = REMOVED;

            //Debug.Assert(model.Outliers.SetEquals(data.Outliers) && model.FalseOutliers.SetEquals(data.FalseOutliers));

            for (int i = 0; i < O.Length; i++)
            {
                if (Outliers.Contains(i)) continue;

                foreach (int num in I[i])
                    A.Add(num);
                foreach (int num in data.I[i])
                    APrime.Add(num);

                for (int k = 0; k < K; k++)
                {
                    A.Add(O[i][k]);
                    APrime.Add(data.O[i][k]);
                }

                outlierSet = A.Except(APrime).Union(APrime.Except(A)).Where(x => x != FILLER);

                if (outlierSet.Count() > maxcount)
                {
                    maxcount = outlierSet.Count();
                    if (!Outliers.Contains(i) && !FalseOutliers.Contains(i))
                        outlier = i;
                }

                A.Clear();
                APrime.Clear();
            }

            return outlier;
        }


        /// <summary>
        /// Checks if the first K columns of both KNN graphs are set equivalent.
        /// </summary>
        /// <param name="modelGraph"></param>
        /// <param name="dataGraph"></param>
        /// <returns></returns>
        public bool GraphEqual(GTMGraph dataGraph)
        {
            HashSet<int> ARow, APRow;
            for (int i = 0; i < this.O.Length; i++)
            {

                ARow = new HashSet<int>();
                APRow = new HashSet<int>();

                if (this.O[i][0] == REMOVED && dataGraph.O[i][0] == REMOVED) continue;

                for (int k = 0; k < K; k++)
                {
                    ARow.Add(this.O[i][k]);
                    APRow.Add(dataGraph.O[i][k]);
                }

                if (!ARow.SetEquals(APRow))
                    return false;

                ARow.Clear();
                APRow.Clear();
            }
            return true;
        }

        public void RemoveOutlier(int outlier)
        {
            // Remove from O and OPrime
            O[outlier][0] = REMOVED;

            // Remove from I and IPrime and refresh every row that had outlier in its first K columns
            foreach (int index in I[outlier])
            {
                I[index].Remove(outlier);
                RefreshKNNGraph(outlier, index);
            }

            I[outlier].Clear();

            // Remove outlier from other I entries
            for (int j = 0; j < I.Length; j++)
            {
                I[j].Remove(outlier);
            }
        }

        /// <summary>
        /// Removes nodes disconnected from the graph. 
        /// </summary>
        public void RemoveDisconnectedVertices()
        {
            Q = Q.Where((x, i) => !Outliers.Contains(i)).ToArray();
        }

        private void ShiftRow(int[] v)
        {
            IEnumerable<int> temp = v.Where(x => x != FILLER);
            v = temp.Concat(Enumerable.Repeat(FILLER, v.Length - temp.Count())).ToArray();
        }

        public void RefreshKNNGraph(int outlier, int index)
        {
            int[] row;
            row = O[index];
            if (row[0] == REMOVED)
                return;

            int tmpi = C[index];
            for (int j = 0; j < K; j++)
            {
                if (row[j] == outlier)
                {
                    while (Outliers.Contains(row[tmpi]))
                    {
                        if (tmpi == O.Length - 1)
                        {
                            tmpi--;
                            break;
                        }
                        tmpi++;
                    }
                    row[j] = row[tmpi];
                    C[index] = tmpi + 1;

                    if (row[tmpi] == FILLER)
                    {
                        continue;
                    }

                    I[row[tmpi]].Add(index);
                    break;
                }
            }
            if (row[tmpi] == FILLER)
            {
                ShiftRow(row);
                return;
            }
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
            if (i > m) return MedianOfMedians(high, i - m - 1);
            return pivot;
        }
    }
}
