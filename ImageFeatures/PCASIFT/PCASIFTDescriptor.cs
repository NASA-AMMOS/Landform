using System;

namespace JPLOPS.ImageFeatures
{
    public class PCASIFTDescriptor : FeatureDescriptor<float>
    {
        public override int Length
        {
            get
            {
                return PCAConstants.EPCALEN;
            }
        }

        public override double GetElement(int index)
        {
            return Data[index];
        }

        public PCASIFTDescriptor(float[] data)
        {
            if (data.Length != Length)
            {
                throw new ArgumentException("PCASIFT escriptor must have length " + Length);
            }
            this.Data = data;
        }
    }
}
