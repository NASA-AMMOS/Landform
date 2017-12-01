using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.SQS.Model;
using Amazon.SQS;

namespace OPS.Cloud
{
    /// <summary>
    /// Tiling pipeline message. 
    /// Sent by DynamoProcessing lambda when all children are present for a parent tile. 
    /// </summary>
    public class NewObservationMessage : PipelineMessage
    {

        public string Url { get; set; }

        public const string TYPE = "NEW_IMAGE";
        public override string MessageType { get { return TYPE; } protected set {; } }

        protected NewObservationMessage()
        {

        }

        /// <summary>
        /// Return a message object from an SQS message
        /// Helper for abstract FromMessage method
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public NewObservationMessage(Message m)
        {
            if (m.MessageAttributes[MessageFields.MSG_TYPE_FIELD].StringValue != TYPE)
            {
                throw new CloudException("creating NewObservationMsg from wrong message type");
            }
            Url = m.MessageAttributes[MessageFields.FILE_S3_PATH].StringValue;
            MessageId = m.MessageId;
            receiptHandle = m.ReceiptHandle;
        }


    }
}
