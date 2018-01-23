using System;

namespace OPS.Alignment
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

        public PCASIFTDescriptor(float[] data)
        {
            if (data.Length != Length)
            {
                throw new ArgumentException("Descriptor must have length " + Length.ToString());
            }
            this.Data = data;
        }

        public float[] GetData()
        {
            return (float[])Data.Clone();
        }
    }
}
