using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MathNet.Numerics.LinearAlgebra;
using log4net;
using OPS.Util;

namespace OPS.Alignment
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

        public ImagePairCorrespondence Match(AlignmentScene scene, string modelUrl, string dataUrl)
        {
            var modelFeatures = scene.DetectedFeatures[modelUrl]; 
            var dataFeatures = scene.DetectedFeatures[dataUrl];
            return new ImagePairCorrespondence(modelUrl, dataUrl, Match(modelFeatures, dataFeatures));
        }

        public IEnumerable<FeatureMatch> Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures)
        {
            if (modelFeatures.Length < 1 || dataFeatures.Length < 1) yield break;
            double maxDistanceRatioSq = MaxDistanceRatio * MaxDistanceRatio;
            for (int dataIndex = 0; dataIndex < dataFeatures.Length; dataIndex++)
            {
                var match = FindBestModelFeatureForDataFeature(modelFeatures, dataFeatures, dataIndex,
                                                               maxDistanceRatioSq);
                if (match != null)
                {
                    yield return match;
                }
            }
        }

        public static FeatureMatch FindBestModelFeatureForDataFeature(ImageFeature[] modelFeatures,
                                                                      ImageFeature[] dataFeatures, int dataIndex,
                                                                      double maxDistanceRatioSq,
                                                                      Func<ImageFeature, bool> filter = null)
        {
            //find 2 closest model features for this data feature
            double closestDistSq = double.PositiveInfinity;
            double secondClosestDistSq = double.PositiveInfinity;
            int closestModelIndex = -1;
            int secondClosestModelIndex = -1;
            var dataDesc = dataFeatures[dataIndex].Descriptor;
            for (int modelIndex = 0; modelIndex < modelFeatures.Length; modelIndex++)
            {
                var modelFeature = modelFeatures[modelIndex];
                if (filter != null && !filter(modelFeature))
                {
                    continue;
                }

                double distSq = dataDesc.L2DistanceSquared(modelFeature.Descriptor);
                if (distSq < secondClosestDistSq)
                {
                    if (distSq < closestDistSq)
                    {
                        closestDistSq = distSq;
                        closestModelIndex = modelIndex;
                    }
                    else
                    {
                        secondClosestDistSq = distSq;
                        secondClosestModelIndex = modelIndex;
                    }
                }
            }

            //keep match iff bestDist/2ndBestDist <= MaxDistanceRatio
            if (closestModelIndex >= 0 &&
                (secondClosestModelIndex < 0 || (closestDistSq <= secondClosestDistSq * maxDistanceRatioSq)))
            {
                return new FeatureMatch()
                {
                    DataIndex = dataIndex,
                    ModelIndex = closestModelIndex,
                    DescriptorDistance = Math.Sqrt(closestDistSq)
                };
            }
            else
            {
                return null;
            }
        }
    }
}
