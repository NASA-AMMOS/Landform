using CommandLine;
using log4net;
using OPS.Geometry;
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

        [Option(HelpText = "AWS profile to use", Default = "default")]
        public string Profile { get; set; }
        
        [Option(HelpText = "Also start the master server as part of this process - useful for debugging", Default = false)]
        public bool StartMaster { get; set; }

        [Option(HelpText = "Run a single worker on the main thread for debugging", Default = false)]
        public bool SingleThreaded { get; set; }
    }

    public class StartWorker : PipelineCore
    {
        static ILog logger = LogManager.GetLogger(typeof(StartWorker));

        StartWorkerOptions options;


        public TilingQueue WorkerQueue
        {
            get { return new TileServerCloud(options.DynamoDBPrefix, this).WorkerQueue; }
        }

        public TilingQueue CompeltionQueue
        {
            get { return new TileServerCloud(options.DynamoDBPrefix, this).CompletionQueue; }            
        }
        

        public StartWorker(StartWorkerOptions options) : base(dynamoPrefix: options.DynamoDBPrefix, profile: options.Profile)
        {
            this.options = options;
        }

        public int Run()
        {
            // Register filetype handlers
            new OpenInventorSerializer().Register();
            new DracoSerializer().Register();
            //Configure gdal
            GdalConfiguration.ConfigureGdal();

            new TileServerCloud(options.DynamoDBPrefix, this).EnsureTablesExist();


            Task masterTask = null;
            if(options.StartMaster)
            {
                masterTask = new Task(() =>
                {
                    StartMasterOptions opts = new StartMasterOptions()
                    {
                        DynamoDBPrefix= options.DynamoDBPrefix,
                        Profile= options.Profile
                    };
                    new StartMaster(opts).Run();
                });
                masterTask.Start();
            }

            if (options.SingleThreaded)
            {
                RunWorker();
            }
            else
            {
                Task[] tasks = new Task[Environment.ProcessorCount];
                for (int i = 0; i < tasks.Length; i++)
                {
                    tasks[i] = Task.Run(() => RunWorker());
                }
                for (int i = 0; i < tasks.Length; i++)
                {
                    tasks[i].Wait();
                }         
            }
            if (masterTask != null)
            {
                masterTask.Wait();
            }
            return 0;
        }

        public void RunWorker()
        {
            logger.Info("Worker starting");
            var queue = WorkerQueue;
            while (true)
            {
                var messages = queue.Deque();
                foreach(var m in messages)
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
                        else if (m.GetType() == typeof(BuildTilesetJsonMessage))
                        {
                            new BuildTilesetJson((BuildTilesetJsonMessage)m, this).Process();
                        }
                        else
                        {
                            logger.Info("Unknown message type: " + m.GetType());
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
