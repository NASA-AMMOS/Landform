using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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
    }
    
    public abstract class LandformService : LandformShell
    {
        public const double DEF_HEARTBEAT_REL_PERIOD = 0.333;

        public const int DEF_DEQUEUE_THROTTLE_MS = 1;

        public const int SERVICE_LOOP_RETRY_SEC = 60;

        public const int DEF_MAX_HANDLER_SEC = 10 * 60; //10 minutes
        public const int DEF_MAX_MESSAGE_AGE_SEC = 60 * 60; //1 hour

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
        /// Other threads which require credentials should hold credentialRefreshLock (only) while needed.  Not to avoid
        /// concurrent use of credentials, which is totally fine (and even necessary e.g. for HeartbeatLoop()), but to
        /// prevent RefreshCredentials() from being called while the credentials may be in use.
        ///
        /// For example
        /// * HeartbeatLoop() acquires credentials when it needs to update SQS message timeouts.
        /// * ProcessContextual.MasterLoop() acquires credentials while it may use PLACES.
        /// </summary>
        protected object credentialRefreshLock = new Object();

        /// <summary>
        /// ServiceLoop() acquires deleteMessageLock while deleting messages from the SQS queue.
        /// HeartbeatLoop() also acquires it while updating the message timeout.
        /// This avoids overlaps between deleting the message and updating its timeout.
        /// HeartbeatLoop() actually needs both credentialRefreshLock and deleteMessageLock while updating the timeout,
        /// but that's OK.  It's the only thing that should acquire both at the same time.  Should anything else also
        /// ever need to acquire both at the same time, the order must be 1) credentialRefreshLock; 2) deleteMessageLock
        /// else deadlock can occur.
        /// </summary>
        private object deleteMessageLock = new Object();

        private QueueMessage currentMessage;
        private double messageStartSec;

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
                else if (serviceMode)
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

            if (!serviceMode)
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

            lvopts.LandformOwnedQueue |= lvopts.LandformOwnedQueues;
            lvopts.LandformOwnedFailQueue |= lvopts.LandformOwnedQueues;

            bool sendMessage = !string.IsNullOrEmpty(lvopts.SendMessage);
            bool peekMessages = lvopts.PeekMessages > 0;
            bool peekFailedMessages = lvopts.PeekFailedMessages > 0;
            bool retryMessages = lvopts.RetryMessages > 0;
            bool failMessages = lvopts.FailMessages > 0;
            bool dropMessages = lvopts.DropMessages > 0;
            bool dropFailedMessages = lvopts.DropFailedMessages > 0;

            string utils = "--peekmessages, --peekfailedmessages, --deletequeues, --sendmessage, --retrymessages, " +
                "--failmessages, --dropmessages, --dropfailedmessages";

            var utilOpts = new bool[] { lvopts.DeleteQueues, sendMessage, peekMessages, peekFailedMessages,
                                        retryMessages, failMessages, dropMessages, dropFailedMessages };
            serviceUtilMode = utilOpts.Any(o => o);
            serviceMode = IsService();

            if (serviceMode && serviceUtilMode)
            {
                throw new Exception(utils + ", and --service are mutually exclusive");
            }
            if (serviceMode || serviceUtilMode)
            {
                if (!string.IsNullOrEmpty(lvopts.ProjectName))
                {
                    throw new Exception("project name must be omitted with " + utils);
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

            int timeoutSec = messageQueue != null ? messageQueue.TimeoutSec : GetDefaultMessageTimeoutSec();
            pipeline.LogInfo("message timeout: {0}", Fmt.HMS(timeoutSec * 1000));
            pipeline.LogInfo("max handler time: {0}", Fmt.HMS(GetMaxHandlerSec() * 1000));
            pipeline.LogInfo("max message age: {0}", Fmt.HMS(GetMaxMessageAgeSec() * 1000));

            int mrc = GetMaxReceiveCount();
            pipeline.LogInfo("max receive count: {0}", mrc < int.MaxValue ? ("" + mrc) : "unlimited");

            return true;
        }

        protected override void RefreshCredentials()
        {
            base.RefreshCredentials();

            if (messageQueue != null)
            {
                messageQueue = GetMessageQueue();
            }

            if (failMessageQueue != null)
            {
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
            switch (lvopts.MessageType)
            {
                case MessageType.Generic: return queue.DequeueOne<GenericMessage>(overrideVisibilityTimeout: ovt);
                case MessageType.S3Event: return queue.DequeueOne<S3EventMessage>(overrideVisibilityTimeout: ovt);
                case MessageType.SNSWrappedS3Event:
                    return queue.DequeueOne<SNSMessageWrapper>(overrideVisibilityTimeout: ovt);
                default: throw new ArgumentException("unhandled messsage type " + lvopts.MessageType);
            }
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
                name = lvopts.QueueName + "-fail";
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
                    bool autoTypes = false;
                    queue = new MessageQueue(name, awsProfile, awsRegion, defTimeoutSec, pipeline, lvopts.Quiet,
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
                messageQueue.Enqueue(lvopts.SendMessage.IndexOf("://") >= 0 ? new GenericMessage(lvopts.SendMessage)
                                     : ParseMessage(File.ReadAllText(lvopts.SendMessage)));
            }
        }

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

        private void PeekMessages()
        {
            PeekMessagesImpl(messageQueue, max: lvopts.PeekMessages);
        }

        private void PeekFailedMessages()
        {
            PeekMessagesImpl(failMessageQueue, max: lvopts.PeekFailedMessages);
        }

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

        private void RetryMessages()
        {
            MoveOrDropMessages(fromQueue: failMessageQueue, toQueue: messageQueue, max: lvopts.RetryMessages);
        }

        private void FailMessages()
        {
            MoveOrDropMessages(fromQueue: messageQueue, toQueue: failMessageQueue, max: lvopts.FailMessages);
        }

        private void DropMessages()
        {
            MoveOrDropMessages(fromQueue: messageQueue, toQueue: null, max: lvopts.DropMessages);
        }
            
        private void DropFailedMessages()
        {
            MoveOrDropMessages(fromQueue: failMessageQueue, toQueue: null, max: lvopts.DropFailedMessages);
        }

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

        protected virtual void DeleteQueues()
        {
            DeleteQueue(messageQueue, "message");
            DeleteQueue(failMessageQueue, "fail");
        }

        protected virtual void RunService()
        {
            Task.Run(() => HeartbeatLoop());
            ServiceLoop();
        }

        private void ServiceLoop()
        {
            int throttleMS = GetDequeueThrottleMS();
            int maxAgeSec = GetMaxMessageAgeSec();
            int maxReceiveCount = GetMaxReceiveCount();
            pipeline.LogInfo("running service loop on queue {0}, throttle {1}ms", messageQueue.Name, throttleMS);

            while (true)
            {
                try
                {
                    if (credentialRefreshSec > 0 &&
                        (lastCredentialRefreshSecUTC <= 0 ||
                         (UTCTime.Now() - lastCredentialRefreshSecUTC) > credentialRefreshSec))
                    {
                        lock (credentialRefreshLock)
                        {
                            RefreshCredentials();
                        }
                    }
                    
                    double startSec = UTCTime.Now();
                    QueueMessage msg = DequeueOneMessage(messageQueue);

                    if (msg != null)
                    {
                        string desc = DescribeMessage(msg);
                        int ageSec = (int)(0.001 * (msg.ApproxReceiveMS - msg.ApproxFirstReceiveMS));
                        bool tooOld = ageSec > maxAgeSec || msg.ApproxReceiveCount > maxReceiveCount;
                        bool accepted = AcceptMessage(msg, out string rejectionReason);
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
                                pipeline.LogException(msgException, "error handling " + desc);
                            }
                            
                            currentMessage = null;
                            
                            StopStopwatch(brief: true);
                        }

                        if (accepted && tooOld)
                        {
                            string reason = ageSec > maxAgeSec ?
                                string.Format("too old {0} > {1}", Fmt.HMS(1000 * ageSec), Fmt.HMS(1000 * maxAgeSec)) :
                                string.Format("too many retries {0} > {1}", msg.ApproxReceiveCount, maxReceiveCount);
                            pipeline.LogError("{0} {1}, removing from queue, {2} fail queue", desc, reason,
                                              failMessageQueue != null ? "adding to" : "no");
                        }

                        if (!accepted && !string.IsNullOrEmpty(rejectionReason))
                        {
                            pipeline.LogVerbose("rejected message: {0}", rejectionReason);
                        }

                        if (!accepted || handled || tooOld)
                        {
                            try
                            {
                                lock (deleteMessageLock)
                                {
                                    //the reason we hold deleteMessageLock here is to make sure that
                                    //the call to UpdateTimeout() in HeartbeatLoop() can't overlap with
                                    //this call to DeleteMessage()
                                    messageQueue.DeleteMessage(msg);
                                }
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
                    SleepSec((0.001 * throttleMS) - elapsedSec);
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
                if (currentMessage != null)
                {
                    var totalSec = UTCTime.Now() - messageStartSec;
                    if (totalSec > maxHandlerSec)
                    {
                        pipeline.LogError("handler has run for {0} > {1}, killing",
                                          Fmt.HMS(1000 * totalSec), Fmt.HMS(1000 * maxHandlerSec));
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
                            
                            if (lastHeartbeatSec >= 0)
                            {
                                //upper bound on time between visibility update
                                double heartbeatPeriod = UTCTime.Now() - lastHeartbeatSec;
                                if (heartbeatPeriod > timeoutSec)
                                {
                                    pipeline.LogError("heartbeat {0:F3}s exceeded visibility timeout {1:F3}s",
                                                      heartbeatPeriod, timeoutSec);
                                }
                            }
                            lastHeartbeatSec = UTCTime.Now();
                        }
                        catch (Exception ex)
                        {
                            pipeline.LogException(ex, "error updating message timeout");
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
    }
}
