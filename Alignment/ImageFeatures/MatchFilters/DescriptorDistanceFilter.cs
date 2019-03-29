using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;
using log4net;

namespace OPS.Alignment
{
    public class DescriptorDistanceFilter : IMatchFilter
    {
        private double maxDistance;
        public DescriptorDistanceFilter(double maxDistance)
        {
            this.maxDistance = maxDistance;
        }

        public ImagePairCorrespondence Filter(AlignmentScene scene, ImagePairCorrespondence matches)
        {
            var modelUrl = matches.ModelImageUrl;
            var dataUrl = matches.DataImageUrl;
            var modelFeatures = scene.DetectedFeatures[modelUrl];
            var dataFeatures = scene.DetectedFeatures[dataUrl];
            return Filter(modelFeatures, dataFeatures, matches);
        }

        public ImagePairCorrespondence Filter(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                              ImagePairCorrespondence matches)
        {
            var goodMatches = new List<FeatureMatch>();
            for (int i = 0; i < matches.DataToModel.Length; i++)
            {
                if (matches.DescriptorDistance[i] <= maxDistance)
                {
                    goodMatches.Add(new FeatureMatch()
                                    {
                                        DataIndex = matches.DataToModel[i].Key,
                                        ModelIndex = matches.DataToModel[i].Value,
                                        DescriptorDistance = matches.DescriptorDistance[i]
                                    });
                }
            }
            return new ImagePairCorrespondence(matches.ModelImageUrl, matches.DataImageUrl, goodMatches,
                                               matches.FundamentalMatrix, matches.BestTransformEstimate);
        }
    }
}
