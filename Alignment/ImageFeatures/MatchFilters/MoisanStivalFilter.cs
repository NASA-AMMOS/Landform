using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using Microsoft.Xna.Framework;
using log4net;
using OPS.Geometry;

namespace OPS.Alignment
{
    public class MoisanStivalFilter : IMatchFilter
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(MoisanStivalFilter));

        //minimum number of matches this filter can process without crashing 
        private const int MIN_MATCHES = 8; 

        public int MaxIterations;
        public bool RefineStep;
        public bool VirtualLinearCoordinates;

        public EpipolarMatrix LastEpipolarTransform;
        public Matrix LastBestTransform;

        private IImageLoader loader;

        public MoisanStivalFilter(IImageLoader loader, int maxIterations = 5000, bool refineStep = true,
                                  bool virtualLinearCoordinates = true)
        {
            this.loader = loader;
            this.MaxIterations = maxIterations;
            this.RefineStep = refineStep;
            this.VirtualLinearCoordinates = virtualLinearCoordinates;
        }

        public ImagePairCorrespondence Filter(AlignmentScene scene, ImagePairCorrespondence matches)
        {
            if (matches.DataToModel.Length < MIN_MATCHES)
            {
                return matches;
            }

            var modelImg = loader.LoadImage(matches.ModelImageUrl);
            var dataImg = loader.LoadImage(matches.DataImageUrl);
            ImageFeature[] modelFeatures = scene.DetectedFeatures[matches.ModelImageUrl];
            ImageFeature[] dataFeatures = scene.DetectedFeatures[matches.DataImageUrl];

            if (VirtualLinearCoordinates && modelImg.CameraModel != null && dataImg.CameraModel != null)
            {
                FeatureLinearizer lin = new FeatureLinearizer();
                modelFeatures = lin.Linearize(modelImg.CameraModel, modelFeatures);
                dataFeatures = lin.Linearize(dataImg.CameraModel, dataFeatures);
            }

            Vector2[] dataPoints = matches.DataToModel.Select(pair => dataFeatures[pair.Key].Location).ToArray();
            Vector2[] modelPoints = matches.DataToModel.Select(pair => modelFeatures[pair.Value].Location).ToArray();
            
            MoisanStivalEpipolar mso = new MoisanStivalEpipolar(modelPoints, dataPoints,
                                                                new Vector2(modelImg.Width, modelImg.Height),
                                                                new Vector2(dataImg.Width, dataImg.Height));

            mso.Run(MaxIterations, RefineStep);
            if (!mso.Meaningful) return ImagePairCorrespondence.Empty;
            LastEpipolarTransform = mso.FundamentalMatrix;

            List<KeyValuePair<int, int>> goodMatches = new List<KeyValuePair<int, int>>();
            foreach (int idx in mso.ComputeInliers())
            {
                goodMatches.Add(matches.DataToModel[idx]);
            }

            Vector2[] dataPointsAfter = goodMatches.Select(pair => dataFeatures[pair.Key].Location).ToArray();
            Vector2[] modelPointsAfter = goodMatches.Select(pair => modelFeatures[pair.Value].Location).ToArray();

            LastBestTransform = mso.BestTransform;
            logger.Info("Number of residual matches: " + goodMatches.Count);
            return new ImagePairCorrespondence(matches.ModelImageUrl, matches.DataImageUrl,
                                               goodMatches, mso.FundamentalMatrix, mso.BestTransform);
        }
    }
}
