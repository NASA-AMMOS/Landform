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
        [Value(0, Required = true, HelpText = "Dynamo DB prefix")]
        public string DynamoDBPrefix { get; set; }

        [Value(1, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }

        [Option(HelpText = "AWS profile to use", Default = "default")]
        public string Profile { get; set; }
    }

    public class RunProject : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(RunProject));

        RunProjectOptions options;
        public RunProject(RunProjectOptions options) : base(dynamoPrefix: options.DynamoDBPrefix, profile: options.Profile)
        {
            this.options = options;
        }
        
        public int Run()
        {
            var workerQueue = new TileServerCloud(options.DynamoDBPrefix, this).WorkerQueue;
            var project = TilingProject.Find(this.DynamoContext, options.ProjectName);
            logger.Info("Define tiles");
            workerQueue.Enqueue(new DefineTilesMessage(options.ProjectName));
            return 0;
        }
    }
}
