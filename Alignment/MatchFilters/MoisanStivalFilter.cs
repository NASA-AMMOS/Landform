using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Pipeline;
using Microsoft.Xna.Framework;

namespace OPS.Alignment
{
    public class MoisanStivalFilter : IMatchFilter
    {
        public int MaxIterations;
        public bool RefineStep;
        public MoisanStivalFilter(int maxIterations = 5000, bool refineStep = true)
        {
            this.MaxIterations = maxIterations;
            this.RefineStep = refineStep;
        }

        public ImagePairCorrespondence Filter(ImagePairCorrespondence matches)
        {
            ImageFeature[] modelFeat, dataFeat;
            int[] dataToModel;
            matches.Flatten(out modelFeat, out dataFeat, out dataToModel);

            Vector2[] dataPoints = dataFeat.Select(f => f.Location).ToArray();
            Vector2[] modelPoints = Enumerable.Range(0, dataPoints.Length).Select(idx => modelFeat[dataToModel[idx]].Location).ToArray();

            var modelMeta = matches.ModelImage.Metadata;
            var dataMeta = matches.DataImage.Metadata;

            MoisanStivalEpipolar mso = new MoisanStivalEpipolar(
                modelPoints, dataPoints,
                new Vector2(modelMeta.Width, modelMeta.Height),
                new Vector2(dataMeta.Width, dataMeta.Height)
                );

            mso.Run(MaxIterations, RefineStep);
            if (!mso.Meaningful) return null;

            List<KeyValuePair<int, int>> goodMatches = new List<KeyValuePair<int, int>>();
            foreach (int idx in mso.ComputeInliers())
            {
                goodMatches.Add(matches.DataToModel[idx]);
            }
            System.Diagnostics.Debug.WriteLine("Number of residual matches: " + goodMatches.Count);
            return new ImagePairCorrespondence(
                matches.ModelImage, matches.DataImage,
                matches.ModelFeatures, matches.DataFeatures,
                goodMatches);
        }
    }
}
