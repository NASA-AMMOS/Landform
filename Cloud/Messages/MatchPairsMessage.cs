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
    /// Alignment pipeline message
    /// Sent by alignment workers when the FindOverlaps task finds two potentially overlapping messages 
    /// </summary>
    public class MatchPairsMessage : PipelineMessage
    {
        public const string TYPE = "MATCH_PAIRS";

        public override string MessageType { get { return TYPE; } protected set { ; } }

        public string ObservationName0 { get; set; }

        public string ObservationName1 { get; set; }

        public string ProjectName { get; set; }

        protected MatchPairsMessage()
        {

        }

        public MatchPairsMessage(string observationName, string observationName2, string projectName)
        {
            this.ObservationName0 = observationName;
            this.ObservationName1 = observationName2;
            this.ProjectName = projectName;
        }

        public MatchPairsMessage(Message m)
        {
            if (m.MessageAttributes[MessageFields.MSG_TYPE_FIELD].StringValue != MessageType)
            {
                throw new CloudException("creating MatchPairsMsg from wrong message type");
            }
            ObservationName0 = m.MessageAttributes[MessageFields.OBSERVATION_NAME].StringValue;
            ObservationName1 = m.MessageAttributes[MessageFields.OBSERVATION_NAME_2].StringValue;
            ProjectName = m.MessageAttributes[MessageFields.PROJECT_NAME].StringValue;
            MessageId = m.MessageId;
            receiptHandle = m.ReceiptHandle;
        }

        /// <summary>
        /// Send a new match pairs message. 
        /// Order of observations does not matter. 
        /// </summary>
        /// <param name="client"></param>
        /// <param name="observationName"></param>
        /// <param name="observationName2"></param>
        /// <param name="queueUrl"></param>
        public void Send(IAmazonSQS client, string queueUrl)
        {
            string MessageId = Send(client, new Dictionary<string, MessageAttributeValue>
                {
                    {
                    MessageFields.MSG_TYPE_FIELD, new MessageAttributeValue
                    {DataType = "String", StringValue = TYPE }
                    },
                    {
                    MessageFields.OBSERVATION_NAME, new MessageAttributeValue
                    {DataType = "String", StringValue = ObservationName0 } 
                    },
                    {
                    MessageFields.OBSERVATION_NAME_2, new MessageAttributeValue
                    {DataType = "String", StringValue = ObservationName1 } 
                    },
                    {
                    MessageFields.PROJECT_NAME, new MessageAttributeValue
                    {DataType = "String", StringValue = ProjectName }
                    }
                }, queueUrl);
        }

    }
}
