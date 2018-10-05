using CommandLine;
using log4net;
using OPS.Geometry;
using OPS.Plumbing;
using OPS.Pipeline.MeshWorker;

using System;
using System.Threading.Tasks;
using System.Threading;

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

            var cloud = new TileServerCloud(this);
            cloud.EnsureTablesExist();

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
                        while (true)
                        {   
                            try
                            {
                                RunWorker();
                            }
                            catch (Exception e)
                            {
                                logger.Error("error in worker task " + i + ": " + e.Message);
                                logger.Error(e.StackTrace);
                                // Introduce a sleep here to limit debug spew just in case a misconfiguration is causing this error
                                Thread.Sleep(2000);
                            }
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

            //each worker thread has its own cloud instance
            //this avoids the need for synchronization of access to the message queues
            var cloud = new TileServerCloud(this);

            while (true)
            {
                var messages = cloud.WorkerQueue.Dequeue();
                foreach(var m in messages)
                {
                    try
                    {
                        // process
                        if (m.GetType() == typeof(DefineTilesMessage))
                        {
                            new DefineTiles((DefineTilesMessage)m, this, cloud).Process();
                        }
                        else if (m.GetType() == typeof(ChunkInputMessage))
                        {
                            new ChunkInput((ChunkInputMessage)m, this, cloud).Process();
                        }
                        else if (m.GetType() == typeof(BuildBakedLeavesMessage))
                        {
                            new BuildBakedLeaves((BuildBakedLeavesMessage)m, this, cloud).Process();
                        }
                        else if (m.GetType() == typeof(BuildBackprojectLeavesMessage))
                        {
                            new BuildBackprojectLeaves((BuildBackprojectLeavesMessage)m, this, cloud).Process();
                        }
                        else if (m.GetType() == typeof(BuildParentMessage))
                        {
                            new BuildParent((BuildParentMessage)m, this, cloud).Process();
                        }
                        else if (m.GetType() == typeof(BuildTilesetJsonMessage))
                        {
                            new BuildTilesetJson((BuildTilesetJsonMessage)m, this, cloud).Process();
                        }
                        else if (m.GetType() == typeof(BuildTilingInputMessage))
                        {
                            new BuildTilingInput((BuildTilingInputMessage)m, this, cloud).Process();
                        }
                        else
                        {
                            logger.Info("Unknown message type: " + m.GetType());
                        }
                        cloud.WorkerQueue.Delete(m);
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
