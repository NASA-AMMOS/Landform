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

        [Option(Required = false, Default = false, HelpText = "Landform owned queue")]
        public bool LandformOwnedQueue { get; set; }

        [Option(Required = false, Default = false, HelpText = "Use generic message type")]
        public bool UseGenericMessageType { get; set; }

        [Option(Required = false, Default = null, HelpText = "JSON file of message to send")]
        public string SendMessage { get; set; }

        [Option(Required = false, Default = false, HelpText = "Delete message queue, requires --landformownedqueue")]
        public bool DeleteQueue { get; set; }
    }
    
    public abstract class LandformService : LandformShell
    {
        public const double DEF_HEARTBEAT_REL_PERIOD = 0.333;
        public const double DEF_MAX_HANDLER_SEC = 10 * 60;
        public const double DEF_DEQUEUE_THROTTLE_SEC = 1;

        protected LandformServiceOptions lvopts;

        protected MessageQueue messageQueue;

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

                if (lvopts.DeleteQueue)
                {
                    RunPhase("delete queue", DeleteQueue);
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
            bool sendMessage = !string.IsNullOrEmpty(lvopts.SendMessage);
            var svcOpts = new bool[] { lvopts.DeleteQueue, sendMessage, lvopts.Service };
            if (svcOpts.Where(o => o).Count() > 1)
            {
                throw new Exception("--deletequeue, --sendmessage, and --service are mutually exclusive");
            }
            if (lvopts.DeleteQueue && !lvopts.LandformOwnedQueue)
            {
                throw new Exception("--deletequeue requires --landformowned");
            }
            if (svcOpts.Any(o => o))
            {
                if (!string.IsNullOrEmpty(lvopts.ProjectName))
                {
                    throw new Exception("project name must be omitted with --deletequeue, --sendmessage, --service");
                }
                messageQueue = GetMessageQueue(); //creates queue if necessary with --landformowned
            }
            return base.ParseArguments();
        }

        protected abstract void RunBatch();

        protected abstract string GetDefaultQueueName();

        /// <summary>
        /// Messages that keep being received longer than this many seconds
        /// since the first time they are received by any worker
        /// (e.g. because they keep failing to be processed)
        /// will be culled from the queue.
        /// </summary>
        protected abstract int GetMaxMessageAgeSec();

        protected abstract string DescribeMessage(QueueMessage msg);

        protected abstract QueueMessage DequeueOneMessage();

        protected abstract bool HandleMessage(QueueMessage msg);

        protected abstract QueueMessage ParseMessage(string json);

        protected virtual string GetQueueName()
        {
            return !string.IsNullOrEmpty(lvopts.QueueName) ? lvopts.QueueName : GetDefaultQueueName();
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
            return MessageQueue.DEF_TIMEOUT_SEC;
        }

        protected virtual double GetHeartbeatRelPeriod()
        {
            return DEF_HEARTBEAT_REL_PERIOD;
        }

        protected virtual double GetMaxHandlerSec()
        {
            return DEF_MAX_HANDLER_SEC;
        }

        protected virtual bool IsQueueLandformOwned()
        {
            return lvopts.LandformOwnedQueue;
        }

        protected virtual double GetDequeueThrottleSec()
        {
            return DEF_DEQUEUE_THROTTLE_SEC;
        }

        protected virtual MessageQueue GetMessageQueue()
        {
            string name = GetQueueName();
            int defTimeoutSec = GetDefaultMessageTimeoutSec();
            bool owned = IsQueueLandformOwned();
            var queue = new MessageQueue(name, awsProfile, awsRegion, defTimeoutSec, pipeline, lvopts.Quiet, owned);
            pipeline.LogInfo("message queue {0}, {1}landform owned, default timeout {2}s, actual timeout {3}s",
                             name, owned ? "" : "not ", defTimeoutSec, queue.TimeoutSec);
            return queue;
        }

        private void SendMessage()
        {
            pipeline.LogInfo("sending message to queue {0}", messageQueue.Name);
            messageQueue.Enqueue(ParseMessage(File.ReadAllText(lvopts.SendMessage)));
        }

        private void DeleteQueue()
        {
            if (!messageQueue.LandformOwned)
            {
                throw new Exception("cannot delete message queue, not owned by Landform");
            }
            pipeline.LogInfo("deleting message queue {0}", messageQueue.Name);
            messageQueue.Delete();
        }

        private void RunService()
        {
            Task.Run(() => HeartbeatLoop());
            ServiceLoop();
        }

        private void ServiceLoop()
        {
            double throttleSec = GetDequeueThrottleSec();
            int maxAgeSec = GetMaxMessageAgeSec();
            pipeline.LogInfo("running service loop on message queue {0}, throttle {1:F3}s",
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
                        bool handled = false;

                        if (!tooOld)
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
                            
                            StopStopwatch();
                        }
                        else
                        {
                            pipeline.LogError("{0} too old ({1} > {2}), removing from queue without processing", desc,
                                              Fmt.HMS(1000 * ageSec), Fmt.HMS(1000 * maxAgeSec));
                        }

                        if (handled || tooOld)
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
                    }

                    double elapsedSec = UTCTime.Now() - startSec;
                    SleepSec(throttleSec - elapsedSec);
                }
                catch (Exception serviceException)
                {
                    pipeline.LogException(serviceException, "service loop error");
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
                        pipeline.LogError("handler has run for {0} > {1}s, killing",
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
