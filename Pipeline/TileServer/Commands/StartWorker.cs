using CommandLine;
using log4net;
using OPS.Util;
using OPS.Geometry;
using OPS.Plumbing;
using OPS.Pipeline.MeshWorker;

using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
        public const int MAX_PROCESSING_SEC = 15 * 60;

        //indexed by message handle
        private class MessageInfo
        {
            public int deadline; //seconds since epoch
            public int numExceptions;
        }
        private ConcurrentDictionary<string, MessageInfo> messagesInFlight =
            new ConcurrentDictionary<string, MessageInfo>();

        private static ILog logger = LogManager.GetLogger(typeof(StartWorker));

        private StartWorkerOptions options;

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

                //implement the heartbeat to progressively update the visibility timeout for messages "in flight"
                //i.e. currently being processed by one of the workers
                //while a message is in its visiblity timeout it is still in the SQS queue but hidden from other workers
                //this scheme helps avoid multiple workers from trying to process the same message
                //https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-visibility-timeout.html
                //however note that unless we use FIFO queues (which we currently do not)
                //then it's possible that messages will be received more than once
                //FIFO queues impose a limit on the max transactions per second
                //and also aren't available in us-west-1 region as of this writing
                //(and they're a little more expensive)
                var workerQueue = new TileServerCloud(this).WorkerQueue;
                while (true)
                {
                    //in the current implementation the worker threads should never exit
                    //but in case they all do for some reason then we shouldn't hang around either
                    if (tasks.All(t => t.IsCompleted))
                    {
                        break;
                    }

                    Thread.Sleep(1000 * TilingQueue.VISIBILITY_TIMEOUT_SEC / 2);

                    foreach (var entry in messagesInFlight)
                    {
                        var receiptHandle = entry.Key;
                        var info = entry.Value;
                        if (CurrentTimeSec() < info.deadline)
                        {
                            try
                            {
                                workerQueue.UpdateTimeout(receiptHandle, TilingQueue.VISIBILITY_TIMEOUT_SEC);
                            }
                            catch (Exception e)
                            {
                                //ignore first exception, the message was prob just deleted after we started iterating
                                //there does not seem to be a much better way to do this
                                info.numExceptions++;
                                if (info.numExceptions >= 2)
                                {
                                    logger.Error("error updating message timeout: " + e.Message);
                                }
                            }
                        }
                        else
                        {
                            logger.Error("max processing time " + MAX_PROCESSING_SEC + "s reached for message, " +
                                         "stopping heartbeat");
                            ((IDictionary)messagesInFlight).Remove(receiptHandle);
                        }
                    }
                }
            }

            if (masterTask != null)
            {
                masterTask.Wait();
            }

            return 0;
        }

        private static int CurrentTimeSec()
        {
            return (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        }

        private void StartedProcessing(TilingQueue queue, TilingQueueMessage m)
        {
            if (!options.SingleThreaded)
            {
                var info = new MessageInfo() { deadline = CurrentTimeSec() + MAX_PROCESSING_SEC };
                messagesInFlight.TryAdd(m.ReceiptHandle, info);
            }
            else
            {
                queue.UpdateTimeout(m, MAX_PROCESSING_SEC);
            }
        }

        private void FinishedProcessing(TilingQueueMessage m)
        {
            if (!options.SingleThreaded)
            {
                ((IDictionary)messagesInFlight).Remove(m.ReceiptHandle);
            }
        }

        public void RunWorker()
        {
            logger.Info("Worker starting");

            //each worker thread has its own cloud instance
            //this avoids the need for synchronization
            var pipeline = new PipelineCore(dynamoPrefix: TileServerConfig.Instance.VenueName,
                                            profile: TileServerConfig.Instance.Profile);
            var cloud = new TileServerCloud(pipeline);

            var dispatcher = new TypeDispatcher()
                .Case((DefineTilesMessage m) => new DefineTiles(m, pipeline, cloud).Process())
                .Case((ChunkInputMessage m) => new ChunkInput(m, pipeline, cloud).Process())
                .Case((BuildBakedLeavesMessage m) => new BuildBakedLeaves(m, pipeline, cloud).Process())
                .Case((BuildBackprojectLeavesMessage m) => new BuildBackprojectLeaves(m, pipeline, cloud).Process())
                .Case((BuildParentMessage m) => new BuildParent(m, pipeline, cloud).Process())
                .Case((BuildTilesetJsonMessage m) => new BuildTilesetJson(m, pipeline, cloud).Process())
                .Case((BuildTilingInputMessage m) => new BuildTilingInput(m, pipeline, cloud).Process());
            dispatcher.Unhandled = (t, x) => logger.Error("Unknown message type: " + t);

            while (true)
            {
                foreach (var m in cloud.WorkerQueue.Dequeue())
                {
                    try
                    {
                        StartedProcessing(cloud.WorkerQueue, m);
                        dispatcher.Handle(m);
                        FinishedProcessing(m);
                        cloud.WorkerQueue.DeleteMessage(m);
                    }
                    catch (Exception e)
                    {
                        FinishedProcessing(m);
                        logger.Error(e.Message);
                        logger.Error(e.StackTrace);
                    }
                }
            }
        }
    }
}
