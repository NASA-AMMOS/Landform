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
using OPS.Plumbing;

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

    public class AlignmentWorker : PipelineRoutine
    {
        private AllignmentConfig config;

        //AWS clients. All thread safe and reusable 
        IAmazonSQS SQSClient;

        //thread-safe processing helpers
        IngestPDSImage ingester;
        DetectOverlaps detector;
        StorageHelper storage; 

        //monitoring counts 
        private int messagesRecieved = 0;
        private int messagesSucceeded = 0;
        private int messagesFailed = 0;
        
        //Constructor creates clients and reads config file 
        public AlignmentWorker()
            :  base(null)
        {
            //Initialize our utils
            this.config = new AllignmentConfig();
            Pipeline = new PipelineCore(dynamoPrefix: config.TablePrefix);

            //Initialize AWS utils 
            SQSClient = new AmazonSQSClient(Amazon.RegionEndpoint.USWest1);
            ingester = new IngestPDSImage(Pipeline);
            detector = new DetectOverlaps(Pipeline);
        }

        /// <summary>
        /// Start threads which wait for messages on the ingest queue
        /// </summary>
        /// <returns></returns>
        public int Run()
        {
            //wait on queue for images 
            SQSClient = new AmazonSQSClient(Amazon.RegionEndpoint.USWest1); 

            //check that queue exists 
            if (!PipelineMessage.QueueExists(SQSClient, config.JobQueue))
            {
                Console.WriteLine("Queue does not exist. Quitting landform.");
                return 1;
            }

            //These jobs are CPU intensive - feature detection and matching, for example, use 100% of cpu for short bursts.
            //However, there is also time spent waiting on AWS services.
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
            var indexed = ingester.Ingest(new S3ImageRef(m.Url));
            switch (indexed.Status)
            {
                case (OPS.Pipeline.IngestImage.Status.Skipped):
                    m.DeleteMessage(SQSClient, config.JobQueue);
                    return 0;
                case (OPS.Pipeline.IngestImage.Status.Failed):
                    throw new CloudException("Could not add observation metadata"); //don't delete message, let another handler try again
                case (OPS.Pipeline.IngestImage.Status.Duplicate):
                    if (indexed.Observation.FeatureUrl != null) //Features have already been uploaded
                    { //another worker uploaded features but did not delete, so we don't know if a message was sent
                        new FindOverlapsMessage(indexed.Observation.Name).Send(SQSClient, config.JobQueue);
                        m.DeleteMessage(SQSClient, config.JobQueue);
                        return 0;
                    }
                    break;
            }

            //read project. If indexing was successful we know project exists. 
            Project project = Pipeline.GetProject(indexed.Observation.ProjectName);

            //do keypoint and feature detection 
            //Downloading image. Image.Load() does spooky things with temp files which are mitigated (somewhat) by not using the temp file wrapper
            ImageRef imgRef = new ObservationImageRef(indexed.Observation);

            //snagged from MatchImages
            string gpcafile = PCAKeypointProjector.DefaultTrainingSpace;
            List<PCASIFTFeature> features = new PCASIFTDetector().Detect(GetImage(imgRef), null).Cast<PCASIFTFeature>().ToList();
            PCAKeypointProjector projector = new PCAKeypointProjector(gpcafile, false);
            projector.Project(GetImage(imgRef), features, 1);

            // Create DetectedFeatures data product and save to S3
            DetectedFeatures detected = new DetectedFeatures();
            detected.Features = features.ToArray();
            detected.ObservationName = indexed.Observation.Name;
            Pipeline.Save(indexed.Observation.ProjectName, detected);

            // And RoverMask
            var mask = RoverMask.Build(GetImage(imgRef));
            var maskProd = new ImageDataProduct(mask, "png", typeof(byte));
            Pipeline.Save(indexed.Observation.ProjectName, maskProd);

            //save to Dynamo: 
            //   Image record: S3 location, keypoints product GUID, metadata 
            //   Transforms record: this image's transform 
            //Use the observation we made or found while indexing metadata
            indexed.Observation.FeaturesGuid = detected.guid;
            indexed.Observation.MaskGuid = maskProd.guid;
            indexed.Observation.Save(Pipeline.DynamoDB);

            //Start an overlap job in the queue. 
            //Make it invisible for a few seconds so that overlaps don't have to do strongly consistent reads. 
            new FindOverlapsMessage(indexed.Observation.Name).Send(SQSClient, config.JobQueue);
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
            RoverObservation thisobs = RoverObservation.Find(Pipeline.DynamoDB, MSLProject.PROJECT_NAME, m.ObservationName);
            //for now, look at all other images in Dynamo for this same project 
            IEnumerable<RoverObservation> observations = Pipeline.DynamoDB.Scan<RoverObservation>(new ScanCondition("ProjectName",
                Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, MSLProject.PROJECT_NAME));

            foreach(var overlap in detector.Run(observations.Cast<Observation>().ToList()))
            {
                new MatchPairsMessage(overlap.Observations.Obs1, overlap.Observations.Obs2, overlap.ProjectName).Send(SQSClient, config.JobQueue);
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
            Observation obs0 = Observation.Find(Pipeline.DynamoDB, m.ProjectName, m.ObservationName0);
            Observation obs1 = Observation.Find(Pipeline.DynamoDB, m.ProjectName, m.ObservationName1);
            Project project = Project.Find(Pipeline.DynamoDB, obs0.ProjectName);

            //get overlap and check that a match has not already been uploaded 
            Overlap overlap = Overlap.Find(Pipeline.DynamoDB, obs0.Name, obs1.Name, obs0.ProjectName);
            if (overlap == null) throw new CloudException("Could not find overlap between these two observations for match images");
            if (overlap.MatchGuid != null) //someone has already finished this request
            {
                m.DeleteMessage(SQSClient, config.JobQueue);
                return 0;
            }


            ImageRef modelRef = new ObservationImageRef(obs0);
            ImageRef dataRef = new ObservationImageRef(obs1);
            DetectedFeatures modelFeat = Pipeline.Get<DetectedFeatures>(project.Name, obs0.FeaturesGuid);
            DetectedFeatures dataFeat = Pipeline.Get<DetectedFeatures>(project.Name, obs1.FeaturesGuid);

            //below is from MatchImages.cs
            BruteForceMatcher matcher = new BruteForceMatcher();
            ImagePairCorrespondence matches = matcher.Match(modelRef, dataRef, modelFeat.Features, dataFeat.Features);

            MatchingContext ctx = new MatchingContext();
            ctx.DetectedFeatures[modelRef] = modelFeat.Features;
            ctx.DetectedFeatures[dataRef] = dataFeat.Features;
            ctx.Correspondences[new UnorderedImagePair(modelRef, dataRef)] = matches;

            MoisanStivalFilter filter = new MoisanStivalFilter(Pipeline);
            matches = filter.Filter(ctx, matches);
            if (matches == null || matches.DataToModel.Length < 8) //Filters break with too few matches. Issue #91
            {
                Console.WriteLine("No matches found after MoisanStivalFilter");
                m.DeleteMessage(SQSClient, config.JobQueue);
                return 0;
            }
            GTM gtm = new GTM(5);
            matches = gtm.Filter(ctx, matches);
            if (matches == null)
            {
                Console.WriteLine("No matches found after GTM Filter");
                m.DeleteMessage(SQSClient, config.JobQueue);
                return 0;
            }

            ComputedCorrespondence corr = new ComputedCorrespondence();
            corr.Correspondence = matches;
            Pipeline.Save(overlap.ProjectName, corr);
            if (!overlap.TrySave(Pipeline.DynamoDB))
            {
                return 0; //another worker tried to update this overlap. allow message to return to queue
            }

            //we know we computed the match and uploaded it and its location 
            m.DeleteMessage(SQSClient, config.JobQueue);
            return 0;
        }
    }
}
