using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    /// <summary>
    /// A computation that is persisted to disk.
    /// </summary>
    public abstract class PersistedComputation : Computation
    {
        /// <summary>
        /// Serialize the result of this computation to a byte array.
        /// </summary>
        public abstract byte[] Serialize();

        /// <summary>
        /// Populate the result from a previously serialized byte array.
        /// </summary>
        /// <returns>True for success</returns>
        public abstract bool Deserialize(byte[] data);

        /// <summary>
        /// A unique string identifying the result of this computation.
        /// 
        /// Will be used to store the serialized result.
        /// </summary>
        public abstract string Identifier { get; }
    }
}
