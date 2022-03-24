using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.ImageFeatures
{
    /// <summary>
    /// Interface for image correspondence filters.
    /// </summary>
    public interface IMatchFilter
    {
        ImagePairCorrespondence Filter(ImageFeature[] modelFeatures, ImageFeature[] dataFeatures,
                                       ImagePairCorrespondence matches);
    }

}
