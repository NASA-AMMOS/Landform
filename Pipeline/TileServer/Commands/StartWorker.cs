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
        static ILog logger = LogManager.GetLogger(typeof(StartWorker));

        StartWorkerOptions options;


        public TilingQueue WorkerQueue
        {
            get {  return TileServerCloud.WorkerQueue(options.DynamoDBPrefix, options.Profile); }
        }

        public TilingQueue CompeltionQueue(TilingProject project)
        {
            return TileServerCloud.CompletionQueue(options.DynamoDBPrefix, options.Profile, project);            
        }



        public StartWorker(StartWorkerOptions options) : base(dynamoPrefix: options.DynamoDBPrefix, profile: options.Profile)
        {
            this.options = options;
        }

        public int Run()
        {            
            new TileServerCloud(options.DynamoDBPrefix, this).EnsureTablesExist();

            Task[] tasks = new Task[Environment.ProcessorCount];
            for(int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() => RunWorker());
            }
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i].Wait();
            }
            return 0;
        }

        public void RunWorker()
        {
            logger.Info("Worker starting");
            var queue = WorkerQueue;
            while (true)
            {
                var m = queue.Deque();
                if (m != null)
                {
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
                        else if (m.GetType() == typeof(BuildParentsMessage))
                        {
                            new BuildParents((BuildParentsMessage)m, this).Process();
                        }
                        else
                        {
                            logger.Info("Unknown message type");
                        }

                    }
                    catch (Exception e)
                    {
                        logger.Error(e.Message);
                        logger.Error(e.StackTrace);
                    }

                    // TODO: end process that updates timeout
                    queue.Delete(m);
                }
            }
        }
    }
}
