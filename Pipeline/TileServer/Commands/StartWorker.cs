using CommandLine;
using log4net;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{
    
    [Verb("startworker", HelpText = "Starts a worker to process tiling messages")]
    public class StartWorkerOptions
    {
        [Value(0, Required = true, HelpText = "Dynamo DB prefix")]
        public string DynamoDBPrefix { get; set; }

        [Value(1, Required = true, HelpText = "Project Name")]
        public string ProjectName { get; set; }

        [Option(HelpText = "AWS profile to use", Default = "default")]
        public string Profile { get; set; }
    }

    public class StartWorker : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(RunProject));

        StartWorkerOptions options;
        public StartWorker(StartWorkerOptions options) : base(dynamoPrefix: options.DynamoDBPrefix, profile: options.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            var queue = new TilingQueue(options.DynamoDBPrefix, options.Profile);
            new TileServerCloud(options.DynamoDBPrefix, this).EnsureTablesExist();

            while (true)
            {
                logger.Info("Looking for message");
                var m = queue.Deque();
                if(m != null)
                {
                    logger.Info("Message found");
                    // TODO: start a process that updates timeout ever n seconds
                    try
                    {
                        // process
                        if (m.GetType() == typeof(DefineTilesMessage))
                        {
                            new DefineTiles((DefineTilesMessage)m, this).Process();
                        }
                        else if (m.GetType() == typeof(ChunkInputMessage))
                        {
                            new ChunkInput((ChunkInputMessage)m, this).Process();
                        }
                        else if (m.GetType() == typeof(BuildLeavesMessage))
                        {
                            new BuildLeaves((BuildLeavesMessage)m, this).Process();
                        }
                        else
                        {
                            logger.Info("Unknown message type");                            
                        }

                    }
                    catch (Exception e)
                    {
                        logger.Error(e.Message);
                    }

                    // TODO: end process that updates timeout
                    queue.Delete(m);
                }
            }

            return 0;
        }
    }
}
