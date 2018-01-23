using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace OPS.Alignment
{
    /// <summary>
    /// GTM Filter class.
    /// </summary>
    public class GTMFilter
    {
        // Number of nearest-neighbors
        int K;

        // Original image correspondence representing matched nodes
        public ImagePairCorrespondence Matches;

        // Keeps track of matched features throughout iteration.
        int[][] FeatureMap;

        // Set of removed vertices.
        HashSet<int> Outliers { get; set; }

        // Set of falsely identified outliers.
        HashSet<int> FalseOutliers { get; set; }

        // Placeholders for deleted pairs, and absent neighbors, respectively.
        const int REMOVED = -1;
        const int FILLER = -2;
        const int FALSE_POSTIVE = -3;

        // The graph representations of matched keypoints in each image.
        GTMGraph ModelGraph, DataGraph;

        /// <summary>
        /// Given matches, filters them using the GTM method of size k.
        /// </summary>
        /// <param name="matches">Identified matches.</param>
        /// <param name="k">The number of nearest neighbors.</param>
        public GTMFilter(ImagePairCorrespondence matches, int k)
        {
            KeyValuePair<int, int>[] pairs = matches.DataToModel;
            ImageFeature[] modelFeat = matches.ModelFeatures;
            ImageFeature[] dataFeat = matches.DataFeatures;

            ImageFeature zero = new ImageFeature(new Vector2(0, 0), null);
            ImageFeature[] P = new ImageFeature[pairs.Length];
            ImageFeature[] PPrime = new ImageFeature[pairs.Length];
            FeatureMap = new int[pairs.Length][];

            // Create sets P and PPrime, where P[i] and PPrime[i] are matched features
            // featureMap is maintained to return correct mapping at conclusion
            for (int i = 0; i < pairs.Length; i++)
            {
                P[i] = modelFeat[pairs[i].Value];
                PPrime[i] = dataFeat[pairs[i].Key];
                FeatureMap[i] = new int[] { pairs[i].Key, pairs[i].Value };
            }

            K = k;
            Matches = matches;
            Outliers = new HashSet<int>();
            FalseOutliers = new HashSet<int>(new int[] { REMOVED, FILLER });
            ModelGraph = new GTMGraph(P, Outliers, FalseOutliers, k);
            DataGraph = new GTMGraph(PPrime, Outliers, FalseOutliers, k);
        }

        /// <summary>
        /// Filters the given matches.
        /// </summary>
        /// <returns>Filtered matches.</returns>
        internal ImagePairCorrespondence Filter()
        {
            int outlier;
            int counter = 0;

            while (!ModelGraph.GraphEqual(DataGraph))
            {
                counter++;
                outlier = ModelGraph.FindOutlier(DataGraph);

                // the following check shouldn't be needed if executing correctly
                if (outlier == -1)
                {
                    break;
                }

                // A bug fix, temporary
                if (!isFalseOutlier(outlier))
                {
                    FalseOutliers.Add(outlier);
                    continue;
                }

                Outliers.Add(outlier);
                ModelGraph.RemoveOutlier(outlier);
                DataGraph.RemoveOutlier(outlier);
            }

            FeatureMap = GTMGraph.RemoveDisconnectedVertices(ModelGraph, DataGraph, FeatureMap);

            if (ModelGraph.Q.Length != DataGraph.Q.Length)
            {
                throw new Exception("Matched features not equal in length.");
            }

            KeyValuePair<int, int>[] goodMatches = GTMGraph.ConstructFinalMatches(FeatureMap);
            Trace.WriteLine("Number of residual matches: " + goodMatches.Length + " after " + counter + " iterations of GTM");
            if (goodMatches.Length == 0) return null;
            return new ImagePairCorrespondence(Matches.ModelImage, Matches.DataImage, Matches.ModelFeatures, Matches.DataFeatures, goodMatches);
        }

        /// <summary>
        /// Checks if determined outlier is valid.
        /// </summary>
        /// <param name="outlier">Outlier candidate.</param>
        /// <returns>True, if the outlier is valid.</returns>
        private bool isFalseOutlier(int outlier)
        {
            if (ModelGraph.O[outlier][0] == DataGraph.O[outlier][0] &&
                ModelGraph.O[outlier][1] == DataGraph.O[outlier][1] &&
                ModelGraph.O[outlier][2] == DataGraph.O[outlier][2] &&
                ModelGraph.O[outlier][3] == DataGraph.O[outlier][3] &&
                ModelGraph.O[outlier][4] == DataGraph.O[outlier][4])
            {
                return false;
            }

            return true;
        }
    }
}
