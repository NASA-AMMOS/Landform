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
    public class ListProjectsOptions : PipelineCoreOptions
    {       
    }

    public class ListProjects : PipelineCore
    {
        private ListProjectsOptions options;

        public ListProjects(ListProjectsOptions options)
            : base(options, TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile )
        {
            this.options = options;
        }

        public int Run()
        {
            var cloud = new TileServerCloud(this, quiet: true); //ensures queues and tables exist

            var projects = TilingProject.FindAll(DynamoContext);
            var projectNames = projects.Select(project => project.Name).ToList();

            Console.WriteLine(JsonHelper.ToJson(projectNames, indent: true, autoTypes: false));

            return 0;
        }
    }
}
