using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;

namespace OPS.Alignment
{
    public class MoisanStivalFilter : IMatchFilter
    {
        //minimum number of matches this filter can process without crashing 
        private const int MIN_MATCHES = 8; 

        public int MaxIterations = 5000;
        public bool RefineStep = true;
        public bool VirtualLinearCoordinates = true;

        public EpipolarMatrix LastEpipolarTransform;
        public Matrix LastBestTransform;

        public delegate SceneNode ImageNodeDelegate(string imageUrl);

        private readonly ImageNodeDelegate imageToNode;
        private readonly ILogger logger;

        public MoisanStivalFilter(ILogger logger = null, ImageNodeDelegate imageToNode = null)
        {
            this.logger = logger;
            this.imageToNode = imageToNode;
        }

        public ImagePairCorrespondence Filter(AlignmentScene scene, ImagePairCorrespondence matches)
        {
            var modelUrl = matches.ModelImageUrl;
            var dataUrl = matches.DataImageUrl;
            var modelFeatures = scene.DetectedFeatures[modelUrl];
            var dataFeatures = scene.DetectedFeatures[dataUrl];
            var modelNode = scene.ObservationUrlToNode[modelUrl];
            var dataNode = scene.ObservationUrlToNode[dataUrl];
            return Filter(modelFeatures, dataFeatures, matches, modelNode, dataNode);
        }

        public ImagePairCorrespondence Filter(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                              ImagePairCorrespondence matches)
        {
            var modelNode = imageToNode(matches.ModelImageUrl);
            var dataNode = imageToNode(matches.DataImageUrl);
            return Filter(modelFeatures, dataFeatures, matches, modelNode, dataNode);
        }

        public ImagePairCorrespondence Filter(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                              ImagePairCorrespondence matches,
                                              SceneNode modelNode, SceneNode dataNode)
        {
            var modelImg = modelNode.GetOrAddComponent<NodeImage>();
            var dataImg = dataNode.GetOrAddComponent<NodeImage>();

            if (!modelImg.Size.HasValue || !dataImg.Size.HasValue)
            {
                throw new ArgumentException("MoisanStivalFilter requires image sizes");
            }

            if (VirtualLinearCoordinates && (modelImg.CameraModel == null || dataImg.CameraModel == null))
            {
                throw new ArgumentException("MoisanStivalFilter with VirtualLinearCoordinates requires camera models");
            }

            return Filter(modelFeatures, dataFeatures, matches, modelImg.CameraModel, dataImg.CameraModel,
                          modelImg.Size.Value, dataImg.Size.Value);
        }

        public ImagePairCorrespondence Filter(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                              ImagePairCorrespondence matches,
                                              CameraModel modelCam, CameraModel dataCam,
                                              Vector2 modelSize, Vector2 dataSize)
        {
            if (matches.DataToModel.Length < MIN_MATCHES)
            {
                return matches;
            }
            
            if (VirtualLinearCoordinates)
            {
                FeatureLinearizer lin = new FeatureLinearizer();
                modelFeatures = lin.Linearize(modelCam, modelFeatures);
                dataFeatures = lin.Linearize(dataCam, dataFeatures);
            }

            Vector2[] dataPoints = matches.DataToModel.Select(pair => dataFeatures[pair.Key].Location).ToArray();
            Vector2[] modelPoints = matches.DataToModel.Select(pair => modelFeatures[pair.Value].Location).ToArray();
            
            MoisanStivalEpipolar mso = new MoisanStivalEpipolar(modelPoints, dataPoints, modelSize, dataSize, logger);

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

            return new ImagePairCorrespondence(matches.ModelImageUrl, matches.DataImageUrl,
                                               goodMatches, mso.FundamentalMatrix, mso.BestTransform);
        }
    }
}
