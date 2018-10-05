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
    public class DeleteProjectOptions
    {       
        [Value(0, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }

        [Option(HelpText = "Wait until input has been uploaded to project", Default = true)]
        public bool Wait { get; set; }
    }

    public class DeleteProject : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(DeleteProject));

        const int MAX_WAIT_MS = 60 * 1000;
        const int SLEEP_MS = 500;

        DeleteProjectOptions options;

        public DeleteProject(DeleteProjectOptions options)
            : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            var cloud = new TileServerCloud(this);
            cloud.EnsureTablesExist();

            var project = TilingProject.Find(DynamoContext, options.ProjectName);

            if (project == null)
            {
                logger.Error("No project by that name found: " + options.ProjectName);
                return 1; //argument error
            }

            if (project.StartedRunning && !project.FinishedRunning)
            {
                logger.Error("Cannot delete project " + options.ProjectName + " that is currently running");
                return 1; //argument error
            }

            cloud.MasterQueue.Enqueue(new DeleteProjectMessage(options.ProjectName));

            if (options.Wait)
            {
                logger.Info("waiting for project to be deleted");
                var sw = new Stopwatch();
                sw.Start();
                do
                {
                    if (sw.ElapsedMilliseconds > MAX_WAIT_MS)
                    {
                        logger.Error("project not deleted in " + MAX_WAIT_MS + "ms");
                        return 2; //internal error
                    }
                    Thread.Sleep(SLEEP_MS);
                    project = TilingProject.Find(DynamoContext, options.ProjectName);
                }
                while (project != null);
                logger.Info("project has been deleted");
            }

            return 0;
        }
    }
}
