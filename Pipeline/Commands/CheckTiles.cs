using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud;
using CommandLine;
using System.Threading;
using System.Collections.Concurrent;
using Amazon.S3;
using Amazon.S3.Model;
using System.IO;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace OPS.Pipeline
{

    [Verb("checktiles", HelpText = "Check S3 folder for missing tiles. Enter queue requests for any missing tiles")]
    public class CheckTilesOptions
    {
        [Option(Required = true, HelpText = "Name of the aws profile to use to authenticate")]
        public string AwsProfile { get; set; }

        [Option(Required = true, HelpText = "Bucket containing tiles")]
        public string BucketName { get; set; }

        [Option(Required = true, HelpText = "Path to tile folder")]
        public string Prefix { get; set; }

        [Option(Required = true, HelpText = "HTTP address of queue for tile processing requests")]
        public string QueueUrl { get; set; }
    }

    public class CheckTiles
    {
        public CheckTilesOptions options;

        public IAmazonSQS SQSClient;

        public IAmazonS3 S3Client;

        //All keys that we've fetched so far 
        private ConcurrentBag<string> keysPresent;

        public CheckTiles(CheckTilesOptions options)
        {
            this.options = options;
            SQSClient = new AmazonSQSClient(Credentials.Get(options.AwsProfile), Amazon.RegionEndpoint.USWest1); //TODO should pull region from somewhere?
            S3Client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1);
        }

        //Wrapper allows asynchronous code to be called as a console program 
        public int Run()
        {
            //TODO check validity of bucket, prefix, and queue name 
            Task.WaitAll(asyncRun());
            return 0;
        }

        //listing objects modeled off http://docs.aws.amazon.com/AmazonS3/latest/dev/ListingObjectKeysUsingNetSDK.html
        public async Task<int> asyncRun()
        {

            ListObjectsV2Request request = new ListObjectsV2Request
            {
                BucketName = options.BucketName,
                Prefix = options.Prefix
            };
            ListObjectsV2Response response;
            List<Task> tasks = new List<Task>();
            do
            {
                response = S3Client.ListObjectsV2(request);
                foreach (S3Object obj in response.S3Objects)
                {
                    keysPresent.Add(obj.Key);
                    tasks.Add(processObject(obj));
                    tasks[tasks.Count - 1].ConfigureAwait(false);
                }
                request.ContinuationToken = response.NextContinuationToken; //Get some new items! 
            } while (response.IsTruncated);

            await Task.WhenAll(tasks);
            return 0;
        }

        private async Task processObject(S3Object obj)
        {
            Console.WriteLine("key: " + obj.Key);
            string prefix = obj.Key.Substring(0, obj.Key.Length - 5); //TODO: assumes a 3-char suffix 
            for (int i =0; i<4; i++)
            {
                if (!keysPresent.Contains(prefix + Convert.ToString(i) + ".txt")) //...specifically depends on a .txt char ending. w e l p 
                {

                }
            }

        }

        private async Task<bool> objectExists(string bucket, string key)
        {
            //Try to get this object. According to https://forums.aws.amazon.com/message.jspa?messageID=215792, this is faster than listing with key as prefix
            using (IAmazonS3 client = new AmazonS3Client(Amazon.RegionEndpoint.USEast1))
                try
                {
                    GetObjectMetadataResponse data = await client.GetObjectMetadataAsync(bucket, key);
                }
                catch (AmazonS3Exception e) //If this is a NoSuchKey exception, the object does not exist 
                {
                    if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        return false; //if e.ErrorCode == "NotFound"
                    }
                    throw; //otherwise this is some other error 
                }
            return true;
        }

    }
}
