using CommandLine;
using log4net;
using OPS.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OPS.Pipeline;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline.TileServer
{
    [Verb("runproject", HelpText = "Runs a tiling workflow")]
    public class RunProjectOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }
    }

    public class RunProject : CloudPipeline
    {
        private RunProjectOptions options;

        public RunProject(RunProjectOptions options) : base(options, queuePrefix: "tiling")
        {
            this.options = options;
        }
        
        public int Run()
        {
            var project = TilingProject.Find(this, options.ProjectName);
            if (project == null)
            {
                LogError("project \"{0}\" not found", options.ProjectName);
                return 1; //argument error
            }

            LogInfo("running project \"{0}\"", options.ProjectName);
            EnqueueToMaster(new RunProjectMessage(options.ProjectName));

            return 0;
        }
    }
}
