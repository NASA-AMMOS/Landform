using OPS.Imaging;
using OPS.Pipeline;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public interface IFeatureDetector
    {
        IEnumerable<ImageFeature> Detect(Image image, Image mask=null);
    }
}
