using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    /// <summary>
    /// Interface for visual feature detectors.
    /// </summary>
    public interface IFeatureDetector
    {
        /// <summary>
        /// Detect visual features in an image, potentially ignoring masked out areas.
        /// </summary>
        /// <param name="image">Image to detect features in</param>
        /// <param name="mask">Optional mask, where zero pixels are unused</param>
        IEnumerable<ImageFeature> Detect(Image image, Image mask = null);

        void AddDescriptors(Image image, IEnumerable<ImageFeature> features);
    }
}
