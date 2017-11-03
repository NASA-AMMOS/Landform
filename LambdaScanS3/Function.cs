using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

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

        private const int NUM_SEQUENTIAL = 50; //if we do many more messages than this at once, we run out of memory
        private string lastRequestId; //if this lambda retries, we hope we're in the same env and can see we're a retry 

        private List<String> Failures; 

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
        /// Lists all objects in the requested bucket with the requested prefix. 
        /// Warning/TODO: If this fails (eg out of memory or out of time), it will retry itself, and listing starts over from The Beginning
        ///     Lambda retry policies can't be disabled or edited :|
        /// Run me from the command line like: aws lambda invoke --function-name mango-S3Scan-12OKSXXEPPL65 --payload "{\"Bucket\":\"landlords-dev\",\"Prefix\":\"gailin-alignment/images/\"}" C:\Users\gpease\Desktop\output.txt
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<string> FunctionHandler(ScanRequest s3url, ILambdaContext context)
        {
            //Hack from https://forums.aws.amazon.com/thread.jspa?messageID=643046
            //This quits retries IFF we are using the same env. It will not work 100% of the time. 
            if (lastRequestId == context.AwsRequestId)
            {
                LambdaLogger.Log("I'm a retry, quitting");
                return "quit";
            }
            else
            {
                lastRequestId = context.AwsRequestId;
            };

            Failures = new List<string>(); //start with a new list of failures for this invocation

            //scan all items at that path
            ListObjectsV2Request request = new ListObjectsV2Request()
            {
                BucketName = s3url.Bucket,
                Prefix = s3url.Prefix,
                MaxKeys = 80
            };
            ListObjectsV2Response response;

            do
            {
                Stopwatch time = Stopwatch.StartNew();
                response = await S3Client.ListObjectsV2Async(request);
                time.Stop();
                LambdaLogger.Log("time spend requesting messages: " + time.Elapsed);
                //add item to queue for each message 
                time.Reset();
                time.Start();
                List<Task<System.Net.HttpStatusCode>> running = new List<Task<System.Net.HttpStatusCode>>();
                foreach (S3Object entry in response.S3Objects)
                {
                    if (running.Count == NUM_SEQUENTIAL) //stop and process these before we get any more
                    {
                        Task.WaitAll(running.ToArray());
                        running.Clear();
                        time.Stop();
                        LambdaLogger.Log("time spent sending to queue: " + time.Elapsed);
                        time.Reset(); time.Start();
                    }
                    //add to array
                    if (entry.Size > 0)
                    {
                        running.Add(SendMessage(entry.BucketName, entry.Key));
                    }
                }

                Task.WaitAll(running.ToArray());
                time.Stop();
                LambdaLogger.Log("time spent sending to queue: " + time.Elapsed);
                request.ContinuationToken = response.NextContinuationToken;
                
            } while (response.IsTruncated == true);

            //put messages for all valid images in our job queue
            LambdaLogger.Log(s3url.Prefix);

            return Failures.ToString();
        }

        private async Task<System.Net.HttpStatusCode> SendMessage(string bucket, string key)
        {
            string address = "s3://" + bucket + "/" + key;

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
                    {DataType = "String", StringValue = address } 
                    }
                },
                MessageBody = "{}",
                QueueUrl = Environment.GetEnvironmentVariable("JOB_QUEUE")
            };
            SendMessageResponse response = await SQSClient.SendMessageAsync(request);

            if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
            {
                Failures.Add(address);
            }

            return response.HttpStatusCode;
        }
    }
}
