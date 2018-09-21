using CommandLine;
using log4net;
using OPS.Geometry;
using OPS.Plumbing;
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

    public class RunProjectOptions
    {
        [Value(0, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }
    }

    public class RunProject : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(RunProject));

        RunProjectOptions options;

        public RunProject(RunProjectOptions options) : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
        {
            this.options = options;
        }
        
        public int Run()
        {
            var completionQueue = new TileServerCloud(this).CompletionQueue;
            var project = TilingProject.Find(this.DynamoContext, options.ProjectName);
            logger.Info("Run project");
            completionQueue.Enqueue(new RunProjectMessage(options.ProjectName));
            return 0;
        }
    }
}
