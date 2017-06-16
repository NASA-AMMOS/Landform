using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public abstract class Computation
    {
        // API to implement
        /// <summary>
        /// Initialize the computation with its argument.
        /// 
        /// No work should be performed, only initialization of data structures.
        /// </summary>
        /// <param name="arg">The argument to the computation</param>
        public abstract void Init(object arg);

        /// <summary>
        /// Return a list of dependencies of this computation.
        /// </summary>
        public virtual IEnumerable<Reference> Dependencies() { yield break; }

        /// <summary>
        /// Perform the computation.
        /// 
        /// Any references returned from Dependencies() before this function
        /// is called will be evaluated in the passed context.
        /// </summary>
        /// <param name="context">The context to execute in</param>
        public abstract void Compute(ComputationContext context);

        /// <summary>
        /// A reference to a specific memoized computation.
        /// </summary>
        public class Reference
        {
            public readonly Type ComputationType;
            public readonly object Argument;

            public Reference(Type compType, object argument)
            {
                this.ComputationType = compType;
                this.Argument = argument;
            }

            #region GetHashCode & Equals
            public override bool Equals(object obj)
            {
                Reference r = obj as Reference;
                if (r == null) { return false; }

                return r.ComputationType == ComputationType
                    && r.Argument == Argument;
            }

            public override int GetHashCode()
            {
                int hash = 17;
                hash = 23 * hash + ComputationType.GetHashCode();
                hash = 23 * hash + Argument.GetHashCode();
                return hash;
            }
            #endregion GetHashCode & Equals
        }
    }
}
