using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public class BRIEFDescriptor : FeatureDescriptor<byte>
    {
        public override int Length
        {
            get
            {
                return Data.Length;
            }
        }

        public override double GetElement(int index)
        {
            return Data[index];
        }

        public BRIEFDescriptor(byte[] data)
        {
            this.Data = data;
        }
    }
}
