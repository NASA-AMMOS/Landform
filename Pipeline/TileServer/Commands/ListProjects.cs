using CommandLine;
using log4net;
using OPS.Util;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{
    [Verb("listprojects", HelpText = "List projects")]
    public class ListProjectsOptions
    {       
        [Option(Required = false, Default = false, HelpText = "suppress non-essential output")]
        public bool Quiet { get; set; }
    }

    public class ListProjects : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(ListProjects));

        ListProjectsOptions options;

        public ListProjects(ListProjectsOptions options)
            : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            //https://stackoverflow.com/questions/4094032/how-to-switch-on-off-logging-using-log4net
            if (options.Quiet)
            {
                LogManager.GetRepository().ResetConfiguration();
            }

            new TileServerCloud(this).EnsureTablesExist();

            var projects = TilingProject.FindAll(DynamoContext);
            var projectNames = projects.Select(project => project.Name).ToList();

            Console.WriteLine(JsonHelper.ToJson(projectNames, indent: true, autoTypes: false));

            return 0;
        }
    }
}
