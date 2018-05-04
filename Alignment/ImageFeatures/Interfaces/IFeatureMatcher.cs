using OPS.Imaging;
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
        /// <param name="model">Model image reference</param>
        /// <param name="data">Data image reference</param>
        /// <param name="modelFeatures">List of features in model image</param>
        /// <param name="dataFeatures">List of features in data image</param>
        ImagePairCorrespondence Match(AlignmentScene scene, UnorderedImagePair pair);
    }
}
