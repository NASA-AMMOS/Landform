using OPS.Cloud;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    public abstract class IngestImage : PipelineRoutine
    {
        public IngestImage(PipelineCore pipeline) : base(pipeline)
        {
        }

        public enum Status
        {
            Added,
            Failed,
            Duplicate,
            Skipped
        }

        /// <summary>
        /// Result of an IngestImage operation. Consists of a status code and (potentially) a new Observation entry.
        /// </summary>
        public class Result
        {
            public Status Status;
            public Observation Observation;

            public Result()
            {
                Status = Status.Failed;
                Observation = null;
            }
            public Result(Status status, Observation obs)
            {
                Status = status;
                Observation = obs;
            }
        }

        public abstract Result Ingest(S3ImageRef imgRef);
    }
}
