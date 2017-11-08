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

        /// <summary>
        /// Fields used by messages. 
        /// TODO I'm sure there's a cleaner way to do this
        /// </summary>
        protected class MessageFields
        {
            //used by all messages 
            public const string MSG_TYPE_FIELD = "MessageType";

            //used (in various combinations) by alignment pipeline messages 
            public const string FILE_S3_PATH = "FileS3Path";
            public const string OBSERVATION_NAME = "ObservationName";
            public const string OBSERVATION_NAME_2 = "ObservationName2";
            public const string PROJECT_NAME = "ProjectName";

            //used by CreateParentTileMsg only
            public const string PARENT_PATH = "ParentPath"; 
            public const string NUM_CHILDREN = "NumChildren";
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
                case MatchPairsMsg.TYPE:
                    return new MatchPairsMsg(m);
                case CreateParentTileMsg.TYPE:
                    return new CreateParentTileMsg(m);
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
