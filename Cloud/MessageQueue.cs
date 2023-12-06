using System;
using System.Collections.Generic;
using System.Linq;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Newtonsoft.Json;
using JPLOPS.Util;

namespace JPLOPS.Cloud
{
    public class QueueMessage
    {
        [JsonIgnore]
        public string MessageId;

        [JsonIgnore]
        public string ReceiptHandle;

        //approx first time any receiver received this message
        //or -1 if unknown
        //ms since UTC epoch
        [JsonIgnore]
        public double ApproxFirstReceiveMS = -1;

        //approx time we received this message
        //this may be a lower bounds
        //note: other receivers may have received it even later
        //ms since UTC epoch
        [JsonIgnore]
        public double ApproxReceiveMS = -1;

        //time message was sent
        //ms since UTC epoch
        [JsonIgnore]
        public double SentMS = -1;

        //approximate number of times this message has been received
        //this may be a lower bounds
        [JsonIgnore]
        public int ApproxReceiveCount = -1;

        [JsonIgnore]
        public string MessageText;
    }

    public class MessageQueue : IDisposable
    {
        public const int DEF_TIMEOUT_SEC = 30;
        public const String DEF_FIFO_QUEUE_MESSAGE_GROUP_ID = "the_group_id";

        public string Name { get; private set; }
        public int TimeoutSec { get; private set; }
        public bool LandformOwned { get; private set; }

        public string FIFOQueueMessageGroupID = DEF_FIFO_QUEUE_MESSAGE_GROUP_ID;

        private ILogger logger;
        private string url;
        private bool autoTypes;
        private AmazonSQSClient client;

        private bool isFIFO;

        public MessageQueue(string name, string awsProfileName = null, string awsRegionName = null,
                            int timeoutSec = DEF_TIMEOUT_SEC, ILogger logger = null, bool quiet = false,
                            bool landformOwned = true, bool autoTypes = true, bool autoCreateIfLandformOwned = true)
        {
            this.logger = logger;

            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("queue name cannot be empty");
            }

            this.autoTypes = autoTypes;

            this.LandformOwned = landformOwned;

            if (landformOwned && !name.ToLower().Contains("landform"))
            {
                name = "landform-" + name;
            }

            Name = name;
            isFIFO = name.EndsWith(".fifo");

            TimeoutSec = timeoutSec;

            client = GetClient(awsProfileName, awsRegionName, logger);

