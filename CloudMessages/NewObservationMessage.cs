using Amazon.SQS.Model;

namespace OPS.Cloud
{
    /// <summary>
    /// Tiling pipeline message. 
    /// Sent by DynamoProcessing lambda when all children are present for a parent tile. 
    /// </summary>
    [Message("NEW_IMAGE")]
    public class NewObservationMessage : PipelineMessage
    {
        [Field("FileS3Path")]
        public string Url { get; set; }

        protected NewObservationMessage() { }
        public NewObservationMessage(string url)
        {
            Url = url;
        }
        static NewObservationMessage()
        {
            RegisterType(typeof(NewObservationMessage));
        }
    }
}
