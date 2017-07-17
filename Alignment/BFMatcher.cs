using OPS.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;

namespace OPS.Alignment
{
    public class BFMatcherR : IDisposable
    {
        ImageFeature[] ModelFeatures;
        ImageFeature[] DataFeatures;
        knnNode[][] Matches;

        public BFMatcherR(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures)
        {
            Matches = modelFeatures.Select((x, j) => new knnNode[] { new knnNode(0, j) }).ToArray();
        }

        public ImagePairCorrespondence Match(ImageRef model, ImageRef data, 
            IEnumerable<ImageFeature> modelFeat, IEnumerable<ImageFeature> dataFeat)
        {
            SIFTFeature[] feat0 = modelFeat.Cast<SIFTFeature>().ToArray();
            SIFTFeature[] feat1 = dataFeat.Cast<SIFTFeature>().ToArray();

            // Match descriptors
            KnnMatch(feat0, feat1,2);
            
            Matrix<byte> mask = Matrix<byte>.Build.Dense(Matches.Length, 1);

            // OpenCV standard correspondence checks
            for (int idx = 0; idx < Matches.Length; idx++)
            {
                if (Matches[idx][0].Value > Matches[idx][1].Value * 0.8)
                {
                    mask[idx, 0] = 0;
                }
                else
                {
                    mask[idx, 0] = 255;
                }
            }

            List<KeyValuePair<int, int>> dataToModel = new List<KeyValuePair<int, int>>();
            for (int idx = 0; idx < Matches.Length; idx++)
            {
                if (mask[idx, 0] != 0)
                {
                    var match = Matches[idx][0];
                    dataToModel.Add(new KeyValuePair<int, int>(match.Index, match.Index));
                }
            }

            System.Diagnostics.Debug.WriteLine(string.Format("Model features: {0}, Data features: {1}, Matches: {2}", feat0.Length, feat1.Length, dataToModel.Count));
            var res = new ImagePairCorrespondence(model, data, feat0, feat1, dataToModel);
            res.Compact();
            return res;
        }

        private void KnnMatch(SIFTFeature[] modelFeat, SIFTFeature[] dataFeat, int k)
        {
            Matrix<double> dist = Matrix<double>.Build.Dense(modelFeat.Length, dataFeat.Length);

            // Compute distance matrix
            for (int i = 0; i < modelFeat.Length; i++)
            {
                SIFTFeature modelFeature = modelFeat[i];
                for (int j = i; j < dataFeat.Length; j++)
                {
                    dist[i, j] = DescriptorDistance(modelFeature, dataFeat[j]);
                }
            }

            // Get k-nearest neighbors for each image
            knnNode[] knn = new knnNode[k];
            knnNode[] row;
            for (int i = 0; i < dist.RowCount; i++)
            {
                row = dist.Row(i).Select((x, j) => new knnNode(x, j)).ToArray();
                for (int kk = 0; kk < k; kk++)
                {
                    knn[kk] = ComputeMedianIndex(row, true);
                    row = row.Take(row.Length - 1).ToArray();
                }
                Matches[i] = Matches[i].Concat(knn).ToArray();
            }
            /// START HERE TOMORROW, WILL NEED SEPARATE DATA STRUCTURE FOR DATAIMAGE MATCHES

            for (int i = 0; i < dist.ColumnCount; i++)
            {
                row = dist.Column(i).Select((x, j) => new knnNode(x, j)).ToArray();
                for (int kk = 0; kk < k; kk++)
                {
                    knn[kk] = ComputeMedianIndex(row, true);
                    row = row.Take(row.Length - 1).ToArray();
                }
            }


            throw new NotImplementedException();
        }

        public class knnNode : Comparer<knnNode> {
            public int Index;
            public double Value;

            public knnNode(double value, int index)
            {
                Value = value;
                Index = index;
            }

            public override int Compare(knnNode x, knnNode y)
            {
                return x.Value > y.Value ? 1 : -1;
            }
        }

        /// <summary>
        /// Computes the median of a 2D-array.
        /// </summary>
        /// <param name="arr">Input array.</param>
        /// <returns>Median of input array.</returns>
        static public knnNode ComputeMedianIndex(knnNode[] arr, bool linear = false)
        {
            List<knnNode> A = new List<knnNode>();

            // Flatten the array before beginning the algorithm.
            for (int j = 0; j < arr.Length; j++)
            {
                A.Add(arr[j]);
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
        static knnNode MedianOfMedians(List<knnNode> A, int i)
        {
            if (A.Count == 1) return A[0];
            List<List<knnNode>> sublists = new List<List<knnNode>>();
            List<knnNode> medians = new List<knnNode>();
            knnNode pivot;
            int k;

            // Break into sublists
            for (int j = 0; j < A.Count; j += 5)
            {
                k = j + 5 > A.Count - 1 ? A.Count - 1 : j + 5; // checking array bounds
                if (j == k) continue;
                sublists.Add(new List<knnNode>(A.GetRange(j, k - j)));
            }

            foreach (List<knnNode> sublist in sublists)
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

            List<knnNode> low = A.Where(x => x.Value < pivot.Value).ToList();
            List<knnNode> high = A.Where(x => x.Value >= pivot.Value).ToList();

            int m = low.Count;
            if (i < m) return MedianOfMedians(low, i);
            if (i > m) return MedianOfMedians(high, i - m - 1);
            return pivot;
        }

        double DescriptorDistance(SIFTFeature feat0, SIFTFeature feat1)
        {
            float[] data0 = ((FeatureDescriptor<float>)feat0.Descriptor).Data;
            float[] data1 = ((FeatureDescriptor<float>)feat1.Descriptor).Data;

            double res = 0;

            for (int i = 0; i < data0.Length; i++)
            {
                res += Math.Pow(data0[i] - data1[i], 2);
            }

            return Math.Sqrt(res);
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
