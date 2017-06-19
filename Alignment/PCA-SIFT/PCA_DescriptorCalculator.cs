using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment.PCA_SIFT
{
    class PCA_DescriptorCalculator
    {
        void sdfg()
        {
            var sd = new SIFTDetector();
            Calculate(sd.Detect(Imaging.Image.Load("sfghdfgljhls")));
        }
        public void Calculate(IEnumerable<ImageFeature> features)
        {
            foreach (var f in features)
            {
                f.Descriptor = // calculate them
            }
        }
    }
}
