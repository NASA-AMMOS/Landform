using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class IngestAlignmentInputs : PipelineRoutine
    {
        public IngestAlignmentInputs(PipelineCore pipeline) : base(pipeline) { }

        public int Ingest(Project project, MSLLocations mslLocations, Action<IngestImage.Result> func = null)
        {
            string baseUrl = project.InputPath;
            if (!baseUrl.EndsWith("/"))
            {
                //search will return 0 objects if slash is not postfixed
                // not requesting recursively, which means it is using slash delimiter
                baseUrl += "/";
            }

            pipeline.LogInfo("ingesting input files from {0} for alignment project {1}", baseUrl, project.Name);

            IngestPDSImage ingester = new IngestPDSImage(pipeline, mslLocations, project.Name);

            int n = 0;
            Parallel.ForEach(pipeline.SearchFiles(baseUrl, "*.IMG", false), url =>
            {
                var res = ingester.Ingest(url);
                if (res.Status == IngestImage.Status.Added || res.Status == IngestImage.Status.Duplicate) //TODO??
                {
                    Interlocked.Increment(ref n);
                    if (func != null) func(res);
                }
            });

            pipeline.LogInfo("ingested {0} input files for alignment project {1}", n, project.Name);

            return n;
        }
    }
}
