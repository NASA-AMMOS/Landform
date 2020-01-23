using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using CommandLine;
using log4net;
using Amazon.SQS;
using OPS.Util;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;

namespace OPS.Pipeline
{
    [Verb("startworker", HelpText = "Starts a worker to process tiling messages")]
    public class StartWorkerOptions : PipelineCoreOptions
    {
        [Option(Default = false, HelpText = "Limit multiple workers to one core each")]
        public bool OneCorePerWorker { get; set; }
    }

    //TODO: https://github.jpl.nasa.gov/ProtoSpace/ps-pipeline/issues/159
    //this actually handles worker tasks for both alignment and tiling workflows
    //it should be a subcommand of Landform.exe not TilingServer.exe
    public class StartWorker : CloudPipeline
    {
        public const int MAX_PROCESSING_SEC = 6 * 60 * 60; //6h
        public const double HEARTBEAT_PERIOD_REL = 0.333;
        const int DEQUEUE_THROTTLE_MS = 500;
        const int IMAGE_CACHE = 5;
        const int DATA_PRODUCT_CACHE = 5;

        private class MessageRec
        {
            public double StartSec { get; private set; }
            public string Info { get; private set; }

            public volatile string ReceiptHandle;
            public volatile bool Done;
            public int NumHeartbeats;
            public int NumErrors;
            public double ApproxLastReceiveSec;

            public MessageRec(PipelineMessage m)
            {
                StartSec = UTCTime.Now();
                ReceiptHandle = m.ReceiptHandle;
                Info = m.Info();
                ApproxLastReceiveSec = 0.001 * m.ApproxReceiveMS;
            }
        }

        //indexed by message ID
        private Dictionary<string, MessageRec> messagesInFlight = new Dictionary<string, MessageRec>();

        private readonly StartWorkerOptions options;
        private readonly string queuePrefix;

        public static TypeDispatcher MakeDispatcher(PipelineCore pipeline)
        {
            var ret = new TypeDispatcher()
                .Case((BuildTilingInputMessage m) => new BuildTilingInput(pipeline, m).Process())
                .Case((DefineTilesMessage m) => new DefineTiles(pipeline, m).Process())
                .Case((ChunkInputMessage m) => new ChunkInput(pipeline, m).Process())
                .Case((BuildLeavesMessage m) => new BuildLeaves(pipeline, m).Process())
                .Case((BuildParentMessage m) => new BuildParent(pipeline, m).Process())
                .Case((BuildTilesetJsonMessage m) => new BuildTilesetJson(pipeline, m).Process())
                .Case((DetectFeaturesMessage m) => new DetectFeatures(pipeline, m).Process())
                .Case((MatchImagesMessage m) => new MatchImages(pipeline, m).Process());
            ret.Unhandled = (t, x) => pipeline.LogError("unknown worker message type: " + t);
            return ret;
        }

        public StartWorker(StartWorkerOptions options, string queuePrefix = "tiling")
            : base(options, queuePrefix: queuePrefix)
        {
            this.options = options;
            this.queuePrefix = queuePrefix;
        }

