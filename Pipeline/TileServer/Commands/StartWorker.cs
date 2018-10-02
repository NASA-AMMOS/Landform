using CommandLine;
using log4net;
using OPS.Geometry;
using OPS.Plumbing;
using OPS.Pipeline.MeshWorker;

using System;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{
    
    [Verb("startworker", HelpText = "Starts a worker to process tiling messages")]
    public class StartWorkerOptions
    {
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
            get { return new TileServerCloud(this).WorkerQueue; }
        }

        public TilingQueue CompletionQueue
        {
            get { return new TileServerCloud(this).CompletionQueue; }
        }
        

        public StartWorker(StartWorkerOptions options)
            : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
        {
            this.options = options;

            //MSL specific: this project does not hold its images within the same s3 bucket as the project
            //future projects  are expected to be within the same bucket
            var config = TileServerConfig.Instance;
            if (!string.IsNullOrEmpty(config.MSLICEProfile) && !string.IsNullOrEmpty(config.MSLICES3Url) &&
                OPS.Cloud.Credentials.Exists(config.MSLICEProfile))
            {
                this.AddProfile(config.MSLICES3Url, config.MSLICEProfile);
            }
        }

        public int Run()
        {
            TileServerConfig.Instance.Dump(logger);

            // Register filetype handlers
            new OpenInventorSerializer().Register();
            new DracoSerializer().Register();
            //Configure gdal
            GdalConfiguration.ConfigureGdal();

            new TileServerCloud(this).EnsureTablesExist();

            Task masterTask = null;
            if (options.StartMaster)
            {
                masterTask = new Task(() =>
                {
                    try
                    {
                        StartMasterOptions opts = new StartMasterOptions();
                        new StartMaster(opts).Run();
                    }
                    catch (Exception e)
                    {
                        logger.Error("error in master task: " + e.Message);
                        logger.Error(e.StackTrace);
                    }
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
                    tasks[i] = Task.Run(() => {
                            try
                            {
                                RunWorker();
                            }
                            catch (Exception e)
                            {
                                logger.Error("error in worker task " + i + ": " + e.Message);
                                logger.Error(e.StackTrace);
                            }
                        });
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
                        else if (m.GetType() == typeof(BuildBakedLeavesMessage))
                        {
                            new BuildBakedLeaves((BuildBakedLeavesMessage)m, this).Process();
                        }
                        else if (m.GetType() == typeof(BuildBackprojectLeavesMessage))
                        {
                            new BuildBackprojectLeaves((BuildBackprojectLeavesMessage)m, this).Process();
                        }
                        else if (m.GetType() == typeof(BuildParentMessage))
                        {
                            new BuildParent((BuildParentMessage)m, this).Process();
                        }
                        else if (m.GetType() == typeof(BuildTilesetJsonMessage))
                        {
                            new BuildTilesetJson((BuildTilesetJsonMessage)m, this).Process();
                        }
                        else if (m.GetType() == typeof(BuildTilingInputMessage))
                        {
                            new BuildTilingInput((BuildTilingInputMessage)m, this).Process();
                        }
                        else
                        {
                            logger.Info("Unknown message type: " + m.GetType());
                        }
                        queue.Delete(m);
                    }
                    catch (Exception e)
                    {
                        logger.Error(e.Message);
                        logger.Error(e.StackTrace);
                    }
                }
            }
        }
    }
}
