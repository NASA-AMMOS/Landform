using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OPS.Alignment
{
    /// <summary>
    /// Graph Transformation Matching (GTM) algorithm, as explained in http://www.sciencedirect.com/science/article/pii/S0262885608001078#aep-section-id31
    /// </summary>
    public class GTM : IMatchFilter
    {
        public int MaxIterations;
        public bool RefineStep;
        public int K;

        /// <summary>
        /// Creates a new GTM filter instance.
        /// </summary>
        /// <param name="k"></param>
        public GTM(int k = 5)
        {
            K = k;
        }

        /// <summary>
        /// Given matches, filters them according to the GTM algorithm.
        /// </summary>
        /// <param name="matches">Identified matches in a pair of images.</param>
        /// <returns>Filtered matches.</returns>
        public ImagePairCorrespondence Filter(AlignmentScene scene, ImagePairCorrespondence matches)
        {
            ImageFeature[] modelFeat = scene.Context.DetectedFeatures[matches.ModelImage];
            ImageFeature[] dataFeat = scene.Context.DetectedFeatures[matches.DataImage];
            KeyValuePair<int, int>[] pairs = matches.DataToModel;

            ImageFeature zero = new ImageFeature(new Vector2(0, 0), null);
            ImageFeature[] P = new ImageFeature[pairs.Length];
            ImageFeature[] PPrime = new ImageFeature[pairs.Length];

            // Create sets P and PPrime, where P[i] and PPrime[i] are matched features
            for (int i = 0; i < pairs.Length; i++) {
                P[i] = modelFeat[pairs[i].Value];
                PPrime[i] = dataFeat[pairs[i].Key];
            }
      
            GTMFilter filter = new GTMFilter(matches, modelFeat, dataFeat, K);

            return filter.Filter();
        }

        /// <summary>
        /// Writes a list to file.
        /// </summary>
        /// <typeparam name="T">Type of list.</typeparam>
        /// <param name="filename">Location to stor</param>
        /// <param name="list"></param>
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

