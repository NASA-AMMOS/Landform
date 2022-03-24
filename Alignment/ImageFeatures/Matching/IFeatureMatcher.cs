using System;
using System.Collections;
using System.Collections.Generic;

namespace OPS.Alignment
{
    /// <summary>
    /// Interface for feature matching strategies.
    /// </summary>
    public interface IFeatureMatcher
    {
        IEnumerable<FeatureMatch> Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures);
    }
}
