using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CommandLine;
using OPS.Util;
using OPS.Cloud;
using OPS.Pipeline;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    public class LandformServiceOptions : LandformShellOptions
    {
        [Value(0, Required = false, HelpText = "project name, must omit if running as service", Default = null)]
        public override string ProjectName { get; set; }

        [Option(Required = false, Default = false, HelpText = "run as service")]
        public bool Service { get; set; }

        [Option(Required = false, Default = null, HelpText = "Override message queue name")]
        public string QueueName { get; set; }

        [Option(Required = false, Default = null, HelpText = "Override fail message queue name")]
        public string FailQueueName { get; set; }

        [Option(Required = false, Default = false, HelpText = "Message queue is Landform owned")]
        public bool LandformOwnedQueue { get; set; }

        [Option(Required = false, Default = false, HelpText = "Fail message queue is Landform owned")]
        public bool LandformOwnedFailQueue { get; set; }

        [Option(Required = false, Default = false, HelpText = "Use generic message type")]
        public bool UseGenericMessageType { get; set; }

        [Option(Required = false, Default = null, HelpText = "JSON file of message to send")]
        public string SendMessage { get; set; }

        [Option(Required = false, Default = false, HelpText = "Delete message and fail queues iff Landform owned")]
        public bool DeleteQueues { get; set; }

        [Option(Required = false, Default = 0, HelpText = "SQS queue message timeout, nonpositive to use default")]
        public int MessageTimeoutSec { get; set; }

        [Option(Required = false, Default = 0, HelpText = "Maximum handler runtime, nonpositive to use default")]
        public int MaxHandlerSec { get; set; }

        [Option(Required = false, Default = 0, HelpText = "Maximum unhandled message age, nonpositive to use default")]
        public int MaxMessageAgeSec { get; set; }
    }
    
    public abstract class LandformService : LandformShell
    {
        public const double DEF_HEARTBEAT_REL_PERIOD = 0.333;
        public const int DEF_MAX_HANDLER_SEC = 10 * 60; //10 minutes
        public const int DEF_MAX_MESSAGE_AGE_SEC = 60 * 60; //1 hour
        public const int DEF_DEQUEUE_THROTTLE_SEC = 1;
        public const int SERVICE_LOOP_RETRY_SEC = 60;

        protected LandformServiceOptions lvopts;

        protected MessageQueue messageQueue;
        protected MessageQueue failMessageQueue;

        private QueueMessage currentMessage;
        private double messageStartSec;

        public LandformService(LandformServiceOptions options) : base(options)
        {
            this.lvopts = options;
        }

        public int Run()
        {
            if (!lvopts.Service)
            {
                StartStopwatch();
            }

            try
            {
                if (!ParseArguments())
                {
                    return 0; //help
                }

                if (lvopts.DeleteQueues)
                {
                    RunPhase("delete queues", DeleteQueues);
                }
                else if (!string.IsNullOrEmpty(lvopts.SendMessage))
                {
                    RunPhase("send message", SendMessage);
                }
                else if (lvopts.Service)
                {
                    RunService();
                }
                else
                {
                    RunBatch();
                }
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            if (!lvopts.Service)
            {
                StopStopwatch();
            }

            return 0;
        }

        protected override bool ParseArguments()
        {
            if (!base.ParseArguments())
            {
                return false; //e.g. --help
            }

            bool sendMessage = !string.IsNullOrEmpty(lvopts.SendMessage);
            var svcOpts = new bool[] { lvopts.DeleteQueues, sendMessage, lvopts.Service };
            if (svcOpts.Where(o => o).Count() > 1)
            {
                throw new Exception("--deletequeues, --sendmessage, and --service are mutually exclusive");
            }
            if (svcOpts.Any(o => o))
            {
                if (!string.IsNullOrEmpty(lvopts.ProjectName))
                {
                    throw new Exception("project name must be omitted with --deletequeue, --sendmessage, --service");
                }
                messageQueue = GetMessageQueue(); //creates queue if necessary with --landformowned
                if (lvopts.Service || lvopts.DeleteQueues)
                {
                    failMessageQueue = GetFailMessageQueue(); //creates queue if necessary with --landformowned
                }
            }

            int timeoutSec = messageQueue != null ? messageQueue.TimeoutSec : GetDefaultMessageTimeoutSec();
            pipeline.LogInfo("message timeout: {0}", Fmt.HMS(timeoutSec * 1000));
            pipeline.LogInfo("max handler time: {0}", Fmt.HMS(GetMaxHandlerSec() * 1000));
            pipeline.LogInfo("max message age: {0}", Fmt.HMS(GetMaxMessageAgeSec() * 1000));

            return true;
        }

        protected abstract void RunBatch();

        protected abstract string GetDefaultQueueName();

        protected abstract string GetDefaultFailQueueName();

        protected abstract QueueMessage DequeueOneMessage();

        /// <summary>
        /// Should not throw.  
        /// </summary>
        protected abstract string DescribeMessage(QueueMessage msg);

        /// <summary>
        /// Should not throw.  
        /// </summary>
        protected abstract bool AcceptMessage(QueueMessage msg);

        /// <summary>
        /// Can throw.  
        /// </summary>
        protected abstract bool HandleMessage(QueueMessage msg);

        /// <summary>
        /// Used only by SendMessage().
        /// </summary>
        protected abstract QueueMessage ParseMessage(string json);

        protected virtual string GetQueueName()
        {
            return !string.IsNullOrEmpty(lvopts.QueueName) ? lvopts.QueueName : GetDefaultQueueName();
        }

        protected virtual string GetFailQueueName()
        {
            return !string.IsNullOrEmpty(lvopts.FailQueueName) ? lvopts.FailQueueName : GetDefaultFailQueueName();
        }

        /// <summary>
        /// When we dequeue a message SQS will prevent it from also being received by another worker for this long.
        /// But as we handle it we'll continually extend our lease on it in increments of this many seconds.
        /// If we successfully finish handling it we'll remove it from the queue.
        /// If we choose not to handle it, or if our handler fails,
        /// it will get returned to the queue when the latest lease times out.
        ///
        /// Note: the actual message timeout is a parameter of the SQS queue itself.
        /// For Landform owned queues we ensure that matches this default.
        /// Otherwise we issue a warning if the two differ and use the queue's timeout.
        /// </summary>
        protected virtual int GetDefaultMessageTimeoutSec()
        {
            return lvopts.MessageTimeoutSec > 0 ? lvopts.MessageTimeoutSec : MessageQueue.DEF_TIMEOUT_SEC;
        }

        /// <summary>
        /// Message handlers that run longer than this will be killed.  
        /// </summary>
        protected virtual int GetMaxHandlerSec()
        {
            return lvopts.MaxHandlerSec > 0 ? lvopts.MaxHandlerSec : DEF_MAX_HANDLER_SEC;
        }

        /// <summary>
        /// Messages that keep being received longer than this many seconds
        /// since the first time they are received by any worker
        /// (e.g. because they keep failing to be processed)
        /// will be culled from the queue.
        /// </summary>
        protected virtual int GetMaxMessageAgeSec()
        {
            return  lvopts.MaxMessageAgeSec > 0 ? lvopts.MaxMessageAgeSec : DEF_MAX_MESSAGE_AGE_SEC;
        }

        protected virtual int GetDequeueThrottleSec()
        {
            return DEF_DEQUEUE_THROTTLE_SEC;
        }

        protected virtual double GetHeartbeatRelPeriod()
        {
            return DEF_HEARTBEAT_REL_PERIOD;
        }

        protected virtual bool IsQueueLandformOwned()
        {
            return lvopts.LandformOwnedQueue;
        }

        protected virtual bool IsFailQueueLandformOwned()
        {
            return lvopts.LandformOwnedFailQueue;
        }

        protected virtual MessageQueue GetMessageQueue()
        {
            return GetMessageQueue(GetQueueName(), GetDefaultMessageTimeoutSec(), IsQueueLandformOwned(), "message");
        }

        protected virtual MessageQueue GetFailMessageQueue()
        {
            return GetMessageQueue(GetFailQueueName(), GetDefaultMessageTimeoutSec(), IsFailQueueLandformOwned(),
                                   "fail message");
        }

        private MessageQueue GetMessageQueue(string name, int defTimeoutSec, bool landformOwned, string what)
        {
            if (string.IsNullOrEmpty(name))
            {
                pipeline.LogInfo("no {0} queue", what);
                return null;
            }
            pipeline.LogInfo("opening/creating {0} queue: {1} ({2}landform owned)",
                             what, name, landformOwned ? "" : "not ");
            MessageQueue queue = null;
            while (true)
            {
                try
                {
                    queue = new MessageQueue(name, awsProfile, awsRegion, defTimeoutSec, pipeline, lvopts.Quiet,
                                             landformOwned, autoTypes: false);
                    pipeline.LogInfo("{0} queue {1}: default timeout {2}s, actual timeout {3}s",
                                     what, name, defTimeoutSec, queue.TimeoutSec);
                    break;
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, string.Format("error opening/creating {0} queue, retrying in {1}",
                                                            what, Fmt.HMS(SERVICE_LOOP_RETRY_SEC * 1000)));
                    SleepSec(SERVICE_LOOP_RETRY_SEC);
                }
            }
            return queue;
        }

        private void SendMessage()
        {
            pipeline.LogInfo("{0}sending message to queue {1}", lvopts.DryRun ? "dry " : "", messageQueue.Name);
            if (!lvopts.DryRun)
            {
                messageQueue.Enqueue(ParseMessage(File.ReadAllText(lvopts.SendMessage)));
            }
        }

        private void DeleteQueues()
        {
            void deleteQueue(MessageQueue queue, string what)
            {
                if (queue != null)
                {
                    if (queue.LandformOwned)
                    {
                        pipeline.LogInfo("{0}deleting {1} queue {2}", lvopts.DryRun ? "dry " : "", what, queue.Name);
                        if (!lvopts.DryRun)
                        {
                            queue.Delete();
                        }
                    }
                    else
                    {
                        pipeline.LogWarn("cannot delete {0} queue {1}, not owned by Landform", what, queue.Name);
                    }
                }
            }
            deleteQueue(messageQueue, "message");
            deleteQueue(failMessageQueue, "fail");
        }

        private void RunService()
        {
            Task.Run(() => HeartbeatLoop());
            ServiceLoop();
        }

        private void ServiceLoop()
        {
            int throttleSec = GetDequeueThrottleSec();
            int maxAgeSec = GetMaxMessageAgeSec();
            pipeline.LogInfo("running service loop on message queue {0}, throttle {1}s",
                             messageQueue.Name, throttleSec);
            while (true)
            {
                try
                {
                    double startSec = UTCTime.Now();
                    QueueMessage msg = DequeueOneMessage();
                    if (msg != null)
                    {
                        string desc = DescribeMessage(msg);
                        int ageSec = (int)(0.001 * (msg.ApproxReceiveMS - msg.ApproxFirstReceiveMS));
                        bool tooOld = ageSec > maxAgeSec;
                        bool accepted = AcceptMessage(msg);
                        bool handled = false;

                        if (accepted && !tooOld)
                        {
                            StartStopwatch();
                            
                            currentMessage = msg;
                            messageStartSec = UTCTime.Now();
                            
                            pipeline.LogInfo("processing {0}", desc);

                            try
                            {
                                handled = HandleMessage(msg);
                            }
                            catch (Exception msgException)
                            {
                                pipeline.LogException(msgException, "error handling message");
                            }
                            
                            currentMessage = null;
                            
                            StopStopwatch(brief: true);
                        }

                        if (tooOld)
                        {
                            pipeline.LogError("{0} too old ({1} > {2}), removing from queue, {3} fail queue",
                                              desc, Fmt.HMS(1000 * ageSec), Fmt.HMS(1000 * maxAgeSec),
                                              failMessageQueue != null ? "adding to" : "no");
                        }

                        if (!accepted || handled || tooOld)
                        {
                            try
                            {
                                messageQueue.DeleteMessage(msg);
                            }
                            catch (Exception deleteException)
                            {
                                pipeline.LogException(deleteException, "error deleting message");
                            }
                        }

                        if (accepted && tooOld && failMessageQueue != null)
                        {
                            try
                            {
                                failMessageQueue.Enqueue(msg);
                            }
                            catch (Exception failQueueException)
                            {
                                pipeline.LogException(failQueueException, "adding message to fail queue");
                            }
                        }
                    }

                    double elapsedSec = UTCTime.Now() - startSec;
                    SleepSec(throttleSec - elapsedSec);
                }
                catch (Exception serviceException)
                {
                    pipeline.LogException(serviceException, string.Format("service loop error, retrying in {0}",
                                                                          Fmt.HMS(SERVICE_LOOP_RETRY_SEC * 1000)));
                    SleepSec(SERVICE_LOOP_RETRY_SEC);
                }
            }
        }

        private void HeartbeatLoop()
        {
            //attempt to kill subprocesses for any tasks that run too long
            
            //also progressively update the visibility timeout for current message
            //while a message is in its visiblity timeout it is still in the SQS queue but hidden from other workers
            //this scheme helps avoid multiple workers from trying to process the same message
            //https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-visibility-timeout.html
            //however note that unless we use FIFO queues (which we currently do not)
            //then it's possible that messages will be received more than once
            //FIFO queues impose a limit on the max transactions per second
            //and also aren't available in us-west-1 region as of this writing
            //(and they're a little more expensive)

            double maxHandlerSec = GetMaxHandlerSec();
            double lastHeartbeatSec = -1;
            int timeoutSec = messageQueue.TimeoutSec;
            double targetPeriod = GetHeartbeatRelPeriod() * timeoutSec;
            pipeline.LogInfo("running heartbeat, period {0:F3}s, message timeout {1}s, max handler {2}",
                             targetPeriod, timeoutSec, Fmt.HMS(1000 * maxHandlerSec));
            while (true)
            {
                if (lastHeartbeatSec >= 0)
                {
                    //try to maintain heartbeat period proportional to queue timout
                    double actualPeriod = UTCTime.Now() - lastHeartbeatSec;
                    SleepSec(targetPeriod - actualPeriod);
                }
                
                double startSec = UTCTime.Now();

                var msg = currentMessage;
                if (msg != null)
                {
                    var totalSec = UTCTime.Now() - messageStartSec;
                    if (totalSec > maxHandlerSec)
                    {
                        pipeline.LogError("handler has run for {0} > {1}, killing",
                                          Fmt.HMS(1000 * totalSec), Fmt.HMS(1000 * maxHandlerSec));
                        KillCurrentCommand(); //swallows exceptions, but handler will throw exception if killed
                    }
                    else
                    {
                        try
                        {
                            messageQueue.UpdateTimeout(msg, timeoutSec);
                        }
                        catch (Exception ex)
                        {
                            pipeline.LogException(ex, "error updating message timeout");
                        }
                    }
                }

                if (lastHeartbeatSec >= 0)
                {
                    //upper bound on time between visibility update: end of this heartbeat - start of previous
                    double bound = UTCTime.Now() - lastHeartbeatSec;
                    if (bound > timeoutSec)
                    {
                        pipeline.LogError("heartbeat {0:F3}s exceeded visibility timeout {1:F3}s", bound, timeoutSec);
                    }
                }
                
                lastHeartbeatSec = startSec;
            }
        }
    }
}
