using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using JPLOPS.Util;

namespace JPLOPS.Cloud
{
    //https://docs.aws.amazon.com/AmazonS3/latest/dev/notification-content-structure.html
    public class S3User
    {
        public string principalId; //AWS:<id>:<username>
    }

    public class S3Bucket
    {
        public string name;
        public S3User ownerIdentity;
        public string arn;
    }

    public class S3ObjectMetadata
    {
        public string key;
        public long size;
        public string eTag;
        public string versionId; //object version if bucket is versioning-enabled, otherwise null
        public string sequencer; //hex value to determine event sequence, only with PUTs and DELETEs
    }

    public class S3EventData
    {
        public string s3SchemaVersion; //1.0
        public string configurationId; //tf-s3-topic-20191118215246258500000001
        public S3Bucket bucket;

        [JsonProperty("object")] //object is a reserved word
        public S3ObjectMetadata obj;
    }

    public class S3EventRecord
    {
        public string eventVersion; //2.1, 2,2
        public string eventSource;  //aws:s3
        public string awsRegion;
        public string eventTime;    //2019-11-18T21:59:44.631Z
        public string eventName;    //ObjectCreated:Put
        public S3User userIdentity;
        public Dictionary<string, string> requestParameters;
        public Dictionary<string, string> responseElements;
        public S3EventData s3;
        //glacierEventData
    }

    public class S3EventMessage : QueueMessage
    {
        public List<S3EventRecord> Records;

        public static string GetUrl(S3EventMessage msg, string eventType = "ObjectCreated")
        {
            if (msg.Records.Count != 1)
            {
                throw new Exception(string.Format("S3 event message has {0} records, expected 1", msg.Records.Count));
            }

            var record = msg.Records[0];
            if (!string.IsNullOrEmpty(eventType) && !record.eventName.StartsWith(eventType))
            {
                throw new Exception(string.Format("S3 event message {0} is not {1}", record.eventName, eventType));
            }

            var eventData = record.s3;
            if (eventData == null)
            {
                throw new Exception("S3 event message has no event data");
            }

            if (eventData.bucket == null)
            {
                throw new Exception("S3 event message has no bucket");
            }

            if (eventData.obj == null)
            {
                throw new Exception("S3 event message has no S3 object");
            }

            return string.Format("s3://{0}/{1}", eventData.bucket.name, eventData.obj.key); //already url encoded
        }

        public static string GetUrl(SNSMessageWrapper msg, string eventType = "ObjectCreated")
        {
            return GetUrl(JsonHelper.FromJson<S3EventMessage>(msg.Message), eventType);
        }
    }
}
