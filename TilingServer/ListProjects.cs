using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Pipeline;
using OPS.Pipeline.TilingServer;

namespace OPS.TilingServer
{
    [Verb("listprojects", HelpText = "List projects")]
    public class ListProjectsOptions : PipelineCoreOptions
    {
        [Option(Default = false, HelpText = "run locally, do not connect to cloud")]
        public bool Local { get; set; }
    }
        
    public class ListProjects : TilingCommand
    {
        private ListProjectsOptions options;

        public ListProjects(ListProjectsOptions options) : base(options, options.Local)
        {
            this.options = options;
        }

        public int Run()
        {
            var projects = TilingProject.FindAll(pipeline);
            var projectNames = projects.Select(project => project.Name).ToList();
            Console.WriteLine(JsonHelper.ToJson(projectNames, indent: true, autoTypes: false));
            return 0;
        }
    }
}
