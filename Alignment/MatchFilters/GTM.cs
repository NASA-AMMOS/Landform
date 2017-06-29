using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            //             ImageCorrespondence fields
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

            Q = RemoveDisconnectedVertices(AP);
            QPrime = RemoveDisconnectedVertices(APPrime);

            return new ImagePairCorrespondence(
                matches.ModelImage, matches.DataImage,
                matches.ModelFeatures, matches.DataFeatures,
                goodMatches);
        }

        private ImageFeature[] RemoveDisconnectedVertices(double[][] aPPrime)
        {
            throw new NotImplementedException();
        }

        void RemoveOutlier(int outlier, double[][] distP, double[][] distPPrime, ImageFeature[] Q, ImageFeature[] QPrime)
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

                for (int ki = 0; ki < k; ki++) // TODO could get out of bounds if not enough neighbors
                {
                    result[i][distances[ki].Index] = 1;
                }
            }

            return result;
        }

        double ComputeMedian(double[][] dist) // could be optimized to linear time, instead of nlogn
        { 
            List<double> distances = new List<double>();
            
            // not sure if should include diagonal of zeroes
            for (int i = 0; i < dist.Length; i++)
            {
                distances.AddRange(dist[i].Where(x => x > 0));
            }
            distances.Sort();
            return distances[distances.Count / 2];
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="features"></param>
        /// <returns></returns>
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

