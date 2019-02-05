using CommandLine;
using log4net;
using OPS.Util;
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

    public class ListProjects : CloudPipeline
    {
        private ListProjectsOptions options;

        public ListProjects(ListProjectsOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var projects = TilingProject.FindAll(this);
            var projectNames = projects.Select(project => project.Name).ToList();
            Console.WriteLine(JsonHelper.ToJson(projectNames, indent: true, autoTypes: false));
            return 0;
        }
    }
}
