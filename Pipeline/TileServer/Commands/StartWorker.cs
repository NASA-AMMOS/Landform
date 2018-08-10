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
        

        public StartWorker(StartWorkerOptions options) : base(dynamoPrefix: TileServerConfig.Instance.VenueName, profile: TileServerConfig.Instance.Profile)
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

            new TileServerCloud(this).EnsureTablesExist();


            Task masterTask = null;
            if(options.StartMaster)
            {
                masterTask = new Task(() =>
                {
                    StartMasterOptions opts = new StartMasterOptions();
                    opts.SingleThreaded = true;
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

                    queue.Delete(m);
                }
            }
        }
    }
}
