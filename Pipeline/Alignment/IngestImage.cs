using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using OPS.Cloud;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public abstract class IngestImage : PipelineRoutine
    {
        public IngestImage(PipelineCore pipeline) : base(pipeline) { }

        public enum Status
        {
            Added,
            Duplicate,
            Failed,
            Skipped,
            Culled
        }

        /// <summary>
        /// Result of an IngestImage operation. Consists of a status code and (potentially) a new Observation entry.
        /// </summary>
        public class Result
        {
            public Status Status;

            public readonly string Url;
            public readonly string DataUrl; //if null then same as Url
            public readonly Observation Observation;
            public readonly Frame ObservationFrame;

            public bool Accepted
            {
                get
                {
                    //duplicates are OK to allow ingestion being re-run on an existing proj
                    return Status == Status.Added || Status == Status.Duplicate;
                }
            }

            public Result()
            {
                Status = Status.Failed;
            }

            public Result(string url, string dataUrl, Status status, Observation obs = null, Frame obsFrame = null)
            {
                this.Url = url;
                this.DataUrl = dataUrl;
                this.Status = status;
                this.Observation = obs;
                this.ObservationFrame = obsFrame;
            }

            public override string ToString()
            {
                return string.Format("{0}{1} ({2})", Url, DataUrl != null ? (":" + DataUrl) : "", Status);
            }
        }

        public abstract Result Ingest(string imgUrl);
    }
}
