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
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2;
using OPS.Util;

namespace OPS.Pipeline
{

    [Verb("imageintake", HelpText = "Poll image queue. When new images appear, upload their metadata and potential overlaps to DynamoDB. Requires an allignmentworker config file. ")]
    public class ImageIntakeOptions
    {
    }

    /// <summary>
    /// Stack configuration specifies the other resources in this worker's stack relevant to this worker. 
    /// For dev - set in a file in the user's home directory/.landform/pipelineworker.json
    /// In AWS deployment, this file is created by the autoscale group configuration in UserData, executed whenever a machine starts up. 
    /// </summary>
    class AllignmentConfig : Config
    {
        public string JobQueue { get; set; }

        public string PipelineName { get; set; }

        public string FailureSns { get; set; }

        protected override string ConfigFilename()
        {
            return "allignmentworker";
        }
    }

    public class ImageIntake
    {
        public ImageIntakeOptions options;

        public IAmazonSQS SQSClient;

        public IAmazonS3 S3Client;

        //All keys that we've fetched so far 
        private ConcurrentBag<string> keysPresent;

        public ImageIntake(ImageIntakeOptions options)
        {
            this.options = options;
            SQSClient = new AmazonSQSClient(Amazon.RegionEndpoint.USWest1); 
            S3Client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1);
        }

        /// <summary>
        /// Start threads which wait for messages on the ingest queue. 
        /// Pattern matching as much as possible from CrawlMSL 
        /// </summary>
        /// <returns></returns>
        public int Run()
        {
            return 0;
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
