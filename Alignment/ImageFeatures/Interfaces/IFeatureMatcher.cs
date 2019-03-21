using System;
using System.Collections;
using System.Collections.Generic;
using OPS.Util;

namespace OPS.Alignment
{
    public class FeatureMatch
    {
        public int DataIndex;
        public int ModelIndex;

        public double DescriptorDistance;

        public override int GetHashCode()
        {
            return HashCombiner.Combine(DataIndex, ModelIndex);
        }

        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is FeatureMatch))
            {
                return false;
            }
            return this.DataIndex == ((FeatureMatch)obj).DataIndex && this.ModelIndex == ((FeatureMatch)obj).ModelIndex;
        }
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
