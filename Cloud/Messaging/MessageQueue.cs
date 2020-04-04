using System;
using System.Collections.Generic;
using System.Linq;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Newtonsoft.Json;
using OPS.Util;
using OPS.MathExtensions;

namespace OPS.Cloud
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
    }

    public class MessageQueue
    {
        public const int DEF_TIMEOUT_SEC = 20;

        public string Name { get; private set; }
        public int TimeoutSec { get; private set; }
        public bool LandformOwned { get; private set; }

        private ILogger logger;
        private string url;
        private bool autoTypes;
        private AmazonSQSClient client;

        public MessageQueue(string name, string awsProfileName = null, string awsRegionName = null,
                            int timeoutSec = DEF_TIMEOUT_SEC, ILogger logger = null, bool quiet = false,
                            bool landformOwned = true, bool autoTypes = true)
        {
            this.logger = logger;

            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("queue name cannot be empty");
            }

            this.autoTypes = autoTypes;

            this.LandformOwned = landformOwned;

            if (landformOwned && !name.ToLower().StartsWith("landform-"))
            {
                name = "landform-" + name;
            }

            Name = name;

            TimeoutSec = timeoutSec;

            client = GetClient(awsProfileName, awsRegionName);

            try
            {
                url = client.GetQueueUrl(Name).QueueUrl;
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
                if (!quiet)
                {
                    logger.LogInfo("queue \"{0}\" exists, approx {1} messages ({2} in flight)",
                                   Name, res.ApproximateNumberOfMessages, res.ApproximateNumberOfMessagesNotVisible);
                }
                if (res.VisibilityTimeout != timeoutSec)
                {
                    logger.LogWarn("visibility timeout for queue \"{0}\" is {1}s, expected {2}s",
                                   Name, res.VisibilityTimeout, timeoutSec);
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
                if (landformOwned)
                {
                    logger.LogInfo("creating queue \"{0}\"", Name);
                    var req = new CreateQueueRequest() { QueueName = Name };
                    req.Attributes["VisibilityTimeout"] = timeoutSec.ToString(); 
                    url = client.CreateQueue(req).QueueUrl;
                }
                else
                {
                    throw;
                }
            }
        }

        public void Purge()
        {
            client.PurgeQueue(url);
        }
        
        public void Enqueue(QueueMessage message)
        {
            Enqueue(JsonHelper.ToJson(message, autoTypes: autoTypes));
        }

        public void Enqueue(string json)
        {
            client.SendMessage(new SendMessageRequest(url, json));
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

        public QueueMessage DequeueOne(int waitSec = 0, int overrideVisibilityTimeout = -1)
        {
            return DequeueOne<QueueMessage>(waitSec, overrideVisibilityTimeout);
        }

        public T DequeueOne<T>(int waitSec = 0, int overrideVisibilityTimeout = -1) where T : class
        {
            var msgs = Dequeue<T>(1, waitSec, overrideVisibilityTimeout);
            return msgs.Length > 0 ? msgs[0] : null;
        }

        public T[] Dequeue<T>(int maxMessages = 1, int waitSec = 0, int overrideVisibilityTimeout = -1) where T : class
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
                logger.LogError("client over limit: {0}", e.Message);
                throw;
            }

            return msgs.Select(msg =>
            {
                try
                {
                    T m = JsonHelper.FromJson<T>(msg.Body, autoTypes: autoTypes);
                    if (m is QueueMessage)
                    {
                        var qm = m as QueueMessage;
                        qm.MessageId = msg.MessageId;
                        qm.ReceiptHandle = msg.ReceiptHandle;
                        string ts = null;
                        if (msg.Attributes != null &&
                            msg.Attributes.TryGetValue("ApproximateFirstReceiveTimestamp", out ts))
                        {
                            double.TryParse(ts, out qm.ApproxFirstReceiveMS);
                        }
                        qm.ApproxReceiveMS = Math.Max(now, qm.ApproxFirstReceiveMS);
                    }
                    return m;
                }
                catch (Exception e)
                {
                    
                    logger.LogError("invalid message '{0}' in {1} (deleting): {2}", msg.Body, Name, e.Message);
                    try
                    {
                        DeleteMessage(msg.ReceiptHandle);
                    }
                    catch (Exception e2)
                    {
                        logger.LogError("error deleting message: {0}", e2.Message);
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

        public static AmazonSQSClient GetClient(string awsProfileName = null, string awsRegionName = null)
        {
            string[] nulls = { "", "null", "none", "auto" };
            Func<string, string> convertNull = s => s == null || nulls.Any(n => n == s.ToLower()) ? null : s;
            awsProfileName = convertNull(awsProfileName);
            awsRegionName = convertNull(awsRegionName);

            AWSCredentials awsCredentials = awsProfileName != null ? Credentials.Get(awsProfileName) : null;
            RegionEndpoint awsRegion = awsRegionName != null ? RegionEndpoint.GetBySystemName(awsRegionName) : null;

            if (awsCredentials != null && awsRegion != null)
            {
                return new AmazonSQSClient(awsCredentials, awsRegion);
            }
            else if (awsCredentials != null)
            {
                return new AmazonSQSClient(awsCredentials);
            }
            else if (awsRegion != null)
            {
                return new AmazonSQSClient(awsRegion);
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
