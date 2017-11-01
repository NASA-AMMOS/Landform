using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Cloud.Util;
using OPS.Cloud;
using OPS.Imaging;
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
using Microsoft.Xna.Framework;

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

        public string KeyAlias { get; set; }
        

        protected override string ConfigFilename()
        {
            return "alignmentworker";
        }
    }

    public class ImageIntake
    {
        private AllignmentConfig config;

        //AWS clients. All thread safe and reusable 
        IAmazonSQS SQSClient;
        IAmazonS3 S3Client;
        IAmazonDynamoDB DDBClient;
        DynamoDBContext context;

        //thread-safe processing helpers
        MetadataIndexer indexer;

        //monitoring counts 
        private int messagesRecieved = 0;
        private int messagesSucceeded = 0;
        private int messagesFailed = 0;

        //Constructor creates clients and reads config file 
        public ImageIntake()
        {
            //Initialize AWS utils 
            SQSClient = new AmazonSQSClient(Amazon.RegionEndpoint.USWest1); 
            S3Client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1);
            DDBClient = new AmazonDynamoDBClient(Amazon.RegionEndpoint.USWest1);
            context = new DynamoDBContext(DDBClient);

            //Initialize our utils
            this.config = new AllignmentConfig();
            indexer = new MetadataIndexer(context, new StorageHelper());
        }

        /// <summary>
        /// Start threads which wait for messages on the ingest queue
        /// </summary>
        /// <returns></returns>
        public int Run()
        {
            //Initialize project
            //Project p = Project.FindOrCreate(Context, MSLProject.PROJECT_NAME); TODO implement
            //Frame.FindOrCreate(Context, p, MSLProject.ROOT_FRAME_NAME);

            //wait on queue for images 
            SQSClient = new AmazonSQSClient(Amazon.RegionEndpoint.USWest1); 
            S3Client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1);

            Parallel.For(0, 1, (int i) => //Gather a max of 8 messages at once. TODO should be configurable
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
                            + "\r\n        Message ID = " + m.MessageId);
                        try
                        {
                            switch (m.MessageAttributes[MessageFields.MSG_TYPE_FIELD].StringValue)
                            {
                                case MessageTypes.NEW_IMAGE_MSG:
                                    IngestImage(m);
                                    break;
                                case MessageTypes.FIND_OVERLAPS_MSG:
                                    break;
                                case MessageTypes.MATCH_PAIR_MSG:
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
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public int IngestImage(Message m)
        {
            Thread.Sleep(1000); //slow down for debugging

            //Index metadata 
            S3Url url = new S3Url(m.MessageAttributes[MessageFields.FILE_S3_PATH].StringValue); 
            indexer.IndexMetadata(url.Url);

            

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
            //SEE MATCHALLIMAGES 

            //get metadata for both images from Dynamo 

            //read keypoint and feature data for both images from S3

            //compute mapping between keypoints 

            //save mapping to S3, save address of mapping to Dynamo 
            return 0;
        }
    }
}
