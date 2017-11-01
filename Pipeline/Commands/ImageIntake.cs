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

        public string FramesDb { get; set; }

        public string ImagesDb { get; set; }

        public string SectorsDb { get; set; }

        protected override string ConfigFilename()
        {
            return "alignmentworker";
        }
    }

    public class ImageIntake
    {
        private AllignmentConfig config;

        //AWS clients. All thread safe and reusable 
        public IAmazonSQS SQSClient;
        public IAmazonS3 S3Client;
        public IAmazonDynamoDB DDBClient;
        public DynamoDBContext Context;

        //monitoring counts 
        private int messagesRecieved = 0;
        private int messagesSucceeded = 0;
        private int messagesFailed = 0;

        static Dictionary<RoverProductType, ObservationType> productTypeToObservationType = new Dictionary<RoverProductType, ObservationType>();

        //Constructor creates clients and reads config file 
        public ImageIntake()
        {
            this.config = new AllignmentConfig();
            SQSClient = new AmazonSQSClient(Amazon.RegionEndpoint.USWest1); 
            S3Client = new AmazonS3Client(Amazon.RegionEndpoint.USWest1);
            DDBClient = new AmazonDynamoDBClient(Amazon.RegionEndpoint.USWest1);
            Context = new DynamoDBContext(DDBClient);
            
            //Some initialization from CrawlMSL
            productTypeToObservationType.Add(RoverProductType.Image, ObservationType.Image);
            productTypeToObservationType.Add(RoverProductType.Range, ObservationType.Points);
            productTypeToObservationType.Add(RoverProductType.XYZ, ObservationType.Points);
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

            Parallel.For(0, 1, (int i) => //Gather a max of 8 messages at once. TODO should be configurable
            {
                StorageHelper storage = new StorageHelper(); //Not thread safe 
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
                                    IngestImage(storage, m);
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
        public int IngestImage(StorageHelper storage, Message m)
        {
            Thread.Sleep(1000);
            //Should we index, based on the file name? 
            S3Url url = new S3Url(m.MessageAttributes[MessageFields.FILE_S3_PATH].StringValue);
            if (ShouldDownloadHeader(url.Url)){
                //read header data 
                IndexMetadata(storage, url.Url);
            }

            

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

        /// <summary>
        /// Shamelessly snagged from CrawlMSL
        /// </summary>
        /// <param name="storage"></param>
        /// <param name="url"></param>
        void IndexMetadata(StorageHelper storage, string url)
        {
            storage.GetStorageStream(url, stream =>
            {
                string status = "";
                try
                {
                    PDSMetadata metadata = new PDSMetadata(stream);
                    PDSParser parser = new PDSParser(metadata);
                    if (ShouldIndexBasedOnMetadata(parser))
                    {
                        Console.WriteLine("My observation name was " + ObservationName(parser));
                        /*
                            Project project = Project.Find(context, MSLProject.PROJECT_NAME);
                            SiteDrive sd = new SiteDrive(parser.Site, parser.Drive);

                            Frame siteDriveFrame = Frame.FindOrCreate(context, project, SiteDriveFrameName(parser));
                            Frame observationFrame = Frame.FindOrCreate(context, project, ObservationFrameName(parser));
                            Frame rootFrame = Frame.Find(context, project, MSLProject.ROOT_FRAME_NAME);
                            Quaternion roverToLocalLevel = parser.RoverOriginRotation;
                            if (FrameTransform.Find(context, observationFrame, siteDriveFrame).FirstOrDefault() == null)
                            {
                                FrameTransform observationToSiteDrive = FrameTransform.Create(context, observationFrame, siteDriveFrame, Vector3.Zero, roverToLocalLevel, TransformSource.Prior, 0);
                            }
                            var loc = locations.Location(sd);
                            if (loc != null && FrameTransform.Find(context, siteDriveFrame, rootFrame).FirstOrDefault() == null)
                            {
                                FrameTransform siteDriveToRoot = FrameTransform.Create(context, siteDriveFrame, rootFrame, loc.Position, Quaternion.Identity, TransformSource.Prior, 0.5);
                            }
                            
                            string observationName = ObservationName(parser);
                            Observation observation = RoverObservation.Find(this.Context, observationName);
                            if (observation == null)
                            {
                                string cameraModel = JsonHelper.ToJson(metadata.CameraModel);
                                observation = RoverObservation.Create(Context, observationFrame, observationName, url, productTypeToObservationType[parser.DerivedImageType].ToString(), cameraModel, UseForReconstruction(parser, metadata), parser.Site, parser.Drive, parser.ProductId.Version, parser.Camera.ToString(), parser.ImageSizeType.ToString());
                                if (observation != null)
                                {
                                    status = "Add";
                                }
                                else
                                {
                                    status = "Failed to add";
                                }
                            }
                            else
                            {
                                status = "Exists";
                            }
                            */
                    }
                    else
                    {
                        status = "Skipped(metadata)";
                    }

                }
                catch (Exception e)
                {
                    status = "Failed " + e.Message;
                }
                Console.WriteLine(url + "\t" + status);
            });
        }
        
        /// ~~~~~~~~~~~~ from CrawlMSL ~~~~~~~~~~~~~

        /// <summary>
        /// Mostly just confirms what ShouldDownloadHeader did using metadata instead of the filename
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        bool ShouldIndexBasedOnMetadata(PDSParser parser)
        {
            return productTypeToObservationType.ContainsKey(parser.DerivedImageType) &&
                    parser.ImageSizeType == RoverProductSize.Regular;
        }

        /// <summary>
        /// Map metadata to a frame name based on site drive
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        public string SiteDriveFrameName(PDSParser parser)
        {
            return parser.SiteDrive;
        }

        /// <summary>
        /// Map metadata to an observation frame name based on RMC
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        public string ObservationFrameName(PDSParser parser)
        {
            return parser.Camera.ToString() + "_" + parser.RMC;
        }

        /// <summary>
        /// Map metadata to an observation name based on product id
        /// </summary>
        /// <param name="parser"></param>
        /// <returns></returns>
        public string ObservationName(PDSParser parser)
        {
            return parser.ProductIdString;
        }

        /// <summary>
        /// Decide if this is a file we should index from what can be dervied from the filename
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        bool ShouldDownloadHeader(string url)
        {
            string filename = Path.GetFileName(url);
            RoverProductId id = RoverProductId.ParseFromString(filename);
            if (id == null)
            {
                return false;
            }
            if (id.Camera == RoverProductCamera.Unknown)
            {
                return false;
            }
            if (id.ProductType == RoverProductType.Unknown)
            {
                return false;
            }
            if (id.Producer == RoverProductProducer.OPGS)
            {
                OPGSProductId opgsId = (OPGSProductId)id;
                if (opgsId.Size != RoverProductSize.Regular)
                {
                    return false;
                }
            }
            if (id.Producer == RoverProductProducer.MSSS)
            {
                // Check that this is a DCX file
                MSSSProductId msssId = (MSSSProductId)id;
                if (!msssId.RadiometricallyCalibrated || !msssId.ColorCorrected || !msssId.Decompressed)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
