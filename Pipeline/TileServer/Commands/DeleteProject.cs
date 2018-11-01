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
            var cloud = new TileServerCloud(this, quiet: true);

            var project = TilingProject.Find(DynamoContext, options.ProjectName);

            if (project == null)
            {
                Logger.ErrorFormat("project \"{0}\" not found", options.ProjectName);
                return 1; //argument error
            }

            if (project.StartedRunning && !project.FinishedRunning)
            {
                Logger.ErrorFormat("cannot delete project \"{0}\", project currently running",options.ProjectName);
                return 1; //argument error
            }

            cloud.MasterQueue.Enqueue(new DeleteProjectMessage(options.ProjectName));

            if (!options.NoWait)
            {
                Logger.InfoFormat("waiting for project \"{0}\" to be deleted", options.ProjectName);
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    if (sw.ElapsedMilliseconds > MAX_WAIT_MS)
                    {
                        Logger.ErrorFormat("project \"{0}\" not deleted in {1}ms", options.ProjectName, MAX_WAIT_MS);
                        return 2; //internal error
                    }
                    Thread.Sleep(SLEEP_MS);
                    project = TilingProject.Find(DynamoContext, options.ProjectName);
                }
                while (project != null);
                Logger.InfoFormat("project \"{0}\" has been deleted", options.ProjectName);
            }

            return 0;
        }
    }
}
