using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.SQS.Model;
using Amazon.SQS;

namespace OPS.Cloud
{
    //TODO: This class should (maybe?) be cross compiled between Core and Framework as it is used by both Pipeline/Commands and LambdaS3ImageIntake (VS 2017)
    //This would require refactoring to use async, as the AWSSDK for Core only supports Async operations 


    public class NewObservationMsg : PipelineMessage
    {

        public string Url { get; set; }

        public const string TYPE = "NEW_IMAGE";
        public override string MessageType { get { return TYPE; } protected set {; } }

        protected NewObservationMsg()
        {

        }

        /// <summary>
        /// Send a new image message for the alignment pipeline
        /// </summary>
        /// <param name="client"></param>
        /// <param name="url">S3 address of the image</param>
        /// <returns></returns>
        public static void Send(IAmazonSQS client, String url, string queueUrl)
        {
            string MessageId = Send(client, new Dictionary<string, MessageAttributeValue>
                {
                    {
                    MessageFields.MSG_TYPE_FIELD, new MessageAttributeValue
                    {DataType = "String", StringValue = TYPE }
                    },
                    {
                    MessageFields.FILE_S3_PATH, new MessageAttributeValue
                    {DataType = "String", StringValue = url } //No data types other than string currently supported
                    }
                }, 
                queueUrl);
            return;
        }

        /// <summary>
        /// Return a message object from an SQS message
        /// Helper for abstract FromMessage method
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public NewObservationMsg(Message m)
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
