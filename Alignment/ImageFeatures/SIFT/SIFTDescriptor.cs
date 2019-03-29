using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public class SIFTDescriptor : FeatureDescriptor<byte>
    {
        public override int Length
        {
            get
            {
                return 128;
            }
        }

        public override double GetElement(int index)
        {
            return Data[index];
        }

        public SIFTDescriptor(byte[] data)
        {
            if (data.Length != Length)
            {
                throw new ArgumentException("SIFT descriptor must have length " + Length + ", got " + data.Length);
            }
            this.Data = data;
        }
    }
}
