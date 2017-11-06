using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Lambda.LambdaUtil;

using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Util;
using Amazon.SQS;
using Amazon.SQS.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace Lambda.LambdaS3ImageIntake
{
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
        /// This method is called for every Lambda invocation. 
        /// Creates new ingest jobs for alignment workers and uploads them to SQS
        /// Only send puts! The lambda can't check that it is not also processing deletions, etc
        /// </summary>
        /// <param name="evnt"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<string> FunctionHandler(S3Event evnt, ILambdaContext context)
        {
            var s3Event = evnt.Records?[0].S3;
            if(s3Event == null)
            {
                return null;
            }

            //send a message. We'll just assume that any kind of product is ok, though we could check file endings or metadata if desired 
            SendMessageRequest request = new SendMessageRequest
            {
                DelaySeconds = (int)TimeSpan.FromSeconds(5).TotalSeconds,
                MessageAttributes = new Dictionary<string, MessageAttributeValue>
                {
                    {
                    AlignmentMessageFields.MSG_TYPE_FIELD, new MessageAttributeValue
                    {DataType = "String", StringValue = AlignmentMessageTypes.NEW_IMAGE_MSG }
                    },
                    {
                    AlignmentMessageFields.FILE_S3_PATH, new MessageAttributeValue
                    {DataType = "String", StringValue = "s3://" + s3Event.Bucket.Name + "/" + s3Event.Object.Key } //No data types other than string currently supported
                    }
                },
                MessageBody = "{}",
                QueueUrl = Environment.GetEnvironmentVariable("JOB_QUEUE")
            };
            SendMessageResponse response = await SQSClient.SendMessageAsync(request);
            LambdaLogger.Log("Sent message with MessageID " + response.MessageId);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                //Something is wrong with the connection. Quit, another lambda will try again 
                throw new Exception("Problem sending message to SQS queue");
            }

            return response.MessageId;
        }
    }
}
