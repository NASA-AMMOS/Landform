using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public interface IDescriptorCalculator
    {
        /// <summary>
        /// Compute descriptors for each feature passed.
        /// 
        /// After execution, the descriptor field of each ImageFeature should
        /// be populated with a FeatureDescriptor object.
        /// </summary>
        /// <param name="image">Image containing features</param>
        /// <param name="features">Feature points</param>
        void ComputeDescriptors(Image image, IEnumerable<ImageFeature> features);
    }
}
