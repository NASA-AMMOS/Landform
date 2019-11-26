using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace OPS.Cloud
{
    //https://docs.aws.amazon.com/AmazonS3/latest/dev/notification-content-structure.html
    public class S3User
    {
        public string principalId; //AWS:<id>:<username>
    }

    public class S3Bucket
    {
        public string name; //m20-ids-g-data-gahlf1
        public S3User ownerIdentity;
        public string arn; //arn:aws-us-gov:s3:::m20-ids-g-data-gahlf1
    }

    public class S3ObjectMetadata
    {
        public string key; //MedaUnprocessedImage_0625309648-24478-1.log (URL encoded)
        public long size; //21501
        public string eTag; //afa2e1cb4d588edf78b6fdb0184a76dd\",\"sequencer\":\"005DD3145084A6950F\"}}
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
        public string eventVersion; //2.1
        public string eventSource; //aws:s3
        public string awsRegion; ///us-gov-west-1
        public string eventTime; //2019-11-18T21:59:44.631Z
        public string eventName; //ObjectCreated:Put
        public S3User userIdentity;
        public Dictionary<string, string> requestParameters;
        public Dictionary<string, string> responseElements;
        public S3EventData s3;
        //glacierEventData
    }

    public class S3EventMessage
    {
        public List<S3EventRecord> Records;
    }
}
