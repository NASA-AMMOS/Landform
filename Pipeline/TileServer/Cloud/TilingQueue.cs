using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.Runtime;
using OPS.Cloud;
using OPS.Util;
using Newtonsoft.Json;
using log4net;
using OPS.MathExtensions;

namespace OPS.Pipeline.TileServer
{
    public class TilingQueueMessage
    {
        [JsonIgnore]
        public string MessageId;

        [JsonIgnore]
        public string ReceiptHandle;

        //approx first time any receiver received this message
        //or -1 if unknown
        //ms since UTC epoch
        [JsonIgnore]
        public long ApproxFirstReceiveMS = -1;

        //approx latest time we received this message
        //this may be a lower bounds
        //note: other receivers may have received it even later
        //ms since UTC epoch
        [JsonIgnore]
        public long ApproxLastReceiveMS = -1;

        public string ProjectName;

        public TilingQueueMessage() { }
        public TilingQueueMessage(string projectName) { ProjectName = projectName; }

        public string Info()
        {
            var typeName = GetType().Name;
            if (typeName.EndsWith("Message"))
            {
                typeName = typeName.Substring(0, typeName.Length - "Message".Length);
            }
            return string.Format("[{0}] {1} {2}", ProjectName, typeName, MessageId);
        }
    }

    public class TilingQueue
    {
        public const int DEF_TIMEOUT_SEC = 20;

        private static ILog logger = LogManager.GetLogger(typeof(TilingQueue));

        public string Name { get; private set; }
        public int TimeoutSec { get; private set; }

        private string url;
        private AmazonSQSClient client;

        public TilingQueue(string prefix, string awsProfileName, int timeoutSec = DEF_TIMEOUT_SEC,
                           string endpointName = "us-west-1")
        {
            Name = "TilingServerQueue" + prefix;
            TimeoutSec = timeoutSec;

            RegionEndpoint awsRegion = RegionEndpoint.GetBySystemName(endpointName);
            AWSCredentials awsCredentials = null;
            if (awsProfileName != null)
            {
                awsCredentials = Credentials.Get(awsProfileName);
            }

            if (awsCredentials != null)
            {
                client = new AmazonSQSClient(awsCredentials, awsRegion);
            }
            else
            {
                client = new AmazonSQSClient(awsRegion);
            }

            try
            {
                url = client.GetQueueUrl(Name).QueueUrl;
            }
            catch (QueueDoesNotExistException)
            {
                CreateQueueRequest createQueueRequest = new CreateQueueRequest() { QueueName = Name };
                createQueueRequest.Attributes["VisibilityTimeout"] = timeoutSec.ToString(); 
                url = client.CreateQueue(createQueueRequest).QueueUrl;
            }
        }
        
        public void Enqueue(TilingQueueMessage message)
        {
            client.SendMessage(new SendMessageRequest(url, JsonHelper.ToJson(message)));
        }

        public void UpdateTimeout(TilingQueueMessage m, int timeoutSec)
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

        public TilingQueueMessage DequeueOne(int waitSec = 0)
        {
            var msgs = Dequeue(1, waitSec);
            return msgs.Length > 0 ? msgs[0] : null;
        }

        public TilingQueueMessage[] Dequeue(int maxMessages = 1, int waitSec = 0)
        {
            var req = new ReceiveMessageRequest
            {
                QueueUrl = url,                
                AttributeNames = new List<string>() { "All" },
                MessageAttributeNames = new List<string>() { "All" },
                MaxNumberOfMessages = maxMessages,
                WaitTimeSeconds = waitSec
            };
            //try to track information about receive times
            //among other things if a message is multiply received this can help track the latest receivehandle
            //which is apparently needed for SQS apis like ChangeMessageVisibility() and DeleteMessage()
            long now = (long)UTCTime.NowMS(); //lower bounds
            var msgs = client.ReceiveMessage(req).Messages;
            return msgs.Select(msg =>
            {
                try
                {
                    var m = (TilingQueueMessage)JsonHelper.FromJson(msg.Body);
                    m.MessageId = msg.MessageId;
                    m.ReceiptHandle = msg.ReceiptHandle;
                    string ts = null;
                    if (msg.Attributes != null &&
                        msg.Attributes.TryGetValue("ApproximateFirstReceiveTimestamp", out ts))
                    {
                        try
                        {
                            m.ApproxFirstReceiveMS = long.Parse(ts);
                        }
                        catch (Exception)
                        {
                        }
                    }
                    m.ApproxLastReceiveMS = Math.Max(now, m.ApproxFirstReceiveMS);
                    return m;
                }
                catch (Exception e)
                {
                    
                    logger.Error("invalid message '" + msg.Body + "' in " + Name + " (deleting): " + e.Message);
                    try
                    {
                        DeleteMessage(msg.ReceiptHandle);
                    }
                    catch (Exception e2)
                    {
                        logger.Error("error deleting message: " + e2.Message);
                    }
                    return null;
                }
            }).Where(obj => obj != null).ToArray();
        }

        public void DeleteMessage(TilingQueueMessage m)
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

        public void Delete()
        {
            client.DeleteQueue(new DeleteQueueRequest(url));
        }
    }
}
