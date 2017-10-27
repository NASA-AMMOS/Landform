using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Amazon.Lambda.Core;

using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;

using Lambda.LambdaUtil;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace Lambda.LambdaScanS3
{
    public class ScanRequest
    {
        public string Bucket { get; set; }
        public string Prefix { get; set; }
    }

    public class Function
    {

        IAmazonS3 S3Client { get; set; }
        IAmazonSQS SQSClient { get; set; }

        /// <summary>
        /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
        /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
        /// region the Lambda function is executed in.
        /// </summary>
        public Function()
        {
            S3Client = new AmazonS3Client();
            SQSClient = new AmazonSQSClient();
        }

        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<string> FunctionHandler(ScanRequest s3url, ILambdaContext context)
        {
            //scan all items at that path 
            ListObjectsV2Response response;
            do
            {
                response = await S3Client.ListObjectsV2Async(new ListObjectsV2Request()
                {
                    BucketName = s3url.Bucket,
                    Prefix = s3url.Prefix,
                    MaxKeys = 100
                });

                //add item to queue for each message 
                foreach (S3Object entry in response.S3Objects)
                {
                    if (entry.Key.Length > 0)
                    {
                        SendMessage(entry.BucketName, entry.Key);
                    }
                }
            } while (response.IsTruncated == true);


            //put messages for all valid images in our job queue
            LambdaLogger.Log(s3url.Prefix);

            return "ok";
        }

        private async Task<System.Net.HttpStatusCode> SendMessage(string bucket, string key)
        {
            SendMessageRequest request = new SendMessageRequest
            {
                DelaySeconds = (int)TimeSpan.FromSeconds(5).TotalSeconds,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    {
                    MessageFields.MSG_TYPE_FIELD, new MessageAttributeValue
                    {DataType = "String", StringValue = MessageTypes.NEW_IMAGE_MSG }
                    },
                    {
                    MessageFields.FILE_S3_PATH, new MessageAttributeValue
                    {DataType = "String", StringValue = "s3://" + bucket + "/" + key } //No data types other than string currently supported
                    }
                },
                MessageBody = "{}",
                QueueUrl = Environment.GetEnvironmentVariable("JOB_QUEUE")
            };
            SendMessageResponse response = await SQSClient.SendMessageAsync(request);
            LambdaLogger.Log("Sent message with MessageID " + response.MessageId);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                //TODO this is def the wrong approach
                throw new Exception("Problem sending message to SQS queue");
            }

            return response.HttpStatusCode;
        }
    }
}
