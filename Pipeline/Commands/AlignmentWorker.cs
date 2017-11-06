using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
using OPS.Alignment;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace OPS.Pipeline
{

    [Verb("alignmentworker", HelpText = "Poll image queue. When new images appear, upload their metadata and potential overlaps to DynamoDB. Requires an allignmentworker config file. ")]
    public class AlignmentWorkerOptions
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

        public string TablePrefix { get; set; }

        protected override string ConfigFilename()
        {
            return "alignmentworker";
        }
    }

    public class AlignmentWorker
    {
        private AllignmentConfig config;

        //AWS clients. All thread safe and reusable 
        IAmazonSQS SQSClient;
        IAmazonS3 S3Client;
        IAmazonDynamoDB DDBClient;
        DynamoDBContext context;

        //thread-safe processing helpers
        MetadataIndexer indexer;
        OverlapDetection detector;
        StorageHelper storage; //TODO seems thread safe, but I'm not 100% sure

        //monitoring counts 
        private int messagesRecieved = 0;
        private int messagesSucceeded = 0;
        private int messagesFailed = 0;

        //Constructor creates clients and reads config file 
        public AlignmentWorker()
        {
            //Initialize our utils
            this.config = new AllignmentConfig();

            //Initialize AWS utils 
            SQSClient = new AmazonSQSClient(Amazon.RegionEndpoint.USWest1); 
            S3Client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1);
            DDBClient = new AmazonDynamoDBClient(Amazon.RegionEndpoint.USWest1);
            context = new DynamoDBContext(DDBClient, new DynamoDBContextConfig { TableNamePrefix = config.TablePrefix});

            storage = new StorageHelper();
            indexer = new MetadataIndexer(context, new StorageHelper());
            detector = new OverlapDetection(context);
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
                        PipelineMessage m = PipelineMessage.FromMessage(r.Messages[0]);
                        Console.WriteLine(".....Message recieved:"
                            + "\r\n        Message ID = " + m.MessageId);
                        try
                        {
                            switch (m.MessageType)
                            {
                                case NewObservationMsg.TYPE:
                                    IngestImage((NewObservationMsg)m);
                                    break;
                                case FindOverlapsMsg.TYPE:
                                    FindOverlaps((FindOverlapsMsg)m);
                                    break;
                                //case AlignmentMessageTypes.MATCH_PAIR_MSG:
                                //    break;
                            }
                            Interlocked.Increment(ref messagesSucceeded);
                        }
                        catch (Exception e)  
                        {
                            Interlocked.Increment(ref messagesFailed);
                            string msg = "Processing failed for message " + m.MessageId + " of type " + m.MessageType
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
        public int IngestImage(NewObservationMsg m)
        {
            //Index metadata, look up or calculate transforms 
            S3Url url = new S3Url(m.Url); 
            MetadataIndexerStatus indexed = indexer.IndexMetadata(url.Url);
            switch (indexed.status)
            {
                case (Status.SKIPPED):
                    m.DeleteMessage(SQSClient, config.JobQueue);
                    return 0;
                case (Status.FAILEDTOADD):
                    throw new CloudException("Could not add observation metadata"); //don't delete message, let another handler try again
                case (Status.PREEXISTING):
                    if (indexed.obs.FeatureUrl != null) //Features have already been uploaded
                    {
                        m.DeleteMessage(SQSClient, config.JobQueue);
                        return 0;
                    }
                    break;
            }

            //do keypoint and feature detection 
            //Downloading image. Image.Load() does spooky things with temp files which are mitigated (somewhat) by not using the temp file wrapper
            string root = (@"C:\tmp\in\" + Guid.NewGuid()).Replace('/', '\\');
            storage.DownloadFile(url.Url, root + Path.GetExtension(url.Url)); //TODO metadata indexing opens a stream. Is it signifiantly more efficient to download file there?
            Image im = Image.Load(root + Path.GetExtension(url.Url));

            IFeatureDetector detector = new SIFT(); //TODO which detector? 
            var features = detector.Detect(im);
            S3Url featureUrl = new S3Url(url.BucketName, Path.ChangeExtension(url.Prefix, ".json")); //TODO think about this

            //save keypoints and features to S3
            TemporaryFile.GetAndDelete(".json", temp =>
            {
                using (StreamWriter file = File.CreateText(temp))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Serialize(file,features);
                }
                storage.UploadFile(temp, featureUrl.Url);
            });


            //save to Dynamo: 
            //   Image record: S3 locaiton, S3 keypoints location, metadata 
            //   Transforms record: this image's transform 
            //Use the observation we made or found while indexing metadata
            //TODO could reduce dynamo writes by not writing the observation until here
            indexed.obs.FeatureUrl = featureUrl.Url;
            try 
            {
                context.Save(indexed.obs);
            }
            catch (AmazonDynamoDBException e)
            {
                if (e.ErrorCode == "ConditionalCheckFailedException") throw new CloudException("Simultaneous edit attempted, try again");
                else throw e;
            }

            //Start an overlap job in the queue. 
            //Make it invisible for a few seconds so that overlaps don't have to do strongly consistent reads. 
            FindOverlapsMsg.Send(SQSClient, indexed.obs.Name, config.JobQueue);
            m.DeleteMessage(SQSClient, config.JobQueue);

            return 0; 
        }

        public int FindOverlaps(FindOverlapsMsg m)
        {
            //for this image, look up nearby images in Dynamo
            RoverObservation thisobs = RoverObservation.Find(context, MSLProject.PROJECT_NAME, m.ObservationName);
            //for now, look at all other images in Dynamo for this same project 
            IEnumerable<RoverObservation> observations = context.Scan<RoverObservation>(new ScanCondition("ProjectName",
                Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, MSLProject.PROJECT_NAME));

            //check all nearby images for overlapping frusta 

            foreach (RoverObservation obs in observations)
            {
                bool outcome = false;
                if (thisobs.Name == obs.Name)
                {
                    outcome = detector.ProjectiveFrustumOverlap(thisobs, obs);
                }
                else
                {
                    outcome = detector.ProjectiveFrustumOverlap(thisobs, obs);
                }


            }

            //write message for each potential overlap 

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
