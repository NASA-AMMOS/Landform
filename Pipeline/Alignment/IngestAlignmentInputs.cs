using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Pipeline
{
    public class IngestAlignmentInputs : PipelineRoutine
    {
        public string BaseUrl;
        private Project project;
        private IngestImage ingester;

        public IngestAlignmentInputs(PipelineCore pipeline, Project project, MSLLocations mslLocations,
                                     bool recreateExistingObservations = false, bool resetExistingTransforms = false)
            : base(pipeline)
        {
            BaseUrl = StringHelper.EnsureTrailingSlash(project.InputPath);
            this.project = project;
            ingester = new IngestPDSImage(pipeline, project, mslLocations, recreateExistingObservations,
                                          resetExistingTransforms);
        }

        public int Ingest(Action<IngestImage.Result> func = null, bool recursive = true)
        {
            
            pipeline.LogInfo("ingesting input files from {0} for alignment project {1}", BaseUrl, project.Name);
        
            int n = 0;
            Parallel.ForEach(pipeline.SearchFiles(BaseUrl, "*.IMG", recursive: recursive), url =>
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
