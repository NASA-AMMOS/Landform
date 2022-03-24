using System.Collections.Generic;

namespace JPLOPS.ImageFeatures
{
    public class DescriptorDistanceFilter : IMatchFilter
    {
        private double maxDistance;
        public DescriptorDistanceFilter(double maxDistance)
        {
            this.maxDistance = maxDistance;
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
