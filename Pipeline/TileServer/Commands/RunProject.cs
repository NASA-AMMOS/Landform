using CommandLine;
using log4net;
using OPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using OPS.Pipeline;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline.TileServer
{
    [Verb("runproject", HelpText = "Runs a tiling workflow")]
    public class RunProjectOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }

        [Option(Default = false, HelpText = "wait until project has finished running")]
        public bool Wait { get; set; }

        [Option(Default = false, HelpText = "run locally, do not connect to cloud")]
        public bool Local { get; set; }
    }

    public class RunProject
    {
        const int MAX_WAIT_SEC = 60 * 60 * 10; //10h
        const int SLEEP_MS = 500;

        private RunProjectOptions options;
        private PipelineCore pipeline;
        private PipelineExecutive executive;

        public RunProject(RunProjectOptions options)
        {
            this.options = options;
            if (options.Local)
            {
                options.Wait = true;
            }
            pipeline = TileServerCommands.MakePipeline(options, options.Local);
            if (options.Local)
            {
                executive = PipelineExecutive.MakeExecutive(pipeline, ExecutionMode.Deferred);
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

            pipeline.LogInfo("running project \"{0}\"", options.ProjectName);
            pipeline.EnqueueToMaster(new RunProjectMessage(options.ProjectName));

            if (options.Wait)
            {
                pipeline.LogInfo("waiting for project \"{0}\" to finish running", options.ProjectName);
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    if (sw.ElapsedMilliseconds > MAX_WAIT_SEC * 1000)
                    {
                        pipeline.LogError("project \"{0}\" not finished in {1}s", options.ProjectName, MAX_WAIT_SEC);
                        return 2; //internal error
                    }
                    Thread.Sleep(SLEEP_MS);

                    //re-fetch project record to ensure database synchronization
                    project = TilingProject.Find(pipeline, options.ProjectName);
                }
                while (project != null && !project.FinishedRunning);

                if (project != null)
                {
                    pipeline.LogInfo("project \"{0}\" finished running", options.ProjectName);
                }
            }

            if (executive is DeferredExecutive)
            {
                (executive as DeferredExecutive).Quit();
            }

            return 0;
        }
    }
}
