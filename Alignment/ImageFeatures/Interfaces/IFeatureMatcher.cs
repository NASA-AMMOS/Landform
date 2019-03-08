using System;
using System.Collections;
using System.Collections.Generic;

namespace OPS.Alignment
{
    public class FeatureMatch
    {
        public int DataIndex;
        public int ModelIndex;
        public double DescriptorDistance;
    }

    /// <summary>
    /// Interface for feature matching strategies.
    /// </summary>
    public interface IFeatureMatcher
    {
        ImagePairCorrespondence Match(AlignmentScene scene, string modelUrl, string dataUrl);

        IEnumerable<FeatureMatch> Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures);
    }
}
