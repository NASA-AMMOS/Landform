using CommandLine;
using log4net;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{
    [Verb("deleteproject", HelpText = "Delete project")]
    public class DeleteProjectOptions
    {       
        [Value(0, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }
    }

    public class DeleteProject : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(DeleteProject));

        DeleteProjectOptions options;

        public DeleteProject(DeleteProjectOptions options)
            : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            new TileServerCloud(this).EnsureTablesExist();

            var project = TilingProject.Find(DynamoContext, options.ProjectName);

            if (project == null)
            {
                logger.Error("No project by that name found: " + options.ProjectName);
                return 1;
            }

            if (project.StartedRunning && !project.FinishedRunning)
            {
                logger.Error("Cannot delete project that is currently running");
                return 1;
            }

            project.Delete(this, DynamoContext, true, logger);

            return 0;
        }
    }
}