        public int Run()
        {
            DumpConfig();

            // Register filetype handlers
            new IVSerializer().Register();
            new DracoSerializer().Register();
            //Configure gdal
            GdalConfiguration.ConfigureGdal();

            if (options.SingleThreaded)
            {
                RunWorker(queuePrefix);
            }
            else
            {
                int numWorkers = CoreLimitedParallel.GetMaxCores();
                LogInfo("starting {0} workers", numWorkers);
                Task[] tasks = new Task[numWorkers];
                for (int i = 0; i < tasks.Length; i++)
                {
                    tasks[i] = Task.Run(() => {
                        while (true)
                        {   
                            try
                            {
                                RunWorker(queuePrefix);
                            }
                            catch (Exception e)
                            {
                                LogError("error in worker task ({0}): {1}", e.GetType().FullName, e.Message);
                                LogError(e.StackTrace);
                                // limit debug spew just in case a misconfiguration is causing this error
                                Thread.Sleep(2000);
                            }
                        }
                    });
                }
                //now that we've spawned the appropriate number of worker threads
                //we might at least optionally want force them to individually only use one core each
                //but with the current architecture of CoreLimitedParallel that would unfortunately also have the effect
                //of disabling parallelism across the whole app
                //and there are cases where we may not want that
                //such as when the workers are spawned within the same process as a master
                //
                //also the master may not always evenly distribute work across workers
                //i.e. for some workflows, and depending on the number of simultaneous users
                //the master might issue just one or a few tasks for workers to do
                //but those workers could still leverage more cores to execute them
                if (options.OneCorePerWorker)
                {
                    CoreLimitedParallel.SetMaxCores(1);
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
                double lastHeartbeat = -1;
                double heartbeatPeriod = HEARTBEAT_PERIOD_REL * WorkerQueue.TimeoutSec;
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
                        LogInfo("{0} messages in flight, heartbeat period {1:F3}s",
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
                            LogError("{0} {1:F3}s processing > max {2:F3}s, stopping heartbeat",
                                     rec.Info, totalSec, MAX_PROCESSING_SEC);
                            dead.Add(messageHandle);
                        }
                        else
                        {
                            try
                            {
                                /* int nh =*/ Interlocked.Increment(ref rec.NumHeartbeats);
                                WorkerQueue.UpdateTimeout(rec.ReceiptHandle, WorkerQueue.TimeoutSec);
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
                                    //LogError("{0}: in flight for {1:F3}s, max {2:F3}s, " +
                                    //         "latest receive time {3:F3}, " +
                                    //         "error {4}/{5} updating timeout ({6}): {7}{8}",
                                    //         rec.Info, totalSec, MAX_PROCESSING_SEC,
                                    //         rec.ApproxLastReceiveSec, ne, nh,
                                    //         e.GetType().FullName,
                                    //         e is AmazonSQSException ? (e as AmazonSQSException).ErrorCode + " ": "",
                                    //         e.Message);
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
                        if (bound > WorkerQueue.TimeoutSec)
                        {
                            LogError("heartbeat cycle took {0:F3}s, but msg visibility timeout is {1:F3}s",
                                     bound, WorkerQueue.TimeoutSec);
                        }
                    }

                    lastHeartbeat = start;
                }
            }

            return 0;
        }

        private bool StartedProcessing(MessageQueue queue, PipelineMessage m)
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
                        rt = 0.001 * m.ApproxReceiveMS;
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
                    LogWarn("{0}: already started processing at {1:F3}, total {2:F3}s, " +
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
            LogInfo("{0}: started processing at {1:F3}", m.Info(), UTCTime.Now());
            return true;
        }

        private MessageRec FinishedProcessing(PipelineMessage m, CloudPipeline pipeline)
        {
            MessageRec rec = null;
            double now = UTCTime.Now(), totalSec = -1;
            int ne = 0, nh = 0;
            if (options.SingleThreaded)
            {
                pipeline.CleanupTempDir();
            }
            else
            {
                Exception ex = null;
                lock (messagesInFlight)
                {
                    if (!messagesInFlight.TryGetValue(m.MessageId, out rec))
                    {
                        ex = new Exception("message not found");
                    }

                    if (rec != null && ex == null)
                    {
                        rec.Done = true; //in case heartbeat task is already iterating over a copy of messagesInFlight
                        totalSec = now - rec.StartSec;
                        ne = rec.NumErrors;
                        nh = rec.NumHeartbeats;
                        if (!messagesInFlight.Remove(m.MessageId))
                        {
                            ex = new Exception("failed to remove message");
                        }
                    }

                    if (messagesInFlight.Count == 0)
                    {
                        //all threads of this worker are now idle
                        //take this opportunity to clean up the temp dir to help constrain disk usage
                        //holding the lock on messagesInFlight so that any new messages are held until we're done
                        pipeline.CleanupTempDir();
                    }
                }
                if (ex != null)
                {
                    throw ex;
                }
            }
            LogInfo("{0}: finished processing at {1:F3}{2}{3}",
                    m.Info(), now, totalSec >= 0 ? string.Format(", total {0:F3}s", totalSec) : "",
                    ne > 0 ? string.Format(", {0}/{1} heartbeat errors", ne, nh) : "");
            return rec;
        }

        private void RunWorker(string queuePrefix)
        {
            //each worker thread has its own pipeline instance
            //this avoids the need for synchronization
            //all threads share the same logger which is MT safe
            //TODO we should probably switch to using a single shared pipeline instance
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/611
            var pipeline = new CloudPipeline(options, logger: Logger, quietInit: true,
                                             lruImageCache: IMAGE_CACHE, lruDataProductCache: DATA_PRODUCT_CACHE,
                                             initQueues: true, initTables: false, queuePrefix: queuePrefix);

            var dispatcher = MakeDispatcher(pipeline);

            void sendStatus(PipelineMessage m, string status, bool done = false)
            {
                pipeline.EnqueueToMaster(new StatusMessage(m.ProjectName, m.MessageId, m.GetType().Name, status, done));
            }

            while (true)
            {
                //only take one message at a time when we are ready to process it
                var m = pipeline.WorkerQueue.DequeueOne<PipelineMessage>();
                Stopwatch sw = new Stopwatch();
                sw.Start();
                if (m != null)
                {
                    try
                    {
                        if (StartedProcessing(pipeline.WorkerQueue, m))
                        {
                            bool handled = false;
                            try
                            {
                                sendStatus(m, "started");
                                dispatcher.Handle(m);
                                sendStatus(m, "complete", done: true);
                                handled = true;
                            }
                            catch (Exception e)
                            {
                                sendStatus(m, "error: " + e.Message, done: true);
                                LogError("{0}: processing error ({1}): {2}",
                                          m.Info(), e.GetType().FullName, e.Message);
                                LogError(e.StackTrace);
                            }

                            //always try to remove from messagesInFlight if we started processing
                            //even if the handler failed
                            MessageRec rec = null;
                            try
                            {
                                rec = FinishedProcessing(m, pipeline);
                            }
                            catch (Exception e)
                            {
                                LogError("{0}: error removing message from heartbeat table ({1}): {2}",
                                         m.Info(), e.GetType().FullName, e.Message);
                                LogError(e.StackTrace);
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
                                        pipeline.WorkerQueue.DeleteMessage(rec.ReceiptHandle);
                                    }
                                    else
                                    {
                                        pipeline.WorkerQueue.DeleteMessage(m);
                                    }
                                }
                                catch (Exception e)
                                {
                                    LogError("{0}: error removing message from SQS ({1}): {2}",
                                             m.Info(), e.GetType().FullName, e.Message);
                                }
                            }
                        }
                        double totalSec = 0.001 * sw.ElapsedMilliseconds;
                        if (totalSec > MAX_PROCESSING_SEC)
                        {
                            LogError("{0}: took {1:F3}s, but max processing time is {2:F3}s",
                                     m.Info(), totalSec, MAX_PROCESSING_SEC);
                        }
                    }
                    catch (Exception e)
                    {
                        LogError("{0}: error in message loop ({1}): {2}",
                                           m.Info(), e.GetType().FullName, e.Message);
                        LogError(e.StackTrace);
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
