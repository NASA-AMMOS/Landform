using System.Collections.Generic;

namespace JPLOPS.ImageFeatures
{
    /// <summary>
    /// Interface for feature matching strategies.
    /// </summary>
    public interface IFeatureMatcher
    {
        IEnumerable<FeatureMatch> Match(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures);
    }
}
