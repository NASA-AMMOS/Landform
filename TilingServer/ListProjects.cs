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
        
    public class ListProjects
    {
        private ListProjectsOptions options;
        private PipelineCore pipeline;

        public ListProjects(ListProjectsOptions options)
        {
            this.options = options;

            if (options.Local)
            {
                pipeline = new LocalPipeline(options);
            }
            else
            {
                pipeline = new CloudPipeline(options, queuePrefix: "tiling");
            }
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
