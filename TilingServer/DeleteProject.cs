using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using CommandLine;
using log4net;
using OPS.Pipeline;
using OPS.Pipeline.TilingServer;

namespace OPS.TilingServer
{
    [Verb("deleteproject", HelpText = "Delete project")]
    public class DeleteProjectOptions : PipelineCoreOptions
    {       
        [Value(0, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }

        [Option(Default = false, HelpText = "Do not wait until project has been deleted")]
        public bool NoWait { get; set; }

        [Option(Default = false, HelpText = "run locally, do not connect to cloud")]
        public bool Local { get; set; }
    }

    public class DeleteProject
    {
        const int MAX_WAIT_MS = 30 * 60 * 1000; //it can take a while to delete a big project
        const int SLEEP_MS = 500;

        private DeleteProjectOptions options;
        private PipelineCore pipeline;
        private PipelineExecutive executive;

        public DeleteProject(DeleteProjectOptions options)
        {
            this.options = options;

            if (options.Local)
            {
                pipeline = new LocalPipeline(options);
                executive = PipelineExecutive.MakeExecutive(pipeline, ExecutionMode.Immediate);
            }
            else
            {
                pipeline = new CloudPipeline(options, queuePrefix: "tiling");
            }
        }

        public int Run()
        {
            var project = TilingProject.Find(pipeline, options.ProjectName);

            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1; //argument error
            }

            if (project.StartedRunning && !project.FinishedRunning)
            {
                pipeline.LogError("cannot delete project \"{0}\", project currently running", options.ProjectName);
                return 1; //argument error
            }

            pipeline.EnqueueToMaster(new DeleteProjectMessage(options.ProjectName));

            if (!options.NoWait)
            {
                pipeline.LogInfo("waiting for project \"{0}\" to be deleted", options.ProjectName);
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    if (sw.ElapsedMilliseconds > MAX_WAIT_MS)
                    {
                        pipeline.LogError("project \"{0}\" not deleted in {1}ms", options.ProjectName, MAX_WAIT_MS);
                        return 2; //internal error
                    }
                    Thread.Sleep(SLEEP_MS);
                    project = TilingProject.Find(pipeline, options.ProjectName);
                }
                while (project != null);

                pipeline.LogInfo("project \"{0}\" deleted", options.ProjectName);
            }

            return 0;
        }
    }
}
