using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.SQS;
using Amazon.SQS.Model;

namespace OPS.Cloud
{
    public class FindOverlapsMessage : PipelineMessage
    {
        public const string TYPE = "FIND_OVERLAPS";

        public override string MessageType { get { return TYPE; } protected set { ; } }

        public string ObservationName { get; set; }

        protected FindOverlapsMessage()
        {

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

        public static void Send(IAmazonSQS client, string observationName, string queueUrl)
        {

            string MessageId = Send(client, new Dictionary<string, MessageAttributeValue>
                {
                    {
                    MessageFields.MSG_TYPE_FIELD, new MessageAttributeValue
                    {DataType = "String", StringValue = TYPE }
                    },
                    {
                    MessageFields.OBSERVATION_NAME, new MessageAttributeValue
                    {DataType = "String", StringValue = observationName } 
                    }
                }, queueUrl);
        }

    }
}
