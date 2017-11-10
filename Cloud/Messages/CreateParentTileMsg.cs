using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Amazon.SQS;
using Amazon.SQS.Model;

using Newtonsoft.Json;

using OPS.Util;

namespace OPS.Cloud
{
    /// <summary>
    /// This message type is used by Pipeline/Commands/QueueListener and LambdaDynamoProcessing 
    /// Message fields & body must be kept in sync with names in LambdaUtil/Names and LambdaDynamoProcessing
    /// </summary>
    public class CreateParentTileMsg : PipelineMessage
    {
        public const string TYPE = "CREATE_PARENT_TILE";

        public override string MessageType { get { return TYPE; } protected set { ; } }

        public string ParentPath { get; set; }

        public int NumChildren { get; set; }

        public List<string> ChildExtensions { get; set; }

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
            ChildExtensions = JsonConvert.DeserializeObject<List<string>>(m.Body); //can't use type handling here because converting from .NET core to .NET
            //ChildExtensions = (List<string>)JsonHelper.FromJson(m.Body);

        }

        //No send method because these messages always pass lambda -> tiling worker
    }
}
