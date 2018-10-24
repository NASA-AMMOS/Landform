using CommandLine;
using log4net;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace OPS.Pipeline.TileServer
{
    [Verb("deleteproject", HelpText = "Delete project")]
    public class DeleteProjectOptions : PipelineCoreOptions
    {       
        [Value(0, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }

        [Option(Default = false, HelpText = "Do not wait until project has been deleted")]
        public bool NoWait { get; set; }
    }

    public class DeleteProject : PipelineCore
    {
        const int MAX_WAIT_MS = 30 * 60 * 1000; //it can take a while to delete a big project
        const int SLEEP_MS = 500;

        private DeleteProjectOptions options;

        public DeleteProject(DeleteProjectOptions options)
            : base(options, TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            var cloud = new TileServerCloud(this);

            var project = TilingProject.Find(DynamoContext, options.ProjectName);

            if (project == null)
            {
                Logger.Error("No project by that name found: " + options.ProjectName);
                return 1; //argument error
            }

            if (project.StartedRunning && !project.FinishedRunning)
            {
                Logger.Error("Cannot delete project " + options.ProjectName + " that is currently running");
                return 1; //argument error
            }

            cloud.MasterQueue.Enqueue(new DeleteProjectMessage(options.ProjectName));

            if (!options.NoWait)
            {
                Logger.Info("waiting for project to be deleted");
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    if (sw.ElapsedMilliseconds > MAX_WAIT_MS)
                    {
                        Logger.Error("project not deleted in " + MAX_WAIT_MS + "ms");
                        return 2; //internal error
                    }
                    Thread.Sleep(SLEEP_MS);
                    project = TilingProject.Find(DynamoContext, options.ProjectName);
                }
                while (project != null);
                Logger.Info("project has been deleted");
            }

            return 0;
        }
    }
}
