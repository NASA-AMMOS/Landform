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

        public string ProjectName;

        public TilingQueueMessage() { }
        public TilingQueueMessage(string projectName) { ProjectName = projectName; }

        public string Info()
        {
            return string.Format("[{0}] {1} {2}", ProjectName, GetType().Name, MessageId);
        }
    }

    public class TilingQueue
    {
        public const int VISIBILITY_TIMEOUT_SEC = 20;

        private static ILog logger = LogManager.GetLogger(typeof(TilingQueue));

        public string Name;

        private string url;
        private AmazonSQSClient client;

        public TilingQueue(string prefix, string awsProfileName, string endpointName = "us-west-1")
        {
            Name = "TilingServerQueue" + prefix;

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
                createQueueRequest.Attributes["VisibilityTimeout"] = VISIBILITY_TIMEOUT_SEC.ToString(); 
                url = client.CreateQueue(createQueueRequest).QueueUrl;
            }
        }
        
        public void Enqueue(TilingQueueMessage message)
        {
            var response = client.SendMessage(new SendMessageRequest(url, JsonHelper.ToJson(message)));
            message.MessageId = response.MessageId;
        }

        /// <summary>
        /// If timeoutSec is omitted or negative the default VISIBILITY_TIMEOUT_SEC will be used.
        /// </summary>
        /// <returns></returns>
        public void UpdateTimeout(TilingQueueMessage m, int timeoutSec = -1)
        {
            if (m.ReceiptHandle == null)
            {
                throw new CloudException("Message does not have a receipt handle");
            }
            UpdateTimeout(m.ReceiptHandle);
        }

        /// <summary>
        /// If timeoutSec is omitted or negative the default VISIBILITY_TIMEOUT_SEC will be used.
        /// </summary>
        /// <returns></returns>
        public void UpdateTimeout(string messageHandle, int timeoutSec = -1)
        {
            if (timeoutSec < 0)
            {
                timeoutSec = VISIBILITY_TIMEOUT_SEC;
            }
            client.ChangeMessageVisibility(new ChangeMessageVisibilityRequest(url, messageHandle, timeoutSec));
        }

        public TilingQueueMessage[] Dequeue(int maxMessages = 10, int waitSec = 15)
        {
            var req = new ReceiveMessageRequest
            {
                QueueUrl = url,                
                AttributeNames = new List<string>() { "All" },
                MessageAttributeNames = new List<string>() { "All" },
                MaxNumberOfMessages = maxMessages,
                WaitTimeSeconds = waitSec
            };
            return client.ReceiveMessage(req).Messages.Select(msg =>
            {
                try
                {
                    var m = (TilingQueueMessage)JsonHelper.FromJson(msg.Body);
                    m.MessageId = msg.MessageId;
                    m.ReceiptHandle = msg.ReceiptHandle;
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
