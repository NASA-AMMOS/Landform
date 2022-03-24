using System;
using System.Collections.Generic;

namespace JPLOPS.ImageFeatures
{
    /// <summary>
    /// Given two images and a list of features in each and 
    /// returns a set of matches between them using nearest descriptor distance (L2Norm)
    /// </summary>
    public class BruteForceMatcher : IFeatureMatcher
    {
        //maximum ratio between distance of nearest data feature descriptor to model feature descriptor
        //vs 2nd nearest data feature descriptor to the same model feature descriptor
        //set to 1 to disable filtering by this ratio
        public double MaxDistanceRatio = 0.9;

        public IEnumerable<FeatureMatch> Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures)
        {
            if (modelFeatures.Length < 1 || dataFeatures.Length < 1) yield break;
            double maxDistanceRatio = modelFeatures[0].Descriptor.BestDistanceToFastDistance(MaxDistanceRatio);
            for (int dataIndex = 0; dataIndex < dataFeatures.Length; dataIndex++)
            {
                var match = FindBestModelFeatureForDataFeature(modelFeatures, dataFeatures, dataIndex,
                                                               maxDistanceRatio);
                if (match != null)
                {
                    yield return match;
                }
            }
        }

        public static FeatureMatch FindBestModelFeatureForDataFeature(ImageFeature[] modelFeatures,
                                                                      ImageFeature[] dataFeatures, int dataIndex,
                                                                      double maxDistanceRatio,
                                                                      Func<ImageFeature, bool> filter = null,
                                                                      int firstModelIndex = 0,
                                                                      int lastModelIndex = -1)
        {
            //find 2 closest model features for this data feature
            double closestDist = double.PositiveInfinity;
            double secondClosestDist = double.PositiveInfinity;
            int closestModelIndex = -1;
            int secondClosestModelIndex = -1;
            var dataDesc = dataFeatures[dataIndex].Descriptor;
            if (lastModelIndex < 0)
            {
                lastModelIndex = modelFeatures.Length - 1;
            }
            for (int modelIndex = firstModelIndex; modelIndex <= lastModelIndex; modelIndex++)
            {
                var modelFeature = modelFeatures[modelIndex];
                if (filter != null && !filter(modelFeature))
                {
                    continue;
                }

                double dist = dataDesc.FastDistance(modelFeature.Descriptor);
                if (dist < secondClosestDist)
                {
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestModelIndex = modelIndex;
                    }
                    else
                    {
                        secondClosestDist = dist;
                        secondClosestModelIndex = modelIndex;
                    }
                }
            }

            //keep match iff bestDist/2ndBestDist <= MaxDistanceRatio
            if (closestModelIndex >= 0 &&
                (secondClosestModelIndex < 0 ||
                 dataDesc.CheckFastDistanceRatio(closestDist, secondClosestDist, maxDistanceRatio)))
            {
                return new FeatureMatch()
                {
                    DataIndex = dataIndex,
                    ModelIndex = closestModelIndex,
                    DescriptorDistance = (float)dataDesc.FastDistanceToBestDistance(closestDist)
                };
            }
            else
            {
                return null;
            }
        }
    }
}
