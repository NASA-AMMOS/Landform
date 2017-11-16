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
using Amazon.DynamoDBv2.Model;
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
        //urls for feature and match uploads. TODO configure from somewhere sensible 
        //perhaps from Dynamo project entry, with the thought that the REST API will eventually configure them? 
        private string s3FeatureUrl = "s3://landlords-dev/rotini/features/"; 
        private string s3MatchesUrl = "s3://landlords-dev/rotini/matches/";

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
            
            //TODO: what's the proper parallel situation here? 
            //These jobs are CPU intensive - feature detection and matching, for example, use 100% of cpu for short bursts.
            //However, overlap detection (as it is currently) is a lot of reading from Dynamo but is NOT cpu intensive, so would benefit from parallization 
            Parallel.For(0, 1, (int i) =>  
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
                                case NewObservationMessage.TYPE:
                                    IngestImage((NewObservationMessage)m);
                                    break;
                                case FindOverlapsMessage.TYPE:
                                    FindOverlaps((FindOverlapsMessage)m);
                                    break;
                                case MatchPairsMessage.TYPE:
                                    MatchPairs((MatchPairsMessage)m);
                                    break;
                            }
                            Interlocked.Increment(ref messagesSucceeded);
                        }
                        catch (Exception e)  
                        {
                            Interlocked.Increment(ref messagesFailed);
                            string msg = "Processing failed for message " + m.MessageId + " of type " + m.MessageType
                                + "\r\n Error msg is: " + e.Message
                                + "\r\n Inner exception is: " + e.InnerException
                                + "\r\n Stack trace is: " + e.StackTrace;
                            Console.WriteLine(msg);
                        }
                    }
                }
            });

            return 0; 
        }


        /// <summary>
        /// This task: 
        ///  - gets image metadata from observation header and uploads it to dynamo
        ///  - does feature detection and uploads features to S3
        ///  - updates observation in dynamo with feature URL
        ///  - starts overlaps message with a 60 second delay to allow time for eventual consistency in dynamo 
        ///  - deletes message 
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public int IngestImage(NewObservationMessage m)
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
                    { //another worker uploaded features but did not delete, so we don't know if a message was sent
                        new FindOverlapsMessage(indexed.obs.Name).Send(SQSClient, config.JobQueue);
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

            //snagged from MatchImages
            string gpcafile = PCAKeypointProjector.DefaultTrainingSpace;
            List<PCASIFTFeature> features = new PCASIFTDetector().Detect(im, null).Cast<PCASIFTFeature>().ToList();
            PCAKeypointProjector projector = new PCAKeypointProjector(gpcafile, false);
            projector.Project(im, features, 1);

            S3Url featureUrl = new S3Url(s3FeatureUrl + Path.ChangeExtension(Path.GetFileName(url.Url), ".json")); //TODO think about this

            //save keypoints and features to S3
            TemporaryFile.GetAndDelete(".json", temp =>
            {
                using (StreamWriter file = File.CreateText(temp))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.TypeNameHandling = TypeNameHandling.Auto;
                    serializer.Serialize(file,features);
                }
                storage.UploadFile(temp, featureUrl.Url);
            });


            //save to Dynamo: 
            //   Image record: S3 locaiton, S3 keypoints location, metadata 
            //   Transforms record: this image's transform 
            //Use the observation we made or found while indexing metadata
            //TODO could reduce dynamo writes by not writing the observation until here
            //TODO this kind of direct interaction with Dynamo should be in the Object Persistence classes
            indexed.obs.FeatureUrl = featureUrl.Url;
            try 
            {
                context.Save(indexed.obs);
            }
            catch (ConditionalCheckFailedException)
            {
                return 0; //two workers were working on this task simultaneously. Don't delete message
            }

            //Start an overlap job in the queue. 
            //Make it invisible for a few seconds so that overlaps don't have to do strongly consistent reads. 
            new FindOverlapsMessage(indexed.obs.Name).Send(SQSClient, config.JobQueue);
            m.DeleteMessage(SQSClient, config.JobQueue);

            return 0; 
        }

        /// <summary>
        /// This task: 
        ///  - scans all observations 
        ///  - checks them for potential overlaps using only their metadata 
        ///  - starts a matchpairs message for any observations that might overlap 
        /// On failure (eg, a worker crash), another worker will pick up the task later. 
        /// Some overlap messages will be repeated, but none will be missed. 
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public int FindOverlaps(FindOverlapsMessage m)
        {
            //for this image, look up nearby images in Dynamo
            RoverObservation thisobs = RoverObservation.Find(context, MSLProject.PROJECT_NAME, m.ObservationName);
            //for now, look at all other images in Dynamo for this same project 
            IEnumerable<RoverObservation> observations = context.Scan<RoverObservation>(new ScanCondition("ProjectName",
                Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, MSLProject.PROJECT_NAME));

            //check all nearby images for overlapping frusta. TODO only check spacially nearby observations
            foreach (RoverObservation obs in observations)
            {
                bool outcome = false;
                if (thisobs.Name != obs.Name)
                {
                    outcome = detector.ProjectiveFrustumOverlap(thisobs, obs);
                }
                Console.WriteLine("Overlap: " + outcome);
                if (outcome)
                {
                    //write to dynamoDb and, if successful, create a new MatchPairs job
                    if (Overlap.Create(context, thisobs.Name, obs.Name, obs.ProjectName) != null) 
                    {
                        new MatchPairsMessage(thisobs.Name, obs.Name, obs.ProjectName).Send(SQSClient, config.JobQueue);
                    }
                }

            }

            //delete message 
            m.DeleteMessage(SQSClient, config.JobQueue);

            return 0; 
        }

        /// <summary>
        /// This task: 
        ///  - Reads metadata from Dynamo and data products from S3 for two images 
        ///  - Finds a match
        ///  - Uploads the match to S3
        ///  - Uploads the match location to the Dynamo entry for this overlap 
        ///  - Deletes the message
        /// If a worker crashes while working on this task, the message will return to the queue and another worker will repeat the work.
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public int MatchPairs(MatchPairsMessage m)
        {
            //get metadata for both images from Dynamo 
            Observation obs0 = Observation.Find(context, m.ProjectName, m.ObservationName0);
            Observation obs1 = Observation.Find(context, m.ProjectName, m.ObservationName1);

            //get overlap and check that a match has not already been uploaded 
            Overlap overlap = Overlap.Find(context, obs0.Name, obs1.Name, obs0.ProjectName);
            if (overlap == null) throw new CloudException("Could not find overlap between these two observations for match images");
            if (overlap.MatchUrl != null && overlap.MatchUrl.Length > 0) //someone has already finished this request
            {
                m.DeleteMessage(SQSClient, config.JobQueue);
                return 0;
            }
            //read feature data and image for both images from S3
            JsonSerializer serializer = new JsonSerializer();
            serializer.TypeNameHandling = TypeNameHandling.Auto;
            TemporaryFile.GetAndDeleteMultiple(new string[5]{ ".json", ".img", ".json", ".img", ".jpg"}, (temp) =>
            {
                storage.DownloadFile(obs0.FeatureUrl, temp[0]);
                IEnumerable<SIFTFeature> features0; 
                using (JsonReader file = new JsonTextReader(File.OpenText(temp[0])))
                {
                    features0 = serializer.Deserialize<IEnumerable<SIFTFeature>>(file); //TODO problem: the FeatureDescriptor in the feature is a PCASIFT descriptor as created by ImageIntake 
                }
                storage.DownloadFile(obs0.Url, temp[1]);
                Image im0 = Image.Load(temp[1]);
                storage.DownloadFile(obs1.FeatureUrl, temp[2]);
                IEnumerable<SIFTFeature> features1;
                using (JsonReader file = new JsonTextReader(File.OpenText(temp[2])))
                {
                    features1 = serializer.Deserialize<IEnumerable<SIFTFeature>>(file);
                }
                storage.DownloadFile(obs1.Url, temp[3]);
                Image im1 = Image.Load(temp[3]);

                //below is from MatchImages.cs
                BruteForceMatcher matcher = new BruteForceMatcher();
                ImagePairCorrespondence matches = matcher.Match(new ImageRef(im0), new ImageRef(im1), features0, features1);

                MoisanStivalFilter filter = new MoisanStivalFilter();
                matches = filter.Filter(matches);
                if (matches == null || matches.DataToModel.Length < 8) //TODO the next filter breaks if there are too few matches idk man
                {
                    Console.WriteLine("No matches found after MoisanStivalFilter");
                    m.DeleteMessage(SQSClient, config.JobQueue); //TODO if no matches, might be nice to delete Overlaps entry
                    return;
                }
                GTM gtm = new GTM(5);
                matches = gtm.Filter(matches); //an example pairing to replicate this breaking: 0601ML0025370360301244E01_DRCX x 0604ML0025490030301399D01_DRCX
                if (matches == null)
                {
                    Console.WriteLine("No matches found after GTM Filter");
                    m.DeleteMessage(SQSClient, config.JobQueue);
                    return;
                }
                
                MatchImage.WriteMatchImage(matches, temp[4]);
                string url = s3MatchesUrl + overlap.Id + ".jpg";
                storage.UploadFile(temp[4], url);
                overlap.MatchUrl = url;
                try
                {
                    context.Save(overlap);
                }
                catch (ConditionalCheckFailedException)
                {
                    return; //another worker tried to update this overlap. Allow message to return to queue 
                }
                //we know we computed the match and uploaded it and its location 
                m.DeleteMessage(SQSClient, config.JobQueue);
            });
            return 0;
        }
    }
}
