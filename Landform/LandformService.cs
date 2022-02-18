using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Amazon.SQS.Model;
using OPS.Util;
using OPS.Cloud;
using OPS.Pipeline;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    public enum MessageType { Generic, S3Event, SNSWrappedS3Event }

    public enum IdleShutdownMethod
    { None, StopInstance, Shutdown, StopInstanceOrShutdown, ScaleToZero, LogIdle, LogIdleProtected }

    public class LandformServiceOptions : LandformShellOptions
    {
        [Value(0, Required = false, HelpText = "project name, must omit if running as service", Default = null)]
        public override string ProjectName { get; set; }

        [Option(Default = false, HelpText = "run as service")]
        public bool Service { get; set; }

        [Option(Default = null, HelpText = "Message queue name, required with --service")]
        public string QueueName { get; set; }

        [Option(Default = "auto", HelpText = "Fail queue name, null, empty, or \"none\" to disable, \"auto\" to append suffix \"-fail\" to --queuename")]
        public string FailQueueName { get; set; }

        [Option(Default = false, HelpText = "Message queue is Landform owned")]
        public bool LandformOwnedQueue { get; set; }

        [Option(Default = false, HelpText = "Fail message queue is Landform owned")]
        public bool LandformOwnedFailQueue { get; set; }

        [Option(Default = false, HelpText = "All queues are Landform owned")]
        public bool LandformOwnedQueues { get; set; }

        [Option(Default = MessageType.SNSWrappedS3Event, HelpText = "Message type (Generic, S3Event, SNSWrappedS3Event")]
        public MessageType MessageType { get; set; }

        [Option(Default = null, HelpText = "JSON file or raw URL of message to send")]
        public string SendMessage { get; set; }

        [Option(Default = 0, HelpText = "Peek messages in message queue")]
        public int PeekMessages { get; set; }

        [Option(Default = 0, HelpText = "Peek messages in fail queue")]
        public int PeekFailedMessages { get; set; }

        [Option(Default = 0, HelpText = "Move messages from fail queue to message queue")]
        public int RetryMessages { get; set; }

        [Option(Default = 0, HelpText = "Move messages from message queue to fail queue")]
        public int FailMessages { get; set; }

        [Option(Default = 0, HelpText = "Drop messages")]
        public int DropMessages { get; set; }

        [Option(Default = 0, HelpText = "Drop failed messages")]
        public int DropFailedMessages { get; set; }

        [Option(Default = false, HelpText = "Delete message and fail queues iff Landform owned")]
        public bool DeleteQueues { get; set; }

        [Option(Default = 0, HelpText = "SQS queue message timeout, nonpositive to use default")]
        public int MessageTimeoutSec { get; set; }

        [Option(Default = 0, HelpText = "Maximum handler runtime, nonpositive to use default")]
        public int MaxHandlerSec { get; set; }

        [Option(Default = 0, HelpText = "Maximum unhandled message age, nonpositive to use default")]
        public int MaxMessageAgeSec { get; set; }

        [Option(Default = LandformService.DEF_MAX_RECEIVE_COUNT, HelpText = "Maximum message receive count, nonpositive for unlimited")]
        public int MaxReceiveCount { get; set; }

        [Option(HelpText = "Drop messages that may be poison without retry, e.g. because a handler exceeded --maxhandlersec", Default = false)]
        public bool DropPoisonMessages { get; set; }

        [Option(HelpText = "Deprioritize message retries by moving messages to the back of the queue.  May not be supported by all service types because the retry count needs to be kept in the message.", Default = false)]
        public bool DeprioritizeRetries { get; set; }

        [Option(Default = -1, HelpText = "When running as EC2 instance, attempt shutdown after idle for at least this many seconds, non-positive disables")]
        public int IdleShutdownSec { get; set; }

        [Option(Default = IdleShutdownMethod.None, HelpText = "When running as EC2 instance use this method to shutdown after idle time exceeded")]
        public IdleShutdownMethod IdleShutdownMethod { get; set; }

        [Option(Default = null, HelpText = "EC2 auto scale group name, required with --idleshutdownmethod=ScaleToZero or --idleshutdownmethod=LogIdleProtected")]
        public string AutoScaleGroup { get; set; }

        [Option(HelpText = "Use default AWS profile (vs profile from credential refresh) for SQS client", Default = false)]
        public bool UseDefaultAWSProfileForSQSClient { get; set; }

        [Option(Default = LandformService.DEF_WATCHDOG_PERIOD, HelpText = "Watchdog period (seconds), non-positive to disable")]
        public double WatchdogPeriod { get; set; }

        [Option(Default = LandformService.DEF_WATCHDOG_WARN_GB, HelpText = "Watchdog free system virtual memory warning level, absolute GB or fraction")]
        public double WatchdogWarnGB { get; set; }

        [Option(Default = LandformService.DEF_WATCHDOG_ACTION_GB, HelpText = "Watchdog free system virtual memory action level, absolute GB or fraction")]
        public double WatchdogActionGB { get; set; }

        [Option(Default = LandformService.DEF_WATCHDOG_ABORT_GB, HelpText = "Watchdog free system virtual memory abort level, absolute GB or fraction")]
        public double WatchdogAbortGB { get; set; }

        [Option(Default = 0, HelpText = "Positive integer number of GB to leak per watchdog period for leak test")]
        public int WatchdogLeakTest { get; set; }

        [Option(Default = null, HelpText = "Comma separated list of processes to check")]
        public string CheckProcesses { get; set; }

        [Option(Default = "mission", HelpText = "Watchdog SSM process, empty to disable, \"mission\" to use mission-specific default")]
        public string WatchdogSSMProcess { get; set; }

        [Option(Default = "mission", HelpText = "Watchdog SSM restart command, empty to disable, \"mission\" to use mission-specific default")]
        public string WatchdogSSMCommand { get; set; }

        [Option(Default = "mission", HelpText = "Watchdog CloudWatch process, empty to disable, \"mission\" to use mission-specific default")]
        public string WatchdogCloudWatchProcess { get; set; }

        [Option(Default = "mission", HelpText = "Watchdog CloudWatch restart command, empty to disable, \"mission\" to use mission-specific default")]
        public string WatchdogCloudWatchCommand { get; set; }
    }
    
    public abstract class LandformService : LandformShell
    {
        public const double DEF_HEARTBEAT_REL_PERIOD = 0.333;

        public const int DEF_DEQUEUE_THROTTLE_MS = 1;

        public const int SERVICE_LOOP_THROTTLE_SEC = 60;

        public const int IDLE_EVENT_THROTTLE_SEC = 60;

        public const int DEF_MAX_HANDLER_SEC = 10 * 60; //10 minutes
        public const int DEF_MAX_MESSAGE_AGE_SEC = 60 * 60; //1 hour

        public const double DEF_WATCHDOG_PERIOD = 5; //seconds
        public const double DEF_WATCHDOG_WARN_GB = 20;
        public const double DEF_WATCHDOG_ACTION_GB = 10;
        public const double DEF_WATCHDOG_ABORT_GB = 5;

        //if actual total memory is less than this
        //then interpret absolute watchdog thresholds as relative to this
        public const double DEF_WATCHDOG_TOTAL_GB = 80;

        public const int WATCHDOG_ABORT_PERIODS = 2;
        public const int WATCHDOG_ABORT_EXIT_CODE = 10;

        public const int WATCHDOG_PROCESS_RESTART_PERIODS = 12;

        //there is an interplay between the max message age and the max receive count
        //because each time a message is received it becomes invisible for at least the visibility timeout of the queue
        //which is typically 30s
        //(our heartbeat loop may further extend the visibility timeout, but it is always at least that)
        //so e.g. 10 receives should mean that the message is at least 300s (5 min) old
        //
        //if the max message age is e.g. 1 hour, and there are active and available consumers on the queue,
        //then a bad message might typically get culled due to max receive count well before it reaches max age
        //
        //note that message "age" is actually computed as the time since the first receive of the message
        //so messages posted to queues while there are no active or available consumers can wait in the queue
        //for an arbitrary amount of time before they are first received
        //
        //so if max message age is 1 hour, max receive count is 10, and queue visibility timeout is 30s
        //under what circumstances can a messge possibly be culled due to max age?
        //one case is if workers actually spend more than an hour in aggegate trying to process the message,
        //making fewer than 10 total attempts, but always fail
        public const int DEF_MAX_RECEIVE_COUNT = 10;

        //ASG scale down trigger may watch for this text
        public const string LOG_IDLE_MSG = "service idle, shutdown requested";

        protected LandformServiceOptions lvopts;

        protected bool serviceMode, serviceUtilMode;

        protected MessageQueue messageQueue;
        protected MessageQueue failMessageQueue;

        protected int defMaxHandlerSec, defMaxMessageAgeSec;

        /// <summary>
        /// ServiceLoop() acquires credentialRefreshLock before calling RefreshCredentials().
        /// Other uses of credentials throughout ServiceLoop() (i.e. in the main thread), including in subclass
        /// implementations of HandleMessage(), are not locked because they cannot overlap with the call to
        /// RefreshCredentials() which is in the same thread.
        ///
        /// To prevent RefreshCredentials() from being called while the credentials may be in use, other threads which
        /// require credentials should hold credentialRefreshLock only while needed.  Potentially long running
        /// operations should use longRunningCredentialRefereshLock instead.  The only place that both locks should be
        /// qcquired simultaneously is in ServiceLoop() before calling RefreshCredentials().  To prevent any chance of
        /// deadlock acquisition order should always be:
        /// credentialRefreshLock[, longRunningCredentialRefereshLock], deleteMessageLock.
        ///
        /// For example
        /// * HeartbeatLoop() acquires credentials when it needs to update SQS message timeouts.
        /// * ProcessContextual.MasterLoop() acquires credentials while it may use PLACES or S3.
        /// </summary>
        protected object credentialRefreshLock = new Object(), longRunningCredentialRefereshLock = new Object();

        /// <summary>
        /// ServiceLoop() acquires deleteMessageLock while deleting messages from the SQS queue.
        /// HeartbeatLoop() also acquires it while updating the message timeout.
        /// This avoids overlaps between deleting the message and updating its timeout.
        /// HeartbeatLoop() actually needs both credentialRefreshLock and deleteMessageLock while updating the timeout,
        /// but that's OK.  It's the only thing that should acquire both at the same time.
        /// To prevent any chance of deadlock acquisition order should always be:
        /// credentialRefreshLock[, longRunningCredentialRefereshLock], deleteMessageLock.
        /// </summary>
        private object deleteMessageLock = new Object();

        private volatile QueueMessage currentMessage;
        private volatile bool killedCurrentHandler;

        //in C# 64 bit fields can't  be volatile, so can't use double or long here
        //uint max is about 4.2e9; 100y since epoch in sec is 100 * 365 * 24 * 60 * 60 ~= 3.1e6
        private volatile uint messageStartSec;

        protected string selfEC2InstanceID;

        protected volatile bool shuttingDown;
        private double idleStartTime = -1, lastIdleEventTime = -1;

        private double totalMemory = -1;
        private double watchdogWarnGB = -1;
        private double watchdogActionGB = -1;
        private double watchdogAbortGB = -1;

        private double minFreeMemory = -1;
        private int numWatchdogWarns = -1;
        private int numWatchdogCollects = -1;
        private int numWatchdogErrors = -1;
        private object watchdogStatsLock = new Object();
        private DateTime? minFreeMemoryTime = null;

        /// <summary>
        /// Simple JSON message for testing or in workflows not involving [SNS wrapped] S3 event messages.
        /// </summary>
        private class GenericMessage : QueueMessage
        {
            public string url;

            public GenericMessage(string url)
            {
                this.url = url;
            }
        }

        public LandformService(LandformServiceOptions options) : base(options)
        {
            this.lvopts = options;
            defMaxHandlerSec = DEF_MAX_HANDLER_SEC;
            defMaxMessageAgeSec = DEF_MAX_MESSAGE_AGE_SEC;
        }

        public int Run()
        {
            int exitCode = 0;
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
                else if (lvopts.PeekMessages > 0)
                {
                    RunPhase("peek messages", PeekMessages);
                }
                else if (lvopts.PeekFailedMessages > 0)
                {
                    RunPhase("peek failed messages", PeekFailedMessages);
                }
                else if (!string.IsNullOrEmpty(lvopts.SendMessage))
                {
                    RunPhase("send message", SendMessage);
                }
                else if (lvopts.RetryMessages > 0)
                {
                    RunPhase("retry messages", RetryMessages);
                }
                else if (lvopts.FailMessages > 0)
                {
                    RunPhase("fail messages", FailMessages);
                }
                else if (lvopts.DropMessages > 0)
                {
                    RunPhase("dropping messages", DropMessages);
                }
                else if (lvopts.DropFailedMessages > 0)
                {
                    RunPhase("dropping failed messages", DropFailedMessages);
                }
                else if (lvopts.WatchdogLeakTest > 0)
                {
                    RunPhase("testing watchdog", WatchdogLoop);
                }
                else if (!string.IsNullOrEmpty(lvopts.CheckProcesses))
                {
                    RunPhase("checking processes", () =>
                             ConsoleHelper.CheckProcesses(pipeline, StringHelper.ParseList(lvopts.CheckProcesses)));
                }
                else if (serviceMode)
                {
                    RunService();
                }
                else
                {
                    Task.Run(() => WatchdogLoop());
                    RunBatch();
                    abort = true;
                }
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                exitCode = 1;
                abort = true;
            }

            StopStopwatch();

            return exitCode;
        }

        protected override bool ParseArguments()
        {
            if (!base.ParseArguments())
            {
                return false; //e.g. --help
            }

            lvopts.LandformOwnedQueue |= lvopts.LandformOwnedQueues;
            lvopts.LandformOwnedFailQueue |= lvopts.LandformOwnedQueues;

            bool sendMessage = !string.IsNullOrEmpty(lvopts.SendMessage);
            bool peekMessages = lvopts.PeekMessages > 0;
            bool peekFailedMessages = lvopts.PeekFailedMessages > 0;
            bool retryMessages = lvopts.RetryMessages > 0;
            bool failMessages = lvopts.FailMessages > 0;
            bool dropMessages = lvopts.DropMessages > 0;
            bool dropFailedMessages = lvopts.DropFailedMessages > 0;
            bool leakTest = lvopts.WatchdogLeakTest > 0;
            bool checkProcesses = !string.IsNullOrEmpty(lvopts.CheckProcesses);

            string utils = "--peekmessages, --peekfailedmessages, --deletequeues, --sendmessage, --retrymessages, " +
                "--failmessages, --dropmessages, --dropfailedmessages, --watchdogleaktest, --checkprocesses";

            var utilOpts = new bool[] {
                lvopts.DeleteQueues, sendMessage, peekMessages, peekFailedMessages, retryMessages, failMessages,
                dropMessages, dropFailedMessages, leakTest, checkProcesses };
            serviceUtilMode = utilOpts.Any(o => o);
            serviceMode = IsService();

            if (serviceMode && serviceUtilMode)
            {
                throw new Exception(utils + ", and --service are mutually exclusive");
            }

            if (leakTest || checkProcesses)
            {
                return true;
            }

            if (serviceMode || serviceUtilMode)
            {
                if (credentialRefreshSec > 0)
                {
                    RefreshCredentials();
                }

                if (!string.IsNullOrEmpty(lvopts.ProjectName))
                {
                    throw new Exception("project name must be omitted for service");
                }

                if (string.IsNullOrEmpty(lvopts.QueueName))
                {
                    throw new Exception("--queuename must be specified for service");
                }
                messageQueue = GetMessageQueue(); //creates queue if necessary with --landformowned

                bool requireFailQueue = peekFailedMessages || retryMessages || failMessages || dropFailedMessages;
                if (serviceMode || lvopts.DeleteQueues || requireFailQueue)
                {
                    if (!string.IsNullOrEmpty(lvopts.FailQueueName) && lvopts.FailQueueName.ToLower() != "none")
                    {
                        failMessageQueue = GetFailMessageQueue(); //creates queue if necessary with --landformowned
                    }
                    else if (requireFailQueue)
                    {
                        throw new Exception("--failqueuename required for " +
                                            "--retrymessages, --failmessages, --dropfailedmessages");
                    }
                }
            }

            if (serviceMode)
            {
                selfEC2InstanceID = ComputeHelper.GetSelfInstanceID(pipeline);
                if (!string.IsNullOrEmpty(selfEC2InstanceID))
                {
                    pipeline.LogInfo("self EC2 instance ID: {0}", selfEC2InstanceID);
                }
                else
                {
                    pipeline.LogInfo("failed to get self EC2 instance ID");
                }

                if (lvopts.IdleShutdownSec > 0 && lvopts.IdleShutdownMethod != IdleShutdownMethod.None)
                {
                    if (!string.IsNullOrEmpty(selfEC2InstanceID))
                    {
                        if ((lvopts.IdleShutdownMethod == IdleShutdownMethod.ScaleToZero ||
                             lvopts.IdleShutdownMethod == IdleShutdownMethod.LogIdleProtected) &&
                            string.IsNullOrEmpty(lvopts.AutoScaleGroup))
                        {
                            throw new Exception("--autoscalegroup required with --idleshutdownmethod=ScaleToZero " +
                                                "or --idleshutdownmethod=LogIdleProtected");
                        }
                        pipeline.LogInfo("will attempt to shutdown after {0} idle, shutdown method {1}",
                                         Fmt.HMS(lvopts.IdleShutdownSec * 1e3), lvopts.IdleShutdownMethod);
                    }
                    else
                    {
                        pipeline.LogWarn("idle shutdown disabled, failed to get EC2 instance ID");
                    }
                }

                int timeoutSec = messageQueue != null ? messageQueue.TimeoutSec : GetDefaultMessageTimeoutSec();
                pipeline.LogInfo("message timeout: {0}", Fmt.HMS(timeoutSec * 1e3));
                pipeline.LogInfo("max handler time: {0}", Fmt.HMS(GetMaxHandlerSec() * 1e3));
                pipeline.LogInfo("max message age: {0}", Fmt.HMS(GetMaxMessageAgeSec() * 1e3));
                
                int mrc = GetMaxReceiveCount();
                pipeline.LogInfo("max receive count: {0}", mrc < int.MaxValue ? ("" + mrc) : "unlimited");
                
                if (lvopts.DeprioritizeRetries)
                {
                    if (!CanDeprioritizeRetries())
                    {
                        throw new Exception("--deprioritizeretries not supported");
                    }
                    else
                    {
                        pipeline.LogInfo("deprioritizing retries by moving failed messages to the back of the queue");
                    }
                }

                if (lvopts.DropPoisonMessages)
                {
                    pipeline.LogInfo("dropping poison messages without retry");
                }
            }

            return true;
        }

        protected override void RefreshCredentials()
        {
            base.RefreshCredentials();

            if (messageQueue != null)
            {
                messageQueue.Dispose();
                messageQueue = GetMessageQueue();
            }

            if (failMessageQueue != null)
            {
                failMessageQueue.Dispose();
                failMessageQueue = GetFailMessageQueue();
            }
        }

        protected virtual bool IsService()
        {
            return lvopts.Service;
        }

        protected abstract void RunBatch();

        protected virtual QueueMessage DequeueOneMessage(MessageQueue queue, int overrideVisibilityTimeout = -1)
        {
            int ovt = overrideVisibilityTimeout;
            QueueMessage qm = null, aqm = null;
            Func<string, QueueMessage> oh = msg => { aqm = AlternateMessageHandler(msg); return aqm; };
            switch (lvopts.MessageType)
            {
                case MessageType.Generic:
                    qm = queue.DequeueOne<GenericMessage>(overrideVisibilityTimeout: ovt, altHandler: oh); break;
                case MessageType.S3Event:
                    qm = queue.DequeueOne<S3EventMessage>(overrideVisibilityTimeout: ovt, altHandler: oh); break;
                case MessageType.SNSWrappedS3Event:
                    qm = queue.DequeueOne<SNSMessageWrapper>(overrideVisibilityTimeout: ovt, altHandler: oh); break;
                default: throw new ArgumentException("unhandled messsage type " + lvopts.MessageType);
            }
            return qm ?? aqm;
        }

        /// <summary>
        /// Used only by SendMessage().
        /// </summary>
        protected virtual QueueMessage ParseMessage(string json)
        {
            switch (lvopts.MessageType)
            {
                case MessageType.Generic: return JsonHelper.FromJson<GenericMessage>(json, autoTypes: false);
                case MessageType.S3Event: return JsonHelper.FromJson<S3EventMessage>(json, autoTypes: false);
                case MessageType.SNSWrappedS3Event:
                    return JsonHelper.FromJson<SNSMessageWrapper>(json, autoTypes: false);
                default: throw new ArgumentException("unhandled messsage type " + lvopts.MessageType);
            }
        }

        protected virtual string GetUrlFromMessage(QueueMessage msg)
        {
            if (msg is GenericMessage)
            {
                return (msg as GenericMessage).url;
            }
            else if (msg is S3EventMessage)
            {
                return S3EventMessage.GetUrl(msg as S3EventMessage, "ObjectCreated");
            }
            else if (msg is SNSMessageWrapper)
            {
                return S3EventMessage.GetUrl(msg as SNSMessageWrapper, "ObjectCreated");
            }
            else
            {
                throw new Exception("cannot get URL, unhandled queue message type " + msg.GetType().Name);
            }
        }
            
        /// <summary>
        /// Should not throw.  
        /// </summary>
        protected virtual string DescribeMessage(QueueMessage msg, bool verbose = false)
        {
            try
            {
                return GetUrlFromMessage(msg);
            }
            catch
            {
                return "unknown message type " + msg.GetType().Name;
            }
        }

        /// <summary>
        /// Should not throw.  
        /// </summary>
        protected abstract bool AcceptMessage(QueueMessage msg, out string reason);

        /// <summary>
        /// Can throw.  
        /// </summary>
        protected abstract bool HandleMessage(QueueMessage msg);

        /// <summary>
        /// Optional hook to handle unusual messages.
        /// Can throw.  
        /// </summary>
        protected virtual QueueMessage AlternateMessageHandler(string msg)
        {
            return null;
        }

        //Filter out some subfolders on S3.
        protected virtual bool AcceptBucketPath(string url, bool allowInternal = false)
        {
            url = url.ToLower();
            return
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/1110
                (allowInternal || !url.Contains("/ids-pipeline/")) &&
                !url.Contains("/rdr/browse/") &&
                !url.Contains("/rdr/mosaic/") &&
                !url.Contains("/rdr/mesh/") &&
                !url.Contains("/rdr/tileset/");
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
            return lvopts.MaxHandlerSec > 0 ? lvopts.MaxHandlerSec : defMaxHandlerSec;
        }

        /// <summary>
        /// Messages that keep being received longer than this many seconds
        /// since the first time they are received by any worker
        /// (e.g. because they keep failing to be processed)
        /// will be culled from the queue.
        /// </summary>
        protected virtual int GetMaxMessageAgeSec()
        {
            return lvopts.MaxMessageAgeSec > 0 ? lvopts.MaxMessageAgeSec : defMaxMessageAgeSec;
        }

        protected virtual int GetMaxReceiveCount()
        {
            return lvopts.MaxReceiveCount > 0 ? lvopts.MaxReceiveCount : int.MaxValue;
        }

        protected virtual bool CanDeprioritizeRetries()
        {
            return false;
        }

        protected virtual QueueMessage MakeRecycledMessage(QueueMessage msg)
        {
            throw new NotImplementedException("cannot make recycled messages");
        }

        protected virtual double GetFirstReceiveMS(QueueMessage msg)
        {
            return msg.ApproxFirstReceiveMS;
        }

        protected virtual int GetNumReceives(QueueMessage msg)
        {
            return msg.ApproxReceiveCount;
        }

        protected virtual int GetDequeueThrottleMS()
        {
            return DEF_DEQUEUE_THROTTLE_MS;
        }

        protected virtual double GetHeartbeatRelPeriod()
        {
            return DEF_HEARTBEAT_REL_PERIOD;
        }

        protected virtual MessageQueue GetMessageQueue()
        {
            return GetMessageQueue(lvopts.QueueName, GetDefaultMessageTimeoutSec(), lvopts.LandformOwnedQueue,
                                   "message");
        }

        protected virtual MessageQueue GetFailMessageQueue()
        {
            string name = lvopts.FailQueueName;
            if (string.IsNullOrEmpty(name) || name.ToLower() == "none")
            {
                return null;
            }
            if (name.ToLower() == "auto")
            {
                name = lvopts.QueueName;
                if (name.EndsWith(".fifo"))
                {
                    name = name.Substring(0, name.Length - 5);
                }
                name += "-fail";
            }
            return GetMessageQueue(name, GetDefaultMessageTimeoutSec(), lvopts.LandformOwnedFailQueue, "fail message");
        }

        protected MessageQueue GetMessageQueue(string name, int defTimeoutSec, bool landformOwned, string what)
        {
            if (string.IsNullOrEmpty(name))
            {
                pipeline.LogInfo("no {0} queue", what);
                return null;
            }
            pipeline.LogInfo("opening/creating {0} queue: {1} ({2}landform owned)",
                             what, name, landformOwned ? "" : "not ");
            bool autoCreateIfLandformOwned = !lvopts.DeleteQueues;
            MessageQueue queue = null;
            while (true)
            {
                try
                {
                    string profile = lvopts.UseDefaultAWSProfileForSQSClient ? null : awsProfile;
                    bool autoTypes = false;
                    queue = new MessageQueue(name, profile, awsRegion, defTimeoutSec, pipeline, lvopts.Quiet,
                                             landformOwned, autoTypes, autoCreateIfLandformOwned);
                    pipeline.LogInfo("{0} queue {1}: default timeout {2}s, actual timeout {3}s",
                                     what, name, defTimeoutSec, queue.TimeoutSec);
                    break;
                }
                catch (Exception ex)
                {
                    if (landformOwned && !autoCreateIfLandformOwned && (ex is QueueDoesNotExistException))
                    {
                        return null;
                    }
                    pipeline.LogException(ex, string.Format("opening/creating {0} queue {1}, retrying in {2}",
                                                            what, name, Fmt.HMS(SERVICE_LOOP_THROTTLE_SEC * 1e3)));
                    SleepSec(SERVICE_LOOP_THROTTLE_SEC);
                }
            }
            return queue;
        }

        //uses SQS, called only in service util mode so does not need to hold credentialRefreshLock
        protected virtual void SendMessage()
        {
            pipeline.LogInfo("{0}sending message to queue {1}", lvopts.DryRun ? "dry " : "", messageQueue.Name);
            if (!lvopts.DryRun)
            {
                messageQueue.Enqueue(lvopts.SendMessage.IndexOf("://") >= 0 ? new GenericMessage(lvopts.SendMessage)
                                     : ParseMessage(File.ReadAllText(lvopts.SendMessage)));
            }
        }

        //uses SQS, called only in service util mode so does not need to hold credentialRefreshLock
        private void PeekMessagesImpl(MessageQueue queue, int max)
        {
            pipeline.LogInfo("peeking up to {0} messages from {1}", max, queue.Name);
            int num = 0;
            for (int i = 0; i < max; i++)
            {
                try
                {
                    QueueMessage msg = DequeueOneMessage(queue, overrideVisibilityTimeout: 1);
                    if (msg == null) break;
                    num++;
                    pipeline.LogInfo("message {0}: {1}", num, DescribeMessage(msg, verbose: true));
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex);
                    break;
                }
            }
        }

        //uses SQS, called only in service util mode so does not need to hold credentialRefreshLock
        private void PeekMessages()
        {
            PeekMessagesImpl(messageQueue, max: lvopts.PeekMessages);
        }

        //uses SQS, called only in service util mode so does not need to hold credentialRefreshLock
        private void PeekFailedMessages()
        {
            PeekMessagesImpl(failMessageQueue, max: lvopts.PeekFailedMessages);
        }

        //uses SQS, called only in service util mode so does not need to hold credentialRefreshLock
        private void MoveOrDropMessages(MessageQueue fromQueue, MessageQueue toQueue, int max)
        {
            if (toQueue != null)
            {
                pipeline.LogInfo("moving up to {0} messages from {1} to {2}", max, fromQueue.Name, toQueue.Name);
            }
            else
            {
                pipeline.LogInfo("dropping up to {0} messages from {1}", max, fromQueue.Name);
            }
            int num = 0;
            for (int i = 0; i < max; i++)
            {
                try
                {
                    QueueMessage msg = DequeueOneMessage(fromQueue);
                    if (msg == null) break;
                    fromQueue.DeleteMessage(msg);
                    if (toQueue != null)
                    {
                        toQueue.Enqueue(msg);
                    }
                    else
                    {
                        pipeline.LogInfo("dropped message: {0}", DescribeMessage(msg, verbose: true));
                    }
                    num++;
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex);
                    break;
                }
            }
            if (toQueue != null)
            {
                pipeline.LogInfo("moved {0} messages from {1} to {2}", num, fromQueue.Name, toQueue.Name);
            }
            else
            {
                pipeline.LogInfo("dropped {0} messages from {1}", num, fromQueue.Name);
            }
        }

        //uses SQS, called only in service util mode so does not need to hold credentialRefreshLock
        private void RetryMessages()
        {
            MoveOrDropMessages(fromQueue: failMessageQueue, toQueue: messageQueue, max: lvopts.RetryMessages);
        }

        //uses SQS, called only in service util mode so does not need to hold credentialRefreshLock
        private void FailMessages()
        {
            MoveOrDropMessages(fromQueue: messageQueue, toQueue: failMessageQueue, max: lvopts.FailMessages);
        }

        //uses SQS, called only in service util mode so does not need to hold credentialRefreshLock
        private void DropMessages()
        {
            MoveOrDropMessages(fromQueue: messageQueue, toQueue: null, max: lvopts.DropMessages);
        }
            
        //uses SQS, called only in service util mode so does not need to hold credentialRefreshLock
        private void DropFailedMessages()
        {
            MoveOrDropMessages(fromQueue: failMessageQueue, toQueue: null, max: lvopts.DropFailedMessages);
        }

        //uses SQS, called only in service util mode so does not need to hold credentialRefreshLock
        protected void DeleteQueue(MessageQueue queue, string what)
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

        //uses SQS, called only in service util mode so does not need to hold credentialRefreshLock
        protected virtual void DeleteQueues()
        {
            DeleteQueue(messageQueue, "message");
            DeleteQueue(failMessageQueue, "fail");
        }

        protected override string GetPID()
        {
            return !string.IsNullOrEmpty(selfEC2InstanceID) ? selfEC2InstanceID : base.GetPID();
        }

        //uses EC2, called only by ServiceLoop() so does not need to hold credentialRefreshLock
        private void IdleShutdown()
        {
            if (lvopts.IdleShutdownMethod == IdleShutdownMethod.None || lvopts.IdleShutdownSec <= 0 ||
                string.IsNullOrEmpty(selfEC2InstanceID) || shuttingDown)
            {
                return;
            }

            if (lvopts.IdleShutdownMethod == IdleShutdownMethod.LogIdle)
            {
                pipeline.LogInfo("{0} for instance {1}{2}", LOG_IDLE_MSG, selfEC2InstanceID,
                                 !string.IsNullOrEmpty(lvopts.AutoScaleGroup) ?
                                 (" in ASG " + lvopts.AutoScaleGroup) : "");
                shuttingDown = true;
                return;
            }

            if (lvopts.IdleShutdownMethod == IdleShutdownMethod.LogIdleProtected)
            {
                try
                {
                    pipeline.LogInfo("disabling scale-in protection for instance {0} (self) in ASG {1}",
                                     selfEC2InstanceID, lvopts.AutoScaleGroup);
                    shuttingDown = computeHelper.SetInstanceProtection(lvopts.AutoScaleGroup, selfEC2InstanceID, false);
                    if (shuttingDown)
                    {
                        pipeline.LogInfo("{0} for instance {1} in ASG {2}",
                                         LOG_IDLE_MSG, selfEC2InstanceID, lvopts.AutoScaleGroup);
                    }
                    else
                    {
                        pipeline.LogError("failed to change scale-in protection");
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, "creating client or disabling scale-in protection " +
                                          $"for instance {selfEC2InstanceID} in ASG {lvopts.AutoScaleGroup}");
                }
                return;
            }

            if (lvopts.IdleShutdownMethod == IdleShutdownMethod.ScaleToZero)
            {
                try
                {
                    pipeline.LogInfo("scaling ASG {0} to zero instances", lvopts.AutoScaleGroup);
                    shuttingDown = computeHelper.SetAutoScalingGroupSize(lvopts.AutoScaleGroup, 0);
                    if (!shuttingDown)
                    {
                        pipeline.LogError("failed to change ASG size");
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, $"creating client or scaling ASG {lvopts.AutoScaleGroup} to zero");
                }
                return;
            }

            if (lvopts.IdleShutdownMethod == IdleShutdownMethod.StopInstance ||
                lvopts.IdleShutdownMethod == IdleShutdownMethod.StopInstanceOrShutdown)
            {
                try
                {
                    pipeline.LogInfo("stopping EC2 instance {0} (self)", selfEC2InstanceID);
                    shuttingDown = computeHelper.StopInstances(selfEC2InstanceID);
                    if (!shuttingDown)
                    {
                        pipeline.LogError("failed to stop EC2 instance {0} (self)", selfEC2InstanceID);
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, $"creating client or stopping instance {selfEC2InstanceID}");
                }
            }

            if (lvopts.IdleShutdownMethod == IdleShutdownMethod.Shutdown ||
                (lvopts.IdleShutdownMethod == IdleShutdownMethod.StopInstanceOrShutdown && !shuttingDown))
            {
                pipeline.LogInfo("requesting OS shutdown");
                shuttingDown = true;
                ConsoleHelper.Shutdown();
            }
        }

        protected virtual void RunService()
        {
            Task.Run(() => HeartbeatLoop());
            Task.Run(() => WatchdogLoop());
            ServiceLoop();
        }

        private void CheckCredentials()
        {
            if (credentialRefreshSec <= 0)
            {
                return;
            }

            double overdueSec = 0;
            if (lastCredentialRefreshSecUTC >= 0)
            {
                double deadline = lastCredentialRefreshSecUTC + credentialRefreshSec;
                overdueSec = UTCTime.Now() - deadline;
            }

            if (overdueSec >= 0)
            {
                if (Monitor.TryEnter(credentialRefreshLock, 5000))
                {
                    try
                    {
                        if (Monitor.TryEnter(longRunningCredentialRefereshLock, 5000))
                        {
                            try
                            {
                                RefreshCredentials(); //let exception propagate
                            }
                            finally
                            {
                                Monitor.Exit(longRunningCredentialRefereshLock);
                            }
                        }
                        else
                        {
                            pipeline.LogWarn("cannot acquire long-running lock to refresh credentials, {0} overdue",
                                             Fmt.HMS(overdueSec * 1e3));
                        }
                    }
                    finally
                    {
                        Monitor.Exit(credentialRefreshLock);
                    }
                }
                else
                {
                    pipeline.LogWarn("cannot acquire lock to refresh credentials, {0} overdue",
                                     Fmt.HMS(overdueSec * 1e3));
                }
            }
        }

        private void ServiceLoop()
        {
            int throttleMS = GetDequeueThrottleMS();
            int maxAgeSec = GetMaxMessageAgeSec();
            int maxReceiveCount = GetMaxReceiveCount();
            pipeline.LogInfo("running service loop on queue {0}, throttle {1}ms", messageQueue.Name, throttleMS);

            if (!string.IsNullOrEmpty(selfEC2InstanceID) && lvopts.IdleShutdownSec > 0 &&
                lvopts.IdleShutdownMethod == IdleShutdownMethod.LogIdleProtected)
            {
                try
                {
                    //the ASG should probably be configured to launch the instance with scale-in protection enabled
                    //that would avoid a race condition where the instance could get scheduled for termination
                    //sometime between when it was booted and now (which can be like 5 min)
                    pipeline.LogInfo("enabling scale-in protection for instance {0} (self) in ASG {1}",
                                     selfEC2InstanceID, lvopts.AutoScaleGroup);
                    if (!computeHelper.SetInstanceProtection(lvopts.AutoScaleGroup, selfEC2InstanceID, true))
                    {
                        pipeline.LogError("failed to change scale-in protection");
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, "creating client or enabling scale-in protection " +
                                          $"for instance {selfEC2InstanceID} in ASG {lvopts.AutoScaleGroup}");
                }
            }

            while (!abort) //shuttingDown is handled below
            {
                try
                {
                    double startSec = UTCTime.Now();

                    CheckCredentials();
                    
                    QueueMessage msg = !shuttingDown ? DequeueOneMessage(messageQueue) : null;

                    if (msg != null)
                    {
                        idleStartTime = -1;

                        string desc = DescribeMessage(msg);
                        int ageSec = (int)(0.001 * (msg.ApproxReceiveMS - GetFirstReceiveMS(msg)));
                        int totalAgeSec = (int)(0.001 * (msg.ApproxReceiveMS - msg.SentMS));
                        int receiveCount = GetNumReceives(msg);
                        bool tooOld = ageSec > maxAgeSec || receiveCount > maxReceiveCount;
                        bool accepted = AcceptMessage(msg, out string rejectionReason);
                        bool handled = false;
                        bool recycle = false;
                        bool drop = false;
                        bool deleted = false;

                        if (accepted && !tooOld)
                        {
                            StartStopwatch();
                            
                            currentMessage = msg;
                            messageStartSec = (uint)UTCTime.Now();
                            
                            pipeline.LogInfo("processing {0} (age {1}, {2} since first receive, {3} receives)", desc,
                                             Fmt.HMS(totalAgeSec * 1e3), Fmt.HMS(ageSec * 1e3), receiveCount);

                            try
                            {
                                handled = HandleMessage(msg);
                            }
                            catch (Exception msgException)
                            {
                                drop = killedCurrentHandler && lvopts.DropPoisonMessages;
                                recycle = !drop && lvopts.DeprioritizeRetries;
                                pipeline.LogException(msgException, "handling " + desc);
                            }

                            killedCurrentHandler = false;

                            try
                            {
                                lock (deleteMessageLock)
                                {
                                    //the reason we hold deleteMessageLock here is to make sure that
                                    //the call to UpdateTimeout() in HeartbeatLoop() can't overlap with this
                                    currentMessage = null;
                                    if (handled || drop || recycle)
                                    {
                                        messageQueue.DeleteMessage(msg);
                                        deleted = true;
                                    }
                                }
                            }
                            catch (Exception deleteException)
                            {
                                pipeline.LogException(deleteException, "deleting message");
                            }

                            StopStopwatch(brief: true);
                        }
                        else //not accepted or too old
                        {
                            try
                            {
                                lock (deleteMessageLock)
                                {
                                    messageQueue.DeleteMessage(msg);
                                    deleted = true;
                                }
                            }
                            catch (Exception deleteException)
                            {
                                pipeline.LogException(deleteException, "deleting message");
                            }
                        }

                        if (drop)
                        {
                            pipeline.LogError("dropping poison message {0}", desc);
                        }

                        if (accepted && tooOld)
                        {
                            string reason = ageSec > maxAgeSec ?
                                string.Format("too old {0} > {1}", Fmt.HMS(ageSec * 1e3), Fmt.HMS(maxAgeSec * 1e3)) :
                                string.Format("too many retries {0} > {1}", receiveCount, maxReceiveCount);
                            pipeline.LogError("{0} {1}, removing from queue, {2} fail queue", desc, reason,
                                              failMessageQueue != null ? "adding to" : "no");
                        }

                        if (!accepted && !string.IsNullOrEmpty(rejectionReason))
                        {
                            pipeline.LogVerbose("rejected message: {0}", rejectionReason);
                        }

                        if (recycle && deleted)
                        {
                            try
                            {
                                messageQueue.Enqueue(MakeRecycledMessage(msg));
                            }
                            catch (Exception recycleException)
                            {
                                pipeline.LogException(recycleException, "recycling message to end of queue");
                            }
                        }
                        else if (accepted && !handled && deleted && (failMessageQueue != null))
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
                    else // no messages available or shutting down
                    {
                        double now = UTCTime.Now();
                        if (idleStartTime < 0)
                        {
                            idleStartTime = now;
                            lastIdleEventTime = -1;
                        }
                        else if (!string.IsNullOrEmpty(selfEC2InstanceID) && lvopts.IdleShutdownSec > 0 &&
                                 lvopts.IdleShutdownMethod != IdleShutdownMethod.None)
                        {
                            double idleSec = now - idleStartTime;
                            if (idleSec > lvopts.IdleShutdownSec &&
                                (lastIdleEventTime < 0 || (now - lastIdleEventTime > IDLE_EVENT_THROTTLE_SEC)))
                            {
                                if (!shuttingDown)
                                {
                                    pipeline.LogInfo("shutting down EC2 instance {0} (self), idle for {1} >= {2}, " +
                                                     "shutdown method {3}",
                                                     selfEC2InstanceID, Fmt.HMS(idleSec * 1e3),
                                                     Fmt.HMS(lvopts.IdleShutdownSec * 1e3), lvopts.IdleShutdownMethod);
                                    IdleShutdown();
                                }
                                else
                                {
                                    //important to prevent zombie instances that prevent other instances from spawning
                                    //repeat LOG_IDLE_MSG so ASG scale down is retriggered until instance terminated
                                    pipeline.LogWarn("{0} for instance {1}{2}, " +
                                                     "idle for {3} >= {4} but still running",
                                                     LOG_IDLE_MSG, selfEC2InstanceID,
                                                     !string.IsNullOrEmpty(lvopts.AutoScaleGroup) ?
                                                     (" in ASG " + lvopts.AutoScaleGroup) : "",
                                                     Fmt.HMS(idleSec * 1e3), Fmt.HMS(lvopts.IdleShutdownSec * 1e3));
                                }
                                lastIdleEventTime = now;
                            }
                        }
                    }

                    double elapsedSec = UTCTime.Now() - startSec;
                    SleepSec((0.001 * throttleMS) - elapsedSec);
                }
                catch (Exception serviceException)
                {
                    pipeline.LogException(serviceException, string.Format("service loop error, throttling {0}",
                                                                          Fmt.HMS(SERVICE_LOOP_THROTTLE_SEC * 1e3)));
                    SleepSec(SERVICE_LOOP_THROTTLE_SEC);
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
                             targetPeriod, timeoutSec, Fmt.HMS(maxHandlerSec * 1e3));
            while (!abort) //keep going while shuttingDown
            {
                if (currentMessage != null)
                {
                    double totalSec = UTCTime.Now() - messageStartSec;
                    if (totalSec > maxHandlerSec)
                    {
                        pipeline.LogError("handler has run for {0} > {1}, killing",
                                          Fmt.HMS(totalSec * 1e3), Fmt.HMS(maxHandlerSec * 1e3));
                        killedCurrentHandler = true;
                        KillCurrentCommand(); //swallows exceptions, but handler will throw exception if killed
                        SleepSec(targetPeriod);
                        lastHeartbeatSec = -1;
                    }
                    else //still processing message, increase SQS visiblity timeout
                    {
                        try
                        {
                            if (lastHeartbeatSec >= 0)
                            {
                                //try to maintain heartbeat period proportional to queue timout
                                SleepSec(targetPeriod - (UTCTime.Now() - lastHeartbeatSec)); //ignores negative
                            }

                            lock (credentialRefreshLock)
                            {
                                //specifically using two locks here, see
                                //https://github.jpl.nasa.gov/OnSight/Landform/issues/1120
                                //acquistion order to avoid deadlock: 1) credentialRefreshLock, 2) deleteMessageLock
                                lock (deleteMessageLock)
                                {
                                    //message may have finished processing while we were waiting
                                    if (currentMessage != null)
                                    {
                                        messageQueue.UpdateTimeout(currentMessage, timeoutSec);
                                    }
                                }
                            }
                            
                            //upper bound on time between visibility update
                            double heartbeatPeriod = lastHeartbeatSec >= 0 ? (UTCTime.Now() - lastHeartbeatSec) : -1;

                            lastHeartbeatSec = UTCTime.Now();

                            if (heartbeatPeriod > timeoutSec)
                            {
                                pipeline.LogError("heartbeat {0} exceeded visibility timeout {1}",
                                                  Fmt.HMS(heartbeatPeriod * 1e3), Fmt.HMS(timeoutSec * 1e3));
                            }
                        }
                        catch (Exception ex)
                        {
                            pipeline.LogException(ex, "updating message timeout");
                            SleepSec(targetPeriod);
                        }
                    }
                }
                else //no current message
                {
                    SleepSec(targetPeriod);
                    lastHeartbeatSec = -1;
                }
            }
        }

        protected string GetWatchdogStats()
        {
            lock (watchdogStatsLock)
            {
                var sb = new StringBuilder();
                if (minFreeMemory >= 0)
                {
                    sb.Append(string.Format("watchdog period {0}, min {1}/{2} free at {3}",
                                            lvopts.WatchdogPeriod, Fmt.Bytes(minFreeMemory), Fmt.Bytes(totalMemory),
                                            minFreeMemoryTime.Value));
                    if (numWatchdogWarns > 0)
                    {
                        sb.Append(string.Format(", {0} warnings (< {1} free)",
                                                numWatchdogWarns, Fmt.Bytes(watchdogWarnGB)));
                    }
                    if (numWatchdogCollects > 0)
                    {
                        sb.Append(string.Format(", {0} collects (< {1} free)",
                                                numWatchdogCollects, Fmt.Bytes(watchdogActionGB)));
                    }
                    if (numWatchdogErrors > 0)
                    {
                        sb.Append(string.Format(", {0} critical (< {1} free)",
                                                numWatchdogErrors, Fmt.Bytes(watchdogAbortGB)));
                    }
                }
                return sb.ToString();
            }
        }

        protected void ResetWatchdogStats()
        {
            lock (watchdogStatsLock)
            {
                minFreeMemory = -1;
                numWatchdogWarns = -1;
                numWatchdogCollects = -1;
                numWatchdogErrors = -1;
                minFreeMemoryTime = null;
            }
        }

        protected override void DumpExtraStats()
        {
            string stats = GetWatchdogStats();
            if (!string.IsNullOrEmpty(stats))
            {
                pipeline.LogInfo("memory watchdog: " + stats);
            }
        }

        protected void WatchdogLoop()
        {
            //warn and possibly abort if memory runs low
            //on production servers if the application uses too much virtual memory
            //important services like CloudWatch can permanently crash (!)
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/1221

            double targetPeriod = lvopts.WatchdogPeriod;
            if (targetPeriod <= 0)
            {
                pipeline.LogInfo("watchdog disabled");
                return;
            }

            totalMemory = ConsoleHelper.GetTotalSystemVirtualMemory();
            double freeMemory = ConsoleHelper.GetFreeSystemVirtualMemory();
            double lastThreshold = -1;
            if (totalMemory > 0 && freeMemory > 0)
            {
                double getThreshold(double opt)
                {
                    double ret = -1;
                    double gb = 1024L * 1024L * 1024L;
                    if (opt < 0)
                    {
                        ret = 0;
                    }
                    else if (opt < 1)
                    {
                        ret = opt * totalMemory;
                    }
                    else if (totalMemory >= (DEF_WATCHDOG_TOTAL_GB * gb))
                    {
                        ret = opt * gb;
                    }
                    else
                    {
                        ret = (opt / DEF_WATCHDOG_TOTAL_GB) * totalMemory;
                    }
                    if (lastThreshold > 0)
                    {
                        ret = Math.Min(lastThreshold, ret);
                    }
                    ret = Math.Min(totalMemory, ret);
                    if (ret > 0)
                    {
                        lastThreshold = ret;
                    }
                    return ret;
                }
                watchdogWarnGB = getThreshold(lvopts.WatchdogWarnGB);
                watchdogActionGB = getThreshold(lvopts.WatchdogActionGB);
                watchdogAbortGB = getThreshold(lvopts.WatchdogAbortGB);
                if (watchdogWarnGB <= 0 && watchdogActionGB <= 0 && watchdogAbortGB <= 0)
                {
                    pipeline.LogInfo("memory watchdog disabled, all thresholds unset");
                }
                else
                {
                    pipeline.LogInfo("running memory watchdog, period {0}, " +
                                     "{1}/{2} free, warn level {3} free, cleanup {4} abort {5}",
                                     Fmt.HMS(targetPeriod * 1e3), Fmt.Bytes(freeMemory), Fmt.Bytes(totalMemory),
                                     Fmt.Bytes(watchdogWarnGB), Fmt.Bytes(watchdogActionGB),
                                     Fmt.Bytes(watchdogAbortGB));
                }
            }
            else
            {
                pipeline.LogWarn("memory watchdog disabled, error getting system virtual memory stats");
            }

            var procNames = new List<string>();
            var procCmds = new List<string>();
            var procCounters = new List<int>();
            string ssmProcess =
                (lvopts.WatchdogSSMProcess == "mission") ? mission.GetSSMProcess() : lvopts.WatchdogSSMProcess;
            string ssmCommand =
                (lvopts.WatchdogSSMCommand == "mission") ? mission.GetSSMCommand() : lvopts.WatchdogSSMCommand;
            bool ssmEnabled = !string.IsNullOrEmpty(ssmProcess) && !string.IsNullOrEmpty(ssmCommand);
            if (ssmEnabled)
            {
                procNames.Add(ssmProcess);
                procCmds.Add(ssmCommand);
                procCounters.Add(WATCHDOG_PROCESS_RESTART_PERIODS);
                pipeline.LogInfo("running SSM watchdog, period {0}, process: {1}, command: {2}",
                                 Fmt.HMS(targetPeriod * 1e3), ssmProcess, ssmCommand);
            }
            else
            {
                pipeline.LogInfo("SSM watchdog disabled");
            }

            string cwProcess =(lvopts.WatchdogCloudWatchProcess == "mission") ? mission.GetCloudWatchProcess() :
                lvopts.WatchdogCloudWatchProcess;
            string cwCommand = (lvopts.WatchdogCloudWatchCommand == "mission") ? mission.GetCloudWatchCommand() :
                lvopts.WatchdogCloudWatchCommand;
            bool cwEnabled = !string.IsNullOrEmpty(cwProcess) && !string.IsNullOrEmpty(cwCommand);
            if (cwEnabled)
            {
                procNames.Add(cwProcess);
                procCmds.Add(cwCommand);
                procCounters.Add(WATCHDOG_PROCESS_RESTART_PERIODS);
                pipeline.LogInfo("running CloudWatch watchdog, period {0}, process: {1}, command: {2}",
                                 Fmt.HMS(targetPeriod * 1e3), cwProcess, cwCommand);
            }
            else
            {
                pipeline.LogInfo("CloudWatch watchdog disabled");
            }

            string[] processName = procNames.ToArray();
            string[] processCommand = procCmds.ToArray();
            int[] processCounter = procCounters.ToArray();
            bool[] processEverRan = new bool[processName.Length];
            if (processName.Length > 0)
            {
                pipeline.LogInfo("running process watchdog for {0} processes: {1}",
                                 processName.Length, String.Join(", ", processName));
            }
            else
            {
                pipeline.LogInfo("not running process watchdog");
            }

            ResetWatchdogStats();

            bool memWarned = false, procWarned = false;
            int abortCounter = WATCHDOG_ABORT_PERIODS;
            var leaks = lvopts.WatchdogLeakTest > 0 ? new List<byte[]>() : null;
            while (!abort) //keep going while shuttingDown
            {
                SleepSec(targetPeriod);

                if (lvopts.WatchdogLeakTest > 0)
                {
                    pipeline.LogWarn("TESTING MEMORY LEAK, allocating {0}GB", lvopts.WatchdogLeakTest);
                    for (int i = 0; i < lvopts.WatchdogLeakTest; i++)
                    {
                        try
                        {
                            var leak = new byte[1024L * 1024L * 1024L];
                            for (int j = 0; j < leak.Length; j++)
                            {
                                leak[j] = (byte)(j&0xFF);
                            }
                            leaks.Add(leak);
                        }
                        catch (Exception)
                        {
                            try
                            {
                                pipeline.LogError("failed to allocate");
                            }
                            catch (Exception)
                            {
                                //ignore
                            }
                        }
                    }
                }

                freeMemory = totalMemory > 0 ? ConsoleHelper.GetFreeSystemVirtualMemory() : 0;
                if (totalMemory > 0)
                {
                    pipeline.LogVerbose("{0}/{1} free memory", Fmt.Bytes(freeMemory), Fmt.Bytes(totalMemory));
                }
                if (freeMemory > 0)
                {
                    lock (watchdogStatsLock)
                    {
                        if (minFreeMemory < 0 || freeMemory < minFreeMemory)
                        {
                            minFreeMemory = freeMemory;
                            minFreeMemoryTime = DateTime.Now;
                        }
                    }
                    if (watchdogAbortGB >= 0 && freeMemory < watchdogAbortGB)
                    {
                        abortCounter--;
                        lock (watchdogStatsLock)
                        {
                            numWatchdogErrors++;
                        }
                        if (abortCounter <= 0)
                        {
                            pipeline.LogError("{0} < {1} of {2} free system virtual memory, aborting",
                                              Fmt.Bytes(freeMemory), Fmt.Bytes(watchdogAbortGB),
                                              Fmt.Bytes(totalMemory));
                            abort = true;
                            ConsoleHelper.Exit(WATCHDOG_ABORT_EXIT_CODE);
                        }
                        else
                        {
                            pipeline.LogWarn("{0} < {1} of {2} free system virtual memory, " +
                                             "aborting in {3}", Fmt.Bytes(freeMemory),
                                             Fmt.Bytes(watchdogAbortGB), Fmt.Bytes(totalMemory),
                                             Fmt.HMS(abortCounter * targetPeriod * 1e3));
                            pipeline.ClearCaches();
                            ConsoleHelper.GC();
                        }
                    }
                    else
                    {
                        abortCounter = WATCHDOG_ABORT_PERIODS;

                        if (watchdogActionGB >= 0 && freeMemory < watchdogActionGB)
                        {
                            lock (watchdogStatsLock)
                            {
                                numWatchdogCollects++;
                            }
                            pipeline.LogWarn("{0} < {1} of {2} free system virtual memory, recovering memory",
                                             Fmt.Bytes(freeMemory), Fmt.Bytes(watchdogActionGB),
                                             Fmt.Bytes(totalMemory));
                            pipeline.ClearCaches();
                            ConsoleHelper.GC();
                        }
                        else if (watchdogWarnGB >= 0 && freeMemory < watchdogWarnGB)
                        {
                            lock (watchdogStatsLock)
                            {
                                numWatchdogWarns++;
                            }
                            pipeline.LogWarn("{0} < {1} of {2} free system virtual memory",
                                             Fmt.Bytes(freeMemory), Fmt.Bytes(watchdogWarnGB), Fmt.Bytes(totalMemory));
                        }
                    }
                }
                else if (totalMemory > 0 && !memWarned)
                {
                    pipeline.LogWarn("memory watchdog: error getting free system virtual memory");
                    memWarned = true;
                }

                try
                {
                    ILogger logger = lvopts.Verbose ? pipeline : null;
                    bool[] processRunning = ConsoleHelper.CheckProcesses(logger, processName);
                    for (int i = 0; i < processRunning.Length; i++)
                    {
                        if (processRunning[i])
                        {
                            processCounter[i] = WATCHDOG_PROCESS_RESTART_PERIODS;
                            if (!processEverRan[i])
                            {
                                processEverRan[i] = true;
                                pipeline.LogInfo("detected running watchdog process {0}", processName[i]);
                            }
                        }
                        else if (processEverRan[i])
                        {
                            processCounter[i]--;
                            if (processCounter[i] <= 0)
                            {
                                pipeline.LogWarn("watchdog process {0} not running, attempting restart, command: {1}",
                                                 processName[i], processCommand[i]);
                                try
                                {
                                    var pr = new ProgramRunner(processCommand[i], waitForExit: false);
                                    pr.Run(p => pipeline.LogInfo("restarted process {0}, PID {1}",
                                                                 processName[i], p.Id));
                                }
                                catch (Exception ex)
                                {
                                    pipeline.LogException(ex, $"running {processCommand[i]}");
                                }
                                processCounter[i] = WATCHDOG_PROCESS_RESTART_PERIODS;
                            }
                            else
                            {
                                pipeline.LogWarn("watchdog process {0} not running, will attempt restart in {1}",
                                                 processName[i], Fmt.HMS(processCounter[i] * targetPeriod * 1e3));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!procWarned)
                    {
                        pipeline.LogException(ex, "in watchdog for processes: " + string.Join(", ", processName));
                        procWarned = true;
                    }
                }
            }
        }
    }
}
