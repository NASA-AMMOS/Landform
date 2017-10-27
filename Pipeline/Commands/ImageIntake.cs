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

        protected override string ConfigFilename()
        {
            return "alignmentworker";
        }
    }

    /// <summary>
    /// Message fields in allignment job queue messages 
    /// Keep in sync with names in LambdaUtil/ImageIntake.cs
    /// </summary>
    public class MessageFields
    {
        public const string MSG_TYPE_FIELD = "MessageType";
        public const string FILE_S3_PATH = "FileS3Path";
    }

    public class ImageIntake
    {
        //Keep up-to-date with names in LambdaUtil. TODO make this better
        public const string NEW_IMAGE_MSG = "NEW_IMAGE";
        public const string FIND_OVERLAPS_MSG = "FIND_OVERLAPS";
        public const string MATCH_PAIR_MSG = "MATCH_PAIR";

        private AllignmentConfig config;

        //AWS clients
        public IAmazonSQS SQSClient;
        public IAmazonS3 S3Client;

        //monitoring counts 
        private int messagesRecieved = 0;
        private int messagesSucceeded = 0;
        private int messagesFailed = 0;

        //All keys that we've fetched so far 
        private ConcurrentBag<string> keysPresent;

        //Constructor creates clients and reads config file 
        public ImageIntake()
        {
            this.config = new AllignmentConfig();
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
            //wait on queue for images 
            SQSClient = new AmazonSQSClient(Amazon.RegionEndpoint.USWest1); 
            S3Client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1);

            Parallel.For(0, 8, (int i) => //Gather a max of 8 messages at once. TODO should be configurable
            {
                while (true)
                {
                    var req = new ReceiveMessageRequest
                    {
                        AttributeNames = new List<string>() { "All" }, //metadata about recieved message - will enable some benchmarking
                        MessageAttributeNames = new List<string>() { "All" }, //attributes we've defined
                        MaxNumberOfMessages = 1,
                        QueueUrl = config.JobQueue,
                        WaitTimeSeconds = (int)TimeSpan.FromSeconds(15).TotalSeconds //how long I'll wait for a message
                    };
                    ReceiveMessageResponse r = SQSClient.ReceiveMessage(req);
                    if (r.Messages.Count > 0) //we have a message
                    {
                        Interlocked.Increment(ref messagesRecieved);
                        Message m = r.Messages[0];
                        Console.WriteLine(".....Message recieved:"
                            + "\r\n        Message ID = " + m.MessageId
                            + "\r\n        URL = " + m.MessageAttributes["ParentPath"].StringValue);
                        try
                        {
                            switch (m.MessageAttributes["JobType"].StringValue)
                            {
                                case NEW_IMAGE_MSG:
                                    IngestImage(m);
                                    break;
                                case FIND_OVERLAPS_MSG:
                                    break;
                                case MATCH_PAIR_MSG:
                                    break;
                            }
                            Interlocked.Increment(ref messagesSucceeded);
                        }
                        catch (Exception e)
                        {
                            Interlocked.Increment(ref messagesFailed);
                            string msg = "Processing failed for message " + m.MessageId + "; additional message info: " + m.MessageAttributes["ParentPath"].StringValue
                                + "\r\n Error msg is: " + e.Message
                                + "\r\n Stack trace is: " + e.StackTrace;
                            Console.WriteLine(msg);
                        }
                    }
                }
            });

            return 0;
        }


        /// <summary>
        /// 
        /// issues: 
        ///   if someone 
        /// 
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public int IngestImage(Message m)
        {
            //read header data 

            //look up or calculate estimated position 

            //do keypoint and feature detection 

            //save keypoints and features to S3

            //save to Dynamo: 
            //   Image record: S3 locaiton, S3 keypoints location, metadata 
            //   Transforms record: this image's transform 

            //Start an overlap job in the queue. 
            //Make it invisible for a few seconds so that overlaps don't have to do strongly consistent reads. 

            return 0; 
        }

        public int FindOverlaps(Message m)
        {
            //for this image, look up nearby images in Dynamo

            //check all nearby images for overlapping frusta 

            //write potentially overlapping images to Dynamo 

            return 0; 
        }

        public int MatchPairs(Message m)
        {
            //for an overlap 
            return 0;
        }
    }
}
