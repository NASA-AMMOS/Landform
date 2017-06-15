using OPS.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public interface IFeatureMatcher
    {
        ImagePairCorrespondence Match(ImageRef model, ImageRef data,
            IEnumerable<ImageFeature> modelFeatures,
            IEnumerable<ImageFeature> dataFeatures);
    }
}
