using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.TilingServer;

namespace OPS.TilingServer
{
    [Verb("runproject", HelpText = "Runs a tiling workflow")]
    public class RunProjectOptions : TilingServerCommandOptions
    {
        [Option(Default = false, HelpText = "wait until project has finished running")]
        public bool Wait { get; set; }
    }

    public class RunProject : TilingServerCommand
    {
        const int MAX_WAIT_SEC = 60 * 60 * 10; //10h
        const int SLEEP_MS = 500;

        private RunProjectOptions options;

        public RunProject(RunProjectOptions options) : base(options, ExecutionMode.Deferred)
        {
            this.options = options;

            if (options.Local)
            {
                options.Wait = true;
            }
        }

        public int Run()
        {
            try
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
                            pipeline.LogError("project \"{0}\" not done in {1}",
                                              options.ProjectName, Fmt.HMS(MAX_WAIT_SEC * 1000));
                            return 2; //internal error
                        }
                        Thread.Sleep(SLEEP_MS);
                        
                        //re-fetch project record to ensure database synchronization
                        project = TilingProject.Find(pipeline, options.ProjectName);

                        if (executive != null)
                        {
                            Exception ex = executive.MasterError ?? executive.WorkerError;
                            if (ex != null)
                            {
                                if (executive is DeferredExecutive)
                                {
                                    ((DeferredExecutive)executive).Quit();
                                }
                                throw ex;
                            }
                        }
                    }
                    while (project != null && !project.FinishedRunning);
                    
                    if (project != null)
                    {
                        pipeline.LogInfo("project \"{0}\" finished running{1}", options.ProjectName,
                                         !string.IsNullOrEmpty(project.ExecutionError) ?
                                         (" with " + project.ExecutionError) : "");
                    }
                }
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }
            return 0;
        }
    }
}
