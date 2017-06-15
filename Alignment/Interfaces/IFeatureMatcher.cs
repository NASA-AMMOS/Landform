using OPS.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        ImagePairCorrespondence Match(ImageRef model, ImageRef data,
            IEnumerable<ImageFeature> modelFeatures,
            IEnumerable<ImageFeature> dataFeatures);
    }
}
