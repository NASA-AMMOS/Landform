using System;

namespace OPS.Alignment
{
    public class PCA_SIFTDescriptor : FeatureDescriptor<float>
    {
        public override int Length
        {
            get
            {
                return 36;
            }
        }

        public PCA_SIFTDescriptor(float[] data)
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
