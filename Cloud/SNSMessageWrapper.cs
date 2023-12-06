using Newtonsoft.Json;

namespace JPLOPS.Cloud
{
    //https://docs.aws.amazon.com/sns/latest/dg/sns-message-and-json-formats.html
    public class SNSMessageWrapper : QueueMessage
    {
        public string Type; //Notification

        [JsonProperty("MessageId")] //would hide QueueMessage.MessageId
        public string Guid; //guid

        public string TopicArn;
        public string Subject; //Amazon S3 Notification
        public string Message; //S3EventMessage JSON
        public string Timestamp; //2019-11-18T21:59:44.769Z
        public string SignatureVersion; //1
        public string Signature;
        public string SigningCertURL;
        public string UnsubscribeURL;
    }
}
