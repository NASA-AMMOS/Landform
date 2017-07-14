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
      
            Debug.WriteLine("Filtering with GTM...");
            GTMOptimized gtmO = new GTMOptimized(matches, P, PPrime, K);;

            return gtmO.Filter();
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

