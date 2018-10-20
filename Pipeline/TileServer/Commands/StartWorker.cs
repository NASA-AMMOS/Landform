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
using System.Diagnostics;

namespace OPS.Pipeline.TileServer
{
    
    [Verb("startworker", HelpText = "Starts a worker to process tiling messages")]
    public class StartWorkerOptions : PipelineCoreOptions
    {
        [Option(Default = false, HelpText = "Also start the master server (e.g. for debugging)")]
        public bool StartMaster { get; set; }

        [Option(Default = false, HelpText = "Run a single worker on the main thread (e.g. for debugging)")]
        public bool SingleThreaded { get; set; }
    }

    public class StartWorker : PipelineCore
    {
        public const int MAX_PROCESSING_SEC = 6 * 60 * 60; //6h

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
            : base(options, TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile)
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
            TileServerConfig.Instance.Dump(Logger);

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
                        Logger.ErrorFormat("error in master task ({0}): {1}", e.GetType().FullName, e.Message);
                        Logger.Error(e.StackTrace);
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
                                Logger.ErrorFormat("error in worker task {0} ({1}): {2}",
                                                   i, e.GetType().FullName, e.Message);
                                Logger.Error(e.StackTrace);
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
                                    Logger.Error("error updating message timeout: " + e.Message);
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
            Logger.Info("Worker starting");

            //each worker thread has its own cloud instance
            //this avoids the need for synchronization
            var pipeline = new PipelineCore(options,
                                            TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile,
                                            logger: Logger); //all threads share the same logger which is MT safe
            var cloud = new TileServerCloud(pipeline);
            var workerQueue = cloud.WorkerQueue;

            var dispatcher = new TypeDispatcher()
                .Case((DefineTilesMessage m) => new DefineTiles(m, pipeline, cloud).Process())
                .Case((ChunkInputMessage m) => new ChunkInput(m, pipeline, cloud).Process())
                .Case((BuildBakedLeavesMessage m) => new BuildBakedLeaves(m, pipeline, cloud).Process())
                .Case((BuildBackprojectLeavesMessage m) => new BuildBackprojectLeaves(m, pipeline, cloud).Process())
                .Case((BuildParentMessage m) => new BuildParent(m, pipeline, cloud).Process())
                .Case((BuildTilesetJsonMessage m) => new BuildTilesetJson(m, pipeline, cloud).Process())
                .Case((BuildTilingInputMessage m) => new BuildTilingInput(m, pipeline, cloud).Process());
            dispatcher.Unhandled = (t, x) => Logger.Error("Unknown message type: " + t);

            while (true)
            {
                foreach (var m in workerQueue.Dequeue())
                {
                    Stopwatch sw = new Stopwatch();
                    sw.Start();
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
                        Logger.ErrorFormat("{0}: processing error ({1}): {2}",
                                           m.Info(), e.GetType().FullName, e.Message);
                        Logger.Error(e.StackTrace);
                    }
                    double totalSec = 0.001 * sw.ElapsedMilliseconds;
                    if (totalSec > MAX_PROCESSING_SEC)
                    {
                        Logger.ErrorFormat("{0}: took {1:F3}s, but max processing time is {2:F3}s",
                                           m.Info(), totalSec, MAX_PROCESSING_SEC);
                    }
                }
            }
        }
    }
}
