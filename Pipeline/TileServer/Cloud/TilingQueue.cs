using Amazon.SQS.Model;
using Amazon.SQS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon;
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
        public TilingQueueMessage(string projectName) { this.ProjectName = projectName; }
    }

    public class TilingQueue
    {
        string prefix;
        AWSCredentials awsCredentials;
        RegionEndpoint awsRegion;
        int timeout = 60 * 15;
        static ILog logger = LogManager.GetLogger(typeof(TilingQueue));

        public const int MAX_MESSAGES_PER_DEQUEUE = 10;


        string queueName
        {
            get
            {
                return "TilingServerQueue" + this.prefix;
            }
        }

        string queueUrl;

        public TilingQueue(string prefix, string awsProfileName, string endpointName = "us-west-1")
        {
            this.prefix = prefix;
            if (awsProfileName != null)
            {
                this.awsCredentials = Credentials.Get(awsProfileName);
            }
            this.awsRegion = RegionEndpoint.GetBySystemName(endpointName);
            EnsureExists();
            this.queueUrl = GetClient().GetQueueUrl(queueName).QueueUrl;
        }
        
        //Use default credentials (or, for EC2 workers, their IAM role) if credentials are not provided 
        private AmazonSQSClient GetClient()
        {
            if(awsCredentials == null)
            {
                return new AmazonSQSClient(awsRegion);
            }
            return new AmazonSQSClient(awsCredentials, awsRegion);
        }

        public void Enqueue(TilingQueueMessage message)
        {
            SendMessageRequest request = new SendMessageRequest(queueUrl, JsonHelper.ToJson(message));
            SendMessageResponse response = GetClient().SendMessage(request);
            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new CloudException("Could not enque message");
            }
        }

        public void UpdateTimeout(TilingQueueMessage m)
        {
            if(m.ReceiptHandle == null)
            {
                throw new CloudException("Message does not have a recipt handle");
            }
            ChangeMessageVisibilityRequest request = new ChangeMessageVisibilityRequest(queueUrl, m.ReceiptHandle, this.timeout);
            ChangeMessageVisibilityResponse response = GetClient().ChangeMessageVisibility(request);
            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new CloudException("Could not update visibility");
            }
        }

        /// <summary>
        /// Max messages will be clamped between 1 and 10
        /// </summary>
        /// <param name="maxMessages"></param>
        /// <returns></returns>
        public TilingQueueMessage[] Deque(int maxMessages = 1)
        {
            var req = new ReceiveMessageRequest
            {
                AttributeNames = new List<string>() { "All" },
                MessageAttributeNames = new List<string>() { "All" },
                MaxNumberOfMessages = MathE.Clamp(maxMessages, 1, MAX_MESSAGES_PER_DEQUEUE),
                QueueUrl = this.queueUrl,                
                WaitTimeSeconds = (int)TimeSpan.FromSeconds(15).TotalSeconds // How long to wait for message
            };
            ReceiveMessageResponse r = GetClient().ReceiveMessage(req);
            return r.Messages.Select(msg =>
            {
                var m = (TilingQueueMessage)JsonHelper.FromJson(msg.Body);
                m.MessageId = msg.MessageId;
                m.ReceiptHandle = msg.ReceiptHandle;
                return m;
            }).ToArray();
        }

        public void Delete(TilingQueueMessage m)
        {
            if(m.ReceiptHandle == null)
            {
                throw new CloudException("Message does not have a receipt handle");
            }
            var delRequest = new DeleteMessageRequest
            {
                QueueUrl = this.queueUrl,
                ReceiptHandle = m.ReceiptHandle
            };

            var response = GetClient().DeleteMessageAsync(delRequest).Result;
            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new CloudException("Could not delete message");
            }
        }

        void Delete()
        {
            try
            {
                AmazonSQSClient client = GetClient();
                var request = new DeleteQueueRequest(this.queueUrl);
                var response = client.DeleteQueue(request);
            }
            catch (Exception e)
            {
                logger.Error(e.Message);
            }
        }

        void EnsureExists()
        {
            try
            {
                AmazonSQSClient client = GetClient();
                CreateQueueRequest createQueueRequest = new CreateQueueRequest();
                createQueueRequest.QueueName = this.queueName;
                createQueueRequest.Attributes["VisibilityTimeout"] = timeout.ToString(); 
                CreateQueueResponse createQueueResponse = client.CreateQueue(createQueueRequest);
            }
            catch (Exception e)
            {
                logger.Error(e.Message);
            }
        }
    }
}
