using System.Collections.Generic;
using System.Linq;
using Amazon.SQS.Model;

namespace OPS.Cloud
{
    /// <summary>
    /// This message type is used by Pipeline/Commands/QueueListener and LambdaDynamoProcessing 
    /// Message fields & body must be kept in sync with names in LambdaUtil/Names and LambdaDynamoProcessing
    /// </summary>
    [Message("CREATE_PARENT_TILE")]
    public class CreateParentTileMessage : PipelineMessage
    {
        //Path of parent tiles within s3. Format bucket/prefix. Includes full file name, excludes extension. 
        [Field("ParentPath")]
        public string ParentPath { get; set; }

        [Body]
        public List<string> ChildExtensions { get; set; }

        protected CreateParentTileMessage() { }
        protected CreateParentTileMessage(string parentPath, IEnumerable<string> childExtensions)
        {
            ParentPath = parentPath;
            ChildExtensions = childExtensions.ToList();
        }
    }
}
