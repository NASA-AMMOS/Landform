using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public class GTMOptimized
    {
        // Number of nearest-neighbors
        int K;

        // Original image correspondence representing matched nodes/
        public ImagePairCorrespondence Matches;

        // Distance matrix for P and PPrime nodes.
        public double[][] DistP { get; set; }
        public double[][] DistPPrime { get; set; }

        // Medians of flattened P and PPRime matrices.
        public double MedianP, MedianPPrime;

        // Graph where the node at (i, j) represents the jth nearest-neighbor of node i.
        public int[][] O { get; set; }
        public int[][] OPrime { get; set; }

        // Sets where index refers to node (call it A), and values correspond to nodes of which A is part of its KNN.
        public HashSet<int>[] I { get; set; }
        public HashSet<int>[] IPrime { get; set; }

        // Indexed by node, where the value corresponds to next node to try when updating a row in the KNN graph.
        public int[] C { get; set; }
        public int[] CPrime { get; set; }

        // Arrays of filtered image features.
        ImageFeature[] Q, QPrime;

        // Set of removed vertices.
        HashSet<int> Outliers { get; set; }

        // Set of falsely identified outliers.
        HashSet<int> FalseOutliers { get; set; }

        // Placeholders for deleted pairs, and absent neighbors, respectively.
        const int REMOVED = -1;
        const int FILLER = -2;

        public GTMOptimized(ImagePairCorrespondence matches, ImageFeature[] P, ImageFeature[] PPrime, int k)
        {
            K = k;
            Matches = matches;
            DistP = GTM.ComputeDistanceMatrix(P);
            DistPPrime = GTM.ComputeDistanceMatrix(PPrime);
            MedianP = GTM.ComputeMedian(DistP);
            MedianPPrime = GTM.ComputeMedian(DistPPrime);
            O = InitMedianKNNGraph(DistP, MedianP);
            OPrime = InitMedianKNNGraph(DistPPrime, MedianPPrime);
            I = InitNeighborVector(O);
            IPrime = InitNeighborVector(OPrime);
            C = new int[P.Length].Select(x => K).ToArray();
            CPrime = new int[P.Length].Select(x => K).ToArray();
            Outliers = new HashSet<int>();
            FalseOutliers = new HashSet<int>(new int[] { REMOVED, FILLER });
            Q = (ImageFeature[])P.Clone();
            QPrime = (ImageFeature[])PPrime.Clone();
        }

        internal ImagePairCorrespondence Filter()
        {
            int outlier;
            int counter = 0;

            while (!GraphEqual(O, OPrime))
            {
                counter++;
                outlier = FindOutlier();

                if (outlier == -1)
                    break;

                if (!checkOutlier(outlier))
                {
                    FalseOutliers.Add(outlier);
                    continue;
                }
  
                Outliers.Add(outlier);
                RemoveOutlier(outlier);
            }

            int[] rowsums = new int[O.Length];
            for (int y = 0; y < O.Length; y++)
            {
                if (O[y][0] == -1) continue;
                for (int k = 0; k < K; k++)
                {
                    if (O[y][k] != -2)
                        rowsums[y]++;
                }
            }

            int[] rowsums1 = new int[OPrime.Length];
            for (int y = 0; y < OPrime.Length; y++)
            {
                if (OPrime[y][0] == -1) continue;
                for (int k = 0; k < K; k++)
                {
                    if (OPrime[y][k] != -2)
                        rowsums1[y]++;
                }
            }

            RemoveDisconnectedVertices();

            if (Q.Length != QPrime.Length)
            {
                throw new Exception("Matched features not equal in length.");
            }

            KeyValuePair<int, int>[] goodMatches = GTM.ConstructFinalMatches(Q.Length);
            Debug.WriteLine("Number of residual matches: " + goodMatches.Length + " after " + counter + " iterations of GTM");
            return new ImagePairCorrespondence(Matches.ModelImage, Matches.DataImage, Q, QPrime, goodMatches);
        }

        private bool checkOutlier(int outlier)
        {
            if (O[outlier][0] == OPrime[outlier][0] &&
                O[outlier][1] == OPrime[outlier][1] &&
                O[outlier][2] == OPrime[outlier][2] &&
                O[outlier][3] == OPrime[outlier][3] &&
                O[outlier][4] == OPrime[outlier][4])
            {
                return false;
            }

            return true;
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
                res[i] = res[i].Concat(Enumerable.Repeat(-2, fill)).ToArray();
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
        public int FindOutlier()
        {
            HashSet<int> A = new HashSet<int>();
            HashSet<int> APrime = new HashSet<int>();
            int maxcount = -1;
            IEnumerable<int> outlierSet;
            int outlier = -1;

            for (int i = 0; i < O.Length; i++)
            {
                if (Outliers.Contains(i)) continue;

                foreach (int num in I[i])
                    A.Add(num);
                foreach (int num in IPrime[i])
                    APrime.Add(num);

                for (int k = 0; k < K; k++)
                {
                    A.Add(O[i][k]);
                    APrime.Add(OPrime[i][k]);
                }

                outlierSet = A.Except(APrime).Union(APrime.Except(A)).Where(x=>x!=FILLER && !Outliers.Contains(x) && !FalseOutliers.Contains(x));
                int[] outlierSetArr = outlierSet.ToArray();

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
        /// Checks if the first K columns of both graphs are set equivalent.
        /// </summary>
        /// <param name="modelGraph"></param>
        /// <param name="dataGraph"></param>
        /// <returns></returns>
        public bool GraphEqual(int[][] modelGraph, int[][] dataGraph)
        {
            HashSet<int> ARow, APRow;
            for (int i = 0; i < modelGraph.Length; i++)
            {

                ARow = new HashSet<int>();
                APRow = new HashSet<int>();

                if (modelGraph[i][0] == -1 && dataGraph[i][0] == -1) continue;

                for (int k = 0; k < K; k++)
                {
                    ARow.Add(modelGraph[i][k]);
                    APRow.Add(dataGraph[i][k]);
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
            O[outlier][0] = -1;
            OPrime[outlier][0] = -1;

            // Remove from I and IPrime
            foreach (int index in I[outlier])
            {
                I[index].Remove(outlier);
                RefreshKNNGraph(O, C, I, outlier, index);
            }

            // Refresh every row that had outlier in its first K columns
            foreach (int index in IPrime[outlier])
            { 
                IPrime[index].Remove(outlier);
                RefreshKNNGraph(OPrime, CPrime, IPrime, outlier, index);
            }

            I[outlier].Clear();
            IPrime[outlier].Clear();

            // Remove outlier from other I entries
            for (int j = 0; j < I.Length; j++)
            {
                I[j].Remove(outlier);
                IPrime[j].Remove(outlier);
            }
        }

        /// <summary>
        /// Removes nodes disconnected from the graph. 
        /// </summary>
        public void RemoveDisconnectedVertices()
        {
            Q = Q.Where((x, i) => !Outliers.Contains(i)).ToArray();
            QPrime = QPrime.Where((x, i) => !Outliers.Contains(i)).ToArray();
        }

        private void ShiftRow(int[] v)
        {
            IEnumerable<int> temp = v.Where(x => x != -2);
            v = temp.Concat(Enumerable.Repeat(-2, v.Length - temp.Count())).ToArray();
        }

        public void RefreshKNNGraph(int[][] o, int[] c, HashSet<int>[] i, int outlier, int index)
        {
            int[] row;
            row = o[index];
            if (row[0] == -1)
                return;

            int tmpi = c[index];
            for (int j = 0; j < K; j++)
            {
                if (row[j] == outlier)
                {
                    while (Outliers.Contains(row[tmpi]))
                    {
                        if (tmpi == o.Length - 1)
                        {
                            tmpi--;
                            break;
                        }
                        tmpi++;
                    }
                    row[j] = row[tmpi];
                    c[index] = tmpi + 1;

                    if (row[tmpi] == -2)
                    {
                        continue;
                    }

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
    }
}