            try
            {
                url = client.GetQueueUrl(name).QueueUrl;
                var req = new GetQueueAttributesRequest()
                    {
                        QueueUrl = url,
                        AttributeNames =
                        {
                            "VisibilityTimeout",
                            "ApproximateNumberOfMessages",
                            "ApproximateNumberOfMessagesNotVisible"
                        }
                    };
                var res = client.GetQueueAttributes(req);
                if (!quiet && logger != null)
                {
                    logger.LogInfo("queue \"{0}\" exists, approx {1} messages ({2} in flight)",
                                   name, res.ApproximateNumberOfMessages, res.ApproximateNumberOfMessagesNotVisible);
                }
                if (res.VisibilityTimeout != timeoutSec)
                {
                    if (logger != null)
                    {
                        logger.LogWarn("visibility timeout for queue \"{0}\" is {1}s, expected {2}s",
                                       name, res.VisibilityTimeout, timeoutSec);
                    }
                    if (landformOwned)
                    {
                        var attrs = new Dictionary<string, string>();
                        attrs["VisibilityTimeout"] = timeoutSec.ToString();
                        client.SetQueueAttributes(url, attrs);
                    }
                    else
                    {
                        TimeoutSec = res.VisibilityTimeout;
                    }
                }
            }
            catch (QueueDoesNotExistException)
            {
                if (landformOwned && autoCreateIfLandformOwned)
                {
                    if (logger != null)
                    {
                        logger.LogInfo("creating queue \"{0}\"", name);
                    }
                    var req = new CreateQueueRequest() { QueueName = name };
                    req.Attributes["VisibilityTimeout"] = timeoutSec.ToString(); 
                    url = client.CreateQueue(req).QueueUrl;
                }
                else
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError("error creating queue \"{0}\": {1}", name, ex.Message);
                }
                throw;
            }
        }

        public void Dispose()
        {
            client.Dispose();
            client = null;
        }

        public void Purge()
        {
            client.PurgeQueue(url);
        }
        
        public void Enqueue(QueueMessage message)
        {
            Enqueue(JsonHelper.ToJson(message, autoTypes: autoTypes));
        }

        public void Enqueue(string message)
        {
            var req = new SendMessageRequest(url, message);
            if (isFIFO)
            {
                req.MessageGroupId = FIFOQueueMessageGroupID;
            }
            client.SendMessage(req);
        }

        public void UpdateTimeout(QueueMessage m, int timeoutSec)
        {
            if (m.ReceiptHandle == null)
            {
                throw new CloudException("Message does not have a receipt handle");
            }
            UpdateTimeout(m.ReceiptHandle, timeoutSec);
        }

        public void UpdateTimeout(string messageHandle, int timeoutSec)
        {
            client.ChangeMessageVisibility(new ChangeMessageVisibilityRequest(url, messageHandle, timeoutSec));
        }

        public QueueMessage DequeueOne(int waitSec = 0, int overrideVisibilityTimeout = -1,
                                       Func<string, QueueMessage> altHandler = null)
        {
            return DequeueOne<QueueMessage>(waitSec, overrideVisibilityTimeout, altHandler);
        }

        public T DequeueOne<T>(int waitSec = 0, int overrideVisibilityTimeout = -1,
                               Func<string, QueueMessage> altHandler = null) where T : class
        {
            var msgs = Dequeue<T>(1, waitSec, overrideVisibilityTimeout, altHandler);
            return msgs.Length > 0 ? msgs[0] : null;
        }

        public T[] Dequeue<T>(int maxMessages = 1, int waitSec = 0, int overrideVisibilityTimeout = -1,
                              Func<string, QueueMessage> altHandler = null) where T : class
        {
            var req = new ReceiveMessageRequest
            {
                QueueUrl = url,                
                AttributeNames = new List<string>() { "All" },
                MessageAttributeNames = new List<string>() { "All" },
                MaxNumberOfMessages = maxMessages,
                WaitTimeSeconds = waitSec
            };
            if (overrideVisibilityTimeout >= 0)
            {
                req.VisibilityTimeout = overrideVisibilityTimeout;
            }
            //try to track information about receive times
            //among other things if a message is multiply received this can help track the latest receivehandle
            //which is apparently needed for SQS apis like ChangeMessageVisibility() and DeleteMessage()
            var now = UTCTime.NowMS(); //lower bounds

            List<Message> msgs = null;
            try
            {
                msgs = client.ReceiveMessage(req).Messages;
            }
            catch (OverLimitException e)
            {
                if (logger != null)
                {
                    logger.LogError("client over limit: {0}", e.Message);
                }
                throw;
            }

            return msgs.Select(msg =>
            {
                try
                {
                    QueueMessage qm = altHandler != null ? altHandler(msg.Body) : null;
                    T m = null;
                    if (qm == null)
                    {
                        m = JsonHelper.FromJson<T>(msg.Body, autoTypes: autoTypes);
                        qm = m as QueueMessage;
                    }
                    if (qm != null)
                    {
                        qm.MessageId = msg.MessageId;
                        qm.ReceiptHandle = msg.ReceiptHandle;
                        //https://docs.aws.amazon.com/sdkfornet/v3/apidocs/items/SQS/TMessage.html
                        if (msg.Attributes != null)
                        {
                            if (msg.Attributes.TryGetValue("ApproximateFirstReceiveTimestamp", out string frt))
                            {
                                double.TryParse(frt, out qm.ApproxFirstReceiveMS);
                            }
                            if (msg.Attributes.TryGetValue("SentTimestamp", out string st))
                            {
                                double.TryParse(st, out qm.SentMS);
                            }
                            if (msg.Attributes.TryGetValue("ApproximateReceiveCount", out string rc))
                            {
                                int.TryParse(rc, out qm.ApproxReceiveCount);
                            }
                        }
                        qm.ApproxReceiveMS = Math.Max(now, qm.ApproxFirstReceiveMS);
                        qm.MessageText = msg.Body;
                    }
                    return m;
                }
                catch (Exception e)
                {
                    if (logger != null)
                    {
                        logger.LogError("invalid message '{0}' in {1} (deleting): {2}", msg.Body, Name, e.Message);
                    }
                    try
                    {
                        DeleteMessage(msg.ReceiptHandle);
                    }
                    catch (Exception e2)
                    {
                        if (logger != null)
                        {
                            logger.LogError("error deleting message: {0}", e2.Message);
                        }
                    }
                    return null;
                }
            }).Where(obj => obj != null).ToArray();
        }

        public void DeleteMessage(QueueMessage m)
        {
            if (m.ReceiptHandle == null)
            {
                throw new CloudException("message does not have a receipt handle");
            }
            DeleteMessage(m.ReceiptHandle);
        }

        public void DeleteMessage(string receiptHandle)
        {
            client.DeleteMessage(new DeleteMessageRequest { QueueUrl = url, ReceiptHandle = receiptHandle });
        }

        public int GetNumMessages(bool includeInvisible = false, bool throwOnError = false)
        {
            try
            {
                var req = new GetQueueAttributesRequest()
                {
                    QueueUrl = url,
                    AttributeNames = { "ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible" }
                };
                var res = client.GetQueueAttributes(req);
                return res.ApproximateNumberOfMessages +
                    (includeInvisible ? res.ApproximateNumberOfMessagesNotVisible : 0);
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogException(ex, "getting number of messages in queue " + Name);
                }
                if (throwOnError)
                {
                    throw;
                }
                return 0;
            }
        }
        
        public static AmazonSQSClient GetClient(string awsProfileName = null, string awsRegionName = null,
                                                ILogger logger = null)
        {
            string[] nulls = { "", "null", "none", "auto" };
            Func<string, string> convertNull = s => s == null || nulls.Any(n => n == s.ToLower()) ? null : s;
            awsProfileName = convertNull(awsProfileName);
            awsRegionName = convertNull(awsRegionName);

            AWSCredentials awsCredentials = awsProfileName != null ? Credentials.Get(awsProfileName) : null;
            RegionEndpoint awsRegion = awsRegionName != null ? RegionEndpoint.GetBySystemName(awsRegionName) : null;

            if (awsCredentials != null && awsRegion != null)
            {
                if (logger != null)
                {
                    logger.LogInfo("creating AWS SQS client for profile \"{0}\" in region \"{1}\"",
                                   awsProfileName, awsRegionName);
                }
                return new AmazonSQSClient(awsCredentials, awsRegion);
            }
            else if (awsCredentials != null)
            {
                if (logger != null)
                {
                    logger.LogInfo("creating AWS SQS client for profile \"{0}\" in default region", awsProfileName);
                }
                return new AmazonSQSClient(awsCredentials);
            }
            else if (awsRegion != null)
            {
                if (logger != null)
                {
                    logger.LogInfo("creating AWS SQS client for default profile in region \"{0}\"", awsRegion);
                }
                return new AmazonSQSClient(awsRegion);
            }
            if (logger != null)
            {
                logger.LogInfo("creating AWS SQS client for default profile and region");
            }
            return new AmazonSQSClient();
        }

        public static bool QueueExists(AmazonSQSClient client, string name)
        {
            try
            {
                client.GetQueueUrl(name);
                return true;
            }
            catch (QueueDoesNotExistException)
            {
                return false;
            }
        }

        public static bool DeleteQueue(AmazonSQSClient client, string name)
        {
            try
            {
                client.DeleteQueue(client.GetQueueUrl(name).QueueUrl);
                return true;
            }
            catch (QueueDoesNotExistException)
            {
                return false;
            }
        }

        public void Delete()
        {
            DeleteQueue(client, Name);
        }
    }
}
