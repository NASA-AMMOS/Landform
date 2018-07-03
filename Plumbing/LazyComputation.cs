using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using log4net;

namespace OPS.Plumbing
{
    public class LazyComputation<ArgT, ResT> : PipelineRoutine where ResT: DataProduct, new()
    {
        static ILog logger = LogManager.GetLogger(typeof(LazyComputation<ArgT, ResT>));
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
                try
                {
                    return Get<ResT>(project, guid);
                }
                catch (Amazon.S3.AmazonS3Exception ex)
                {
                    if (ex.ErrorCode != "NotFound")
                    {
                        throw;
                    }
                    logger.ErrorFormat("Product {0} does not exist in S3", guid.ToString());
                }
                catch (Exception ex)
                {
                    logger.Error("Failed to load product " + guid.ToString(), ex);
                }
            }
            
            return Compute(argument);
        }
    }
}
