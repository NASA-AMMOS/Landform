using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Alignment
{
    /// <summary>
    /// Base class for all feature descriptor types.
    /// 
    /// All descriptors should have an array of numeric elements.
    /// 
    /// In the common case you should derive from <see cref="FeatureDescriptor{T}"/>,
    /// not this class.
    /// </summary>
    public abstract class FeatureDescriptor
    {
        /// <summary>
        /// The type of element in the descriptor array.
        /// </summary>
        public abstract Type ElementType { get; }

        /// <summary>
        /// Number of entries in the descriptor array.
        /// </summary>
        public abstract int Length { get; }
    }

    /// <summary>
    /// Base class for feature descriptors with element type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Numeric type of descriptor elements</typeparam>
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

        /// <summary>
        /// Array of descriptor elements.
        /// </summary>
        public T[] Data;
    }
}
