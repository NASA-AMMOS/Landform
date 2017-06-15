using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    public abstract class FeatureDescriptor
    {
        public abstract Type ElementType { get; }
        public abstract int Length { get; }
    }

    public abstract class FeatureDescriptor<T> : FeatureDescriptor
        where T : struct
    {
        public override Type ElementType
        {
            get
            {
                return typeof(T);
            }
        }

        public T[] Data;
    }
}
