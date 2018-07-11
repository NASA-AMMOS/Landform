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
        public SIFTDescriptor(byte[] data)
        {
            if (data.Length != Length)
            {
                throw new ArgumentException("Descriptor must have length " + Length.ToString());
            }
            this.Data = data;
        }
    }
}
