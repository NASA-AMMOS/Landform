using CommandLine;
using log4net;
using OPS.Util;
using OPS.Geometry;
using OPS.Plumbing;
using OPS.Pipeline.MeshWorker;
using OPS.Pipeline.AlignmentServer;
using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using Amazon.SQS;

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
        public const double HEARTBEAT_PERIOD_REL = 0.333;

        private class MessageRec
        {
            public double StartSec { get; private set; }
            public string Info { get; private set; }

            public volatile string ReceiptHandle;
            public volatile bool Done;
            public int NumHeartbeats;
            public int NumErrors;
            public double ApproxLastReceiveSec;

            public MessageRec(TilingQueueMessage m)
            {
                StartSec = UTCTime.Now();
                ReceiptHandle = m.ReceiptHandle;
                Info = m.Info();
                ApproxLastReceiveSec = 0.001 * m.ApproxLastReceiveMS;
            }
        }

        //indexed by message ID
        private Dictionary<string, MessageRec> messagesInFlight = new Dictionary<string, MessageRec>();

        private StartWorkerOptions options;

        public StartWorker(StartWorkerOptions options)
            : base(options, TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile)
        {
            this.options = options;
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
                int numWorkers = Environment.ProcessorCount;
                Logger.InfoFormat("starting {0} workers", numWorkers);
                Task[] tasks = new Task[numWorkers];
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
                                Logger.ErrorFormat("error in worker task ({0}): {1}", e.GetType().FullName, e.Message);
                                Logger.Error(e.StackTrace);
                                // limit debug spew just in case a misconfiguration is causing this error
                                Thread.Sleep(2000);
                            }
                        }
                    });
                }

                //heartbeat to progressively update the visibility timeout for messages in flight
                //i.e. currently being processed by one of the workers
                //while a message is in its visiblity timeout it is still in the SQS queue but hidden from other workers
                //this scheme helps avoid multiple workers from trying to process the same message
                //https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-visibility-timeout.html
                //however note that unless we use FIFO queues (which we currently do not)
                //then it's possible that messages will be received more than once
                //FIFO queues impose a limit on the max transactions per second
                //and also aren't available in us-west-1 region as of this writing
                //(and they're a little more expensive)
                var workerQueue = cloud.WorkerQueue;
                double lastHeartbeat = -1;
                double heartbeatPeriod = HEARTBEAT_PERIOD_REL * workerQueue.TimeoutSec;
                while (true)
                {
                    //in the current implementation the worker threads should never exit
                    //but in case they all do for some reason then we shouldn't hang around either
                    if (tasks.All(t => t.IsCompleted))
                    {
                        break;
                    }

                    if (lastHeartbeat >= 0)
                    {
                        //try to maintain heartbeat period proportional to queue timout
                        double period = UTCTime.Now() - lastHeartbeat;
                        int sleepMS = (int)(1000 * (heartbeatPeriod - period));
                        if (sleepMS > 0)
                        {
                            Thread.Sleep(sleepMS);
                        }
                    }

                    double start = UTCTime.Now();

                    List<KeyValuePair<string, MessageRec>> inFlight = null;
                    lock (messagesInFlight)
                    {
                        inFlight = messagesInFlight.ToList();
                    }

                    if (inFlight.Count > 0)
                    {
                        Logger.InfoFormat("{0} messages in flight, heartbeat period {1:F3}s",
                                          inFlight.Count, start - lastHeartbeat);
                    }

                    var dead = new List<string>();
                    foreach (var entry in inFlight)
                    {
                        var messageHandle = entry.Key;
                        var rec = entry.Value;
                        var totalSec = UTCTime.Now() - rec.StartSec;
                        if (totalSec > MAX_PROCESSING_SEC)
                        {
                            Logger.ErrorFormat("{0} {1:F3}s processing > max {2:F3}s, stopping heartbeat",
                                               rec.Info, totalSec, MAX_PROCESSING_SEC);
                            dead.Add(messageHandle);
                        }
                        else
                        {
                            try
                            {
                                /* int nh =*/ Interlocked.Increment(ref rec.NumHeartbeats);
                                workerQueue.UpdateTimeout(rec.ReceiptHandle, workerQueue.TimeoutSec);
                            }
                            catch (Exception /*e*/)
                            {
                                if (!rec.Done)
                                {
                                    //sometimes we get errors
                                    //
                                    //"Message does not exist or is not available for visibility timeout change" 
                                    //
                                    //one way this can happen is if there is another worker process in the same venue
                                    //e.g. running on another EC2 instance
                                    //and the same message was multiply received by both of us, but either
                                    //(a) they received the mssage later than we did
                                    //    and AWS is only allowing the latest receive handle to be used, or
                                    //(b) they already finished processing and deleted the message
                                    //
                                    //however these errors might occur even if we are the only worker process in venue
                                    //e.g. if we multiply received the message,
                                    //our receive time estimates are too inaccurate,
                                    //and we aren't using the latest receive handle

                                    //commented out code here could be useful for future debugging
                                    /* int ne =*/ Interlocked.Increment(ref rec.NumErrors);
                                    //Logger.ErrorFormat("{0}: in flight for {1:F3}s, max {2:F3}s, " +
                                    //                   "latest receive time {3:F3}, " +
                                    //                   "error {4}/{5} updating timeout ({6}): {7}{8}",
                                    //                   rec.Info, totalSec, MAX_PROCESSING_SEC,
                                    //                   rec.ApproxLastReceiveSec, ne, nh,
                                    //                   e.GetType().FullName,
                                    //                   e is AmazonSQSException ?
                                    //                   (e as AmazonSQSException).ErrorCode + " ": "",
                                    //                   e.Message);
                                }
                                //if rec.Done the message was deleted after we started this heartbeat iteration
                            }
                        }
                    }

                    lock (messagesInFlight)
                    {
                        foreach (var mh in dead)
                        {
                            messagesInFlight.Remove(mh);
                        }
                    }

                    if (lastHeartbeat >= 0)
                    {
                        //upper bound on time between visibility update of any in-flight message:
                        //end of this heartbeat - start of previous
                        double bound = UTCTime.Now() - lastHeartbeat;
                        if (bound > workerQueue.TimeoutSec)
                        {
                            Logger.ErrorFormat("heartbeat cycle took {0:F3}s, but msg visibility timeout is {1:F3}s",
                                               bound, workerQueue.TimeoutSec);
                        }
                    }

                    lastHeartbeat = start;
                }
            }

            if (masterTask != null)
            {
                masterTask.Wait();
            }

            return 0;
        }

        private bool StartedProcessing(TilingQueue queue, TilingQueueMessage m)
        {
            if (!options.SingleThreaded)
            {
                double existingStartSec = -1;
                double now = UTCTime.Now();
                double totalSec = -1;
                double rt = -1;
                int ne = 0;
                int nh = 0;
                lock (messagesInFlight)
                {
                    if (!messagesInFlight.ContainsKey(m.MessageId))
                    {
                        messagesInFlight.Add(m.MessageId, new MessageRec(m));
                    }
                    else
                    {
                        var rec = messagesInFlight[m.MessageId];
                        existingStartSec = rec.StartSec;
                        totalSec = now - existingStartSec;
                        ne = rec.NumErrors;
                        nh = rec.NumHeartbeats;
                        //use latest receipt handle for heartbeats and deletion
                        //https://stackoverflow.com/a/42000192
                        rt = 0.001 * m.ApproxLastReceiveMS;
                        if (rt >= rec.ApproxLastReceiveSec)
                        {
                            rec.ReceiptHandle = m.ReceiptHandle;
                            rec.ApproxLastReceiveSec = rt;
                        }
                    }
                }
                if (existingStartSec >= 0)
                {
                    //multiple message receipt *is* possible in SQS 
                    //https://aws.amazon.com/sqs/faqs
                    //unless using FIFO queues
                    //but FIFO queues are not available in us-west-1 as of this writing 
                    //
                    //we can safely detect and ignore multiple receipt here only for threads within this process
                    //it is possible that other worker processes exist in this venue, e.g. on other EC2 instances
                    //we cannot detect multiple recipt across such processes, so we need to just let that happen
                    Logger.WarnFormat("{0}: already started processing at {1:F3}, total {2:F3}s, " +
                                      "last receive time {3:F3}, ignoring multiple message receipt{4}",
                                      m.Info(), existingStartSec, totalSec, rt,
                                      ne > 0 ? string.Format(", {0}/{1} heartbeat errors", ne, nh) : "");

                    return false;
                }
            }
            else
            {
                queue.UpdateTimeout(m, MAX_PROCESSING_SEC);
            }
            Logger.InfoFormat("{0}: started processing at {1:F3}", m.Info(), UTCTime.Now());
            return true;
        }

        private MessageRec FinishedProcessing(TilingQueueMessage m)
        {
            MessageRec rec = null;
            double now = UTCTime.Now(), totalSec = -1;
            int ne = 0, nh = 0;
            if (!options.SingleThreaded)
            {
                lock (messagesInFlight)
                {
                    if (!messagesInFlight.TryGetValue(m.MessageId, out rec))
                    {
                        throw new Exception("message not found");
                    }
                    rec.Done = true; //in case heartbeat task is already iterating over a copy of messagesInFlight
                    totalSec = now - rec.StartSec;
                    ne = rec.NumErrors;
                    nh = rec.NumHeartbeats;
                    if (!messagesInFlight.Remove(m.MessageId))
                    {
                        throw new Exception("failed to remove message");
                    }
                }
            }
            Logger.InfoFormat("{0}: finished processing at {1:F3}{2}{3}",
                              m.Info(), now,
                              totalSec >= 0 ? string.Format(", total {0:F3}s", totalSec) : "",
                              ne > 0 ? string.Format(", {0}/{1} heartbeat errors", ne, nh) : "");
            return rec;
        }

        const int DEQUEUE_THROTTLE_MS = 500;
        const int WORKER_IMAGELRUCACHESIZE = 5;
        private void RunWorker()
        {
            //each worker thread has its own cloud instance
            //this avoids the need for synchronization
            var pipeline = new PipelineCore(options,
                                            TileServerConfig.Instance.VenueName, TileServerConfig.Instance.Profile,
                                            logger: Logger, numImagesLRUCache: WORKER_IMAGELRUCACHESIZE); //all threads share the same logger which is MT safe

            //MSL specific
            OPS.Pipeline.Rover.MSLCloud.AddMSLICEProfile(pipeline);

            var cloud = new TileServerCloud(pipeline, initQueues: true, initTables: false, quiet: true);
            var workerQueue = cloud.WorkerQueue;

            var dispatcher = new TypeDispatcher()
                .Case((DefineTilesMessage m) => new DefineTiles(m, pipeline, cloud).Process())
                .Case((ChunkInputMessage m) => new ChunkInput(m, pipeline, cloud).Process())
                .Case((BuildBakedLeavesMessage m) => new BuildBakedLeaves(m, pipeline, cloud).Process())
                .Case((BuildBackprojectLeavesMessage m) => new BuildBackprojectLeaves(m, pipeline, cloud).Process())
                .Case((BuildParentMessage m) => new BuildParent(m, pipeline, cloud).Process())
                .Case((BuildTilesetJsonMessage m) => new BuildTilesetJson(m, pipeline, cloud).Process())
                .Case((BuildTilingInputMessage m) => new BuildTilingInput(m, pipeline, cloud).Process())
                .Case((CreateMaskMessage m) => new CreateMask(m, pipeline, cloud).Process())
                .Case((DetectFeaturesMessage m) => new AlignmentServer.DetectFeatures(m, pipeline, cloud).Process())
                .Case((MatchImagesMessage m) => new MatchImages(m, pipeline, cloud).Process());
            dispatcher.Unhandled = (t, x) => Logger.Error("Unknown message type: " + t);

            while (true)
            {
                //only take one message at a time when we are ready to process it
                var m = workerQueue.DequeueOne();
                Stopwatch sw = new Stopwatch();
                sw.Start();
                if (m != null)
                {
                    try
                    {
                        if (StartedProcessing(cloud.WorkerQueue, m))
                        {
                            bool handled = false;
                            try
                            {
                                dispatcher.Handle(m);
                                handled = true;
                            }
                            catch (Exception e)
                            {
                                Logger.ErrorFormat("{0}: processing error ({1}): {2}",
                                                   m.Info(), e.GetType().FullName, e.Message);
                                Logger.Error(e.StackTrace);
                            }

                            //always try to remove from messagesInFlight if we started processing
                            //even if the handler failed
                            MessageRec rec = null;
                            try
                            {
                                rec = FinishedProcessing(m);
                            }
                            catch (Exception e)
                            {
                                Logger.ErrorFormat("{0}: error removing message from heartbeat table ({1}): {2}",
                                                   m.Info(), e.GetType().FullName, e.Message);
                                Logger.Error(e.StackTrace);
                            }

                            //always try to delete message from SQS if we successfully processed it
                            if (handled)
                            {
                                try
                                {
                                    //this will fail if the message has already been deleted
                                    //e.g. if another worker process got a multiple receipt of the message
                                    //and finished processing before we did (it stole the message)
                                    if (rec != null)
                                    {
                                        //use latest receipt handle
                                        //https://stackoverflow.com/a/42000192
                                        workerQueue.DeleteMessage(rec.ReceiptHandle);
                                    }
                                    else
                                    {
                                        workerQueue.DeleteMessage(m);
                                    }
                                }
                                catch (Exception e)
                                {
                                    Logger.ErrorFormat("{0}: error removing message from SQS ({1}): {2}",
                                                       m.Info(), e.GetType().FullName, e.Message);
                                }
                            }
                        }
                        double totalSec = 0.001 * sw.ElapsedMilliseconds;
                        if (totalSec > MAX_PROCESSING_SEC)
                        {
                            Logger.ErrorFormat("{0}: took {1:F3}s, but max processing time is {2:F3}s",
                                               m.Info(), totalSec, MAX_PROCESSING_SEC);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.ErrorFormat("{0}: error in message loop ({1}): {2}",
                                           m.Info(), e.GetType().FullName, e.Message);
                        Logger.Error(e.StackTrace);
                    }
                }
                int sleepMS = (int)(DEQUEUE_THROTTLE_MS - sw.ElapsedMilliseconds);
                if (sleepMS > 0)
                {
                    Thread.Sleep(sleepMS);
                }
            }
        }
    }
}
