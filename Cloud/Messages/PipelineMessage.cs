using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.SQS.Model;
using Amazon.SQS;

namespace OPS.Cloud
{
    public abstract class PipelineMessage
    {
        public string MessageId { get; protected set; }

        public abstract string MessageType { get;  protected set;}

        protected string receiptHandle;

        //protected string queueUrl; 

        protected class MessageFields
        {
            public const string MSG_TYPE_FIELD = "MessageType";
            public const string FILE_S3_PATH = "FileS3Path";
            public const string OBSERVATION_NAME = "ObservationName";
            public const string OBSERVATION_NAME_2 = "ObservationName2";
        }

        /// <summary>
        /// Return a message object of the appropriate type for this SQS message
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public static PipelineMessage FromMessage(Message m)
        {
            switch (m.MessageAttributes[MessageFields.MSG_TYPE_FIELD].StringValue)
            {
                case NewObservationMsg.TYPE:
                    return new NewObservationMsg(m);
                case FindOverlapsMsg.TYPE:
                    return new FindOverlapsMsg(m);
            }
            throw new CloudException("Unrecognized message type");
        }


        /// <summary>
        /// Delete this message 
        /// </summary>
        /// <param name="client"></param>
        /// <param name="queueUrl"></param>
        public void DeleteMessage(IAmazonSQS client, string queueUrl)
        {
            var delRequest = new DeleteMessageRequest
            {
                QueueUrl = queueUrl,
                ReceiptHandle = this.receiptHandle
            };

            var response = client.DeleteMessage(delRequest);
            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new CloudException("Could not delete message");
            }
        }

        /// <summary>
        /// Helper called by concrete implementations' Send methods
        /// </summary>
        /// <param name="client"></param>
        /// <param name="attributes"></param>
        /// <param name="queueUrl"></param>
        /// <returns></returns>
        protected static string Send(IAmazonSQS client, Dictionary<string, MessageAttributeValue> attributes, string queueUrl)
        {
            SendMessageRequest request = new SendMessageRequest
            {
                DelaySeconds = (int)TimeSpan.FromSeconds(5).TotalSeconds,
                MessageAttributes = attributes,
                MessageBody = "{}",
                QueueUrl = queueUrl
            };
            SendMessageResponse response = client.SendMessage(request);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new CloudException("Could not send message to queue");
            }

            return response.MessageId;
        }
    }
}
