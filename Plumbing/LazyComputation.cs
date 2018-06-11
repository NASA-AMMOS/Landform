using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Plumbing
{
    public class LazyComputation<ArgT, ResT> : PipelineRoutine where ResT: DataProduct, new()
    {
        internal readonly Func<ArgT, Guid> GetExistingGuid;
        internal readonly Func<ArgT, ResT> Compute;

        public LazyComputation(PipelineCore pipeline, Func<ArgT, Guid> getExistingGuid, Func<ArgT, ResT> compute)
            : base(pipeline)
        {
            GetExistingGuid = getExistingGuid;
            Compute = compute;
        }

        public ResT Get(string project, ArgT argument)
        {
            var guid = GetExistingGuid(argument);
            if (guid != Guid.Empty)
            {
                return Get<ResT>(project, guid);
            }
            
            return Compute(argument);
        }
    }
}
