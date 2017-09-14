using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using CommandLine;
using System.Threading;
using Amazon.S3;
using Amazon.S3.Model;
using System.IO;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace OPS.Pipeline
{

    [Verb("queuelisten", HelpText = "Listen for messages on an SQS queue")]
    public class QueueListenerOptions
    {
        [Option(Required = true, HelpText = "Name of the aws profile to use to authenticate")]
        public string AwsProfile { get; set; }

        [Option(Required = true, HelpText = "HTTP address of the queue")]
        public string QueueUrl { get; set; }
    }

    public class QueueListener
    {
        public QueueListenerOptions options;

        public IAmazonSQS SQSClient;

        public IAmazonS3 S3Client; 

        private const string BUCKET_NAME = "landlords-dev";

        public QueueListener(QueueListenerOptions options)
        {
            this.options = options;
        }

        //Wrapper allows asynchronous code to be called as a console program 
        public int Run()
        {
            //TODO check that the given queue name is valid before we wait around a long time 
            SQSClient = new AmazonSQSClient(Credentials.Get(options.AwsProfile), Amazon.RegionEndpoint.USWest1); //TODO should pull region from somewhere?
            S3Client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1);
            Task.WaitAll(asyncRun());
            return 0;
        }

        public async Task<int> asyncRun()
        {
            while (true)
            {
                var req = new ReceiveMessageRequest
                {
                    AttributeNames = new List<string>() { "All" }, //metadata about recieved message - will enable some benchmarking
                    MessageAttributeNames = new List<string>() { "All"},
                    MaxNumberOfMessages = 1,
                    QueueUrl = options.QueueUrl,
                    WaitTimeSeconds = (int)TimeSpan.FromSeconds(15).TotalSeconds //how long I'll wait for a message
                };
                ReceiveMessageResponse r = await SQSClient.ReceiveMessageAsync(req);  
                if (r.Messages.Count > 0) //we have a message
                {
                    Console.WriteLine(".....Message recieved:");
                    Message m = r.Messages[0];
                    Console.WriteLine("        Message ID = " + m.MessageId);
                    foreach (var kv in m.Attributes)
                    {
                        Console.WriteLine("        Key: " + kv.Key + " : " + kv.Value);
                    }

                        string key = m.MessageAttributes["Prefix"].StringValue; //TODO API 
                    await processFile(key);

                    //message is still in queue until we tell it to delete 
                    var delRequest = new DeleteMessageRequest
                    {
                        QueueUrl = options.QueueUrl,
                        ReceiptHandle = m.ReceiptHandle
                    };

                    var delResponse = await SQSClient.DeleteMessageAsync(delRequest);
                    Console.WriteLine(".....Message deleted.");
                }
            }
            

            return 0;
        }

        //Someday I might do actual work, for now I just pretend I have 
        private async Task<int> processFile(string prefix)
        {
            await Task.Delay(1000); //wait a sec 
            //maybe we want to read something
            Console.WriteLine(".....Looking up file " + prefix + "0.txt");
            string fileContents = await ReadObjectData(BUCKET_NAME, prefix + "0.txt");

            PutObjectRequest r = new PutObjectRequest
            {
                BucketName = BUCKET_NAME,
                Key = prefix + ".txt",
                ContentBody = "I was added by a robot!"
            };
            PutObjectResponse response = await S3Client.PutObjectAsync(r);
            return 0; //TODO check response 
        }

        //modeled off http://docs.aws.amazon.com/AmazonS3/latest/dev/RetrievingObjectUsingNetSDK.html
        //Get an object that we know exists in S3. W
        public async Task<string> ReadObjectData(string bucket, string key)
        {
            string responseBody = "";
            GetObjectRequest request = new GetObjectRequest
            {
                BucketName = bucket,
                Key = key
            };

            GetObjectResponse response = null;
            for (int i = 0; i < 4; i++) //Eventual consistency means we may have to wait for the object to appear
            {
                try
                {
                    response = await S3Client.GetObjectAsync(request);
                }
                catch (AmazonS3Exception e)
                {
                    if (i < 3 && e.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        await Task.Delay(1000); //wait a second. TODO what is the usual time to consistency? 
                        continue; 
                    }
                    throw;
                }
            }

            Stream responseStream = response.ResponseStream;
            StreamReader reader = new StreamReader(responseStream);
            responseBody = reader.ReadToEnd();
            Console.WriteLine(responseBody);
            return responseBody;
        }
    }
}
