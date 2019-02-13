using OPS.Util;
using System.Collections.Generic;

namespace OPS.Alignment
{
    /// <summary>
    /// Interface for feature matching strategies.
    /// </summary>
    public interface IFeatureMatcher
    {
        /// <summary>
        /// Match features between a pair of images and return the
        /// set of corresponding points.
        /// </summary>
        ImagePairCorrespondence Match(AlignmentScene scene, URLPair pair);
    }
}
