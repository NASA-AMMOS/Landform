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
    /// This message type is used by Pipeline/Commands/QueueListener and LambdaDynamoProcessing 
    /// Message fields must be kept in sync with names in LambdaUtil/Names and LambdaDynamoProcessing
    /// </summary>
    public class CreateParentTileMsg : PipelineMessage
    {
        public const string TYPE = "CREATE_PARENT_TILE";

        public override string MessageType { get { return TYPE; } protected set { ; } }

        public string ParentPath { get; set; }

        public int NumChildren { get; set; }

        protected CreateParentTileMsg()
        {

        }

        public CreateParentTileMsg(Message m)
        {
            if (m.MessageAttributes[MessageFields.MSG_TYPE_FIELD].StringValue != MessageType)
            {
                throw new CloudException("creating CreateParentTileMsg from wrong message type");
            }
            ParentPath = m.MessageAttributes[MessageFields.PARENT_PATH].StringValue;
            NumChildren = Convert.ToInt32(m.MessageAttributes[MessageFields.NUM_CHILDREN].StringValue);
            MessageId = m.MessageId;
            receiptHandle = m.ReceiptHandle;
        }

        public static void Send(IAmazonSQS client, string parentPath, string numChildren, string queueUrl)
        {

            string MessageId = Send(client, new Dictionary<string, MessageAttributeValue>
                {
                    {
                    MessageFields.MSG_TYPE_FIELD, new MessageAttributeValue
                    {DataType = "String", StringValue = TYPE }
                    },
                    {
                    MessageFields.PARENT_PATH, new MessageAttributeValue
                    {DataType = "String", StringValue = parentPath } 
                    },
                    {
                    MessageFields.NUM_CHILDREN, new MessageAttributeValue
                    {DataType = "String", StringValue = numChildren }
                    }
                }, queueUrl);
        }

    }
}
