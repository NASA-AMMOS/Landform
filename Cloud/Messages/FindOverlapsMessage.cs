using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.SQS;
using Amazon.SQS.Model;

namespace OPS.Cloud
{
    /// <summary>
    /// Alignment pipeline message. 
    /// Sent by workers when they are done finding features for an image. 
    /// Starts a findOverlaps task, which scans other observations for potential overlaps. 
    /// </summary>
    public class FindOverlapsMessage : PipelineMessage
    {
        public const string TYPE = "FIND_OVERLAPS";

        public override string MessageType { get { return TYPE; } protected set { ; } }

        public string ObservationName { get; set; }

        protected FindOverlapsMessage()
        {

        }

        public FindOverlapsMessage(string observationName)
        {
            this.ObservationName = observationName;
        }

        public FindOverlapsMessage(Message m)
        {
            if (m.MessageAttributes[MessageFields.MSG_TYPE_FIELD].StringValue != MessageType)
            {
                throw new CloudException("creating FindOverlapsMsg from wrong message type");
            }
            ObservationName = m.MessageAttributes[MessageFields.OBSERVATION_NAME].StringValue;
            MessageId = m.MessageId;
            receiptHandle = m.ReceiptHandle;
        }

        /// <summary>
        /// Send needs an additional delay to avoid the need for a strongly consistent read 
        /// in the FindOverlaps task. 
        /// If there was no delay, two FindOverlaps tasks for overlapping observation occuring simultaneously right after the upload of those observations
        /// could both miss observing the other observation, and no overlap would be recorded for those two observations. 
        /// </summary>
        /// <param name="client"></param>
        /// <param name="observationName"></param>
        /// <param name="queueUrl"></param>
        public void Send(IAmazonSQS client, string queueUrl)
        {

            Dictionary<string, MessageAttributeValue> attributes = new Dictionary<string, MessageAttributeValue>
                {
                    {
                    MessageFields.MSG_TYPE_FIELD, new MessageAttributeValue
                    {DataType = "String", StringValue = TYPE }
                    },
                    {
                    MessageFields.OBSERVATION_NAME, new MessageAttributeValue
                    {DataType = "String", StringValue = ObservationName }
                    }
                };

            SendMessageRequest request = new SendMessageRequest
            {
                DelaySeconds = (int)TimeSpan.FromSeconds(60).TotalSeconds,
                MessageAttributes = attributes,
                MessageBody = "{}",
                QueueUrl = queueUrl
            };
            SendMessageResponse response = client.SendMessage(request);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new CloudException("Could not send message to queue");
            }
            
        }

    }
}
