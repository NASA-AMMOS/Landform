using CommandLine;
using log4net;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
            var queue = new TilingQueue(options.DynamoDBPrefix, options.Profile);
            logger.Info("Define tiles");
            queue.Enqueue(new DefineTilesMessage(options.ProjectName));
            WaitForTilesToBeDefined();
            logger.Info("Chunk inputs");
            var p = TilingProject.Find(this.DynamoContext, options.ProjectName);
            var inputs = TilingInput.Find(this.DynamoContext, p);
            foreach(var input in inputs)
            {
                queue.Enqueue(new ChunkInputMessage(options.ProjectName, input.Name));
            }


            return 0;
        }


        void WaitForTilesToBeDefined()
        {
            while(true)
            {
                var p = TilingProject.Find(this.DynamoContext, options.ProjectName);
                if(!p.TilesDefined)
                {
                    Thread.Sleep(1000);
                }
                else
                {
                    break;
                }
            }
        }

        //void WaitForInputsToChunk()
        //{
        //    while (true)
        //    {
        //        var p = TilingProject.Find(this.DynamoContext, options.ProjectName);
                
        //        if (!p.TilesDefined)
        //        {
        //            Thread.Sleep(1000);
        //        }
        //        else
        //        {
        //            break;
        //        }
        //    }
        //}
    }


}
