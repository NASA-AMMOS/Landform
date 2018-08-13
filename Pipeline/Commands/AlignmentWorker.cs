using System;
using System.Collections.Generic;
using System.Linq;
using OPS.Cloud;
using CommandLine;
using System.Threading;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.DynamoDBv2.DataModel;
using OPS.Util;
using OPS.Alignment;
using OPS.Plumbing;
using log4net;

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
    class AlignmentConfig : Config
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
        private AlignmentConfig config;

        //AWS clients. All thread safe and reusable 
        IAmazonSQS SQSClient;

        //thread-safe processing helpers
        IngestPDSImage ingester;
        DetectOverlaps detector;

        //monitoring counts 
        private int messagesRecieved = 0;
        private int messagesSucceeded = 0;
        private int messagesFailed = 0;
        
        //Constructor creates clients and reads config file 
        public AlignmentWorker()
            :  base(null)
        {
            //Initialize our utils
            this.config = new AlignmentConfig();
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
            Console.WriteLine("Listening to " + config.JobQueue);

            //These jobs are CPU intensive - feature detection and matching, for example, use 100% of cpu for short bursts.
            //However, there is also time spent waiting on AWS services.
            Serial.For(0, 1, (int i) =>  
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
                            var t = m.GetType();
                            if (t == typeof(NewObservationMessage))
                            {
                                IngestImage((NewObservationMessage)m);
                            }
                            else if(t == typeof(FindOverlapsMessage))
                            {
                                FindOverlaps((FindOverlapsMessage)m);
                            }
                            else if (t == typeof(MatchPairsMessage))
                            {
                                MatchPairs((MatchPairsMessage)m);
                            }
                            else if (t == typeof(BundleAdjustMessage))
                            {
                                BundleAdjust((BundleAdjustMessage)m);
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

        private void BundleAdjust(BundleAdjustMessage m)
        {
            throw new NotImplementedException();
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

            Project project = Pipeline.GetProject(indexed.Observation.ProjectName);
            ImageRef imgRef = new ObservationImageRef(indexed.Observation);

            // Make rover mask
            var mask = RoverMask.Build(GetImage(imgRef));
            var maskProd = new PngDataProduct(mask);
            Save(indexed.Observation.ProjectName, maskProd);
            indexed.Observation.MaskGuid = maskProd.Guid;

            // Detect image features and save to S3
            string gpcafile = PCAKeypointProjector.DefaultTrainingSpace;
            List<PCASIFTFeature> features = new PCASIFTDetector().Detect(GetImage(imgRef), mask).Cast<PCASIFTFeature>().ToList();
            PCAKeypointProjector projector = new PCAKeypointProjector(gpcafile, false);
            projector.Project(GetImage(imgRef), features, 1);

            DetectedFeatures detected = new DetectedFeatures
            {
                Features = features.ToArray(),
                ObservationName = indexed.Observation.Name
            };
            Save(indexed.Observation.ProjectName, detected);
            indexed.Observation.FeaturesGuid = detected.Guid;
            indexed.Observation.MaskGuid = maskProd.Guid;
            indexed.Observation.Save(Pipeline.DynamoContext);
            
            // TODO:
            // send ObservationAdded message

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
            RoverObservation thisobs = RoverObservation.Find(Pipeline.DynamoContext, "MSL", m.ObservationName);
            //for now, look at all other images in Dynamo for this same project 
            IEnumerable<RoverObservation> observations = Pipeline.DynamoContext.Scan<RoverObservation>(new ScanCondition("ProjectName",
                Amazon.DynamoDBv2.DocumentModel.ScanOperator.Equal, "MSL"));

            foreach(var overlap in detector.Run(observations.Cast<Observation>().ToList()))
            {
                new MatchPairsMessage(overlap.ObservationNameOne, overlap.ObservationNameTwo, overlap.ProjectName).Send(SQSClient, config.JobQueue);
            }
            //delete message 
            m.DeleteMessage(SQSClient, config.JobQueue);

            return 0; 
        }
        
        private bool DoMatch(Overlap overlap, Observation obs0, Observation obs1, Project project, MatchPairsMessage m)
        {
            ImageRef modelRef = new ObservationImageRef(obs0);
            ImageRef dataRef = new ObservationImageRef(obs1);
            DetectedFeatures modelFeat = Get<DetectedFeatures>(project.Name, obs0.FeaturesGuid);
            DetectedFeatures dataFeat = Get<DetectedFeatures>(project.Name, obs1.FeaturesGuid);



            AlignmentScene scene = new AlignmentScene();
            scene.DetectedFeatures[modelRef] = modelFeat.Features;
            scene.DetectedFeatures[dataRef] = dataFeat.Features;

            var pair = new UnorderedImagePair(modelRef, dataRef);
            IFeatureMatcher matcher = new BruteForceMatcher();
            var matches = matcher.Match(scene, pair);
            
            if (matches == null)
            {
                Console.WriteLine("No matches found (at all)");
                return false;
            }
            scene.Correspondences[pair] = matches;

            MoisanStivalFilter filter = new MoisanStivalFilter(Pipeline);
            matches = filter.Filter(scene, matches);
            if (matches == null || matches.DataToModel.Length < 8) //Filters break with too few matches. Issue #91
            {
                Console.WriteLine("No matches found after MoisanStivalFilter");
                return false;
            }
            GTMFilter gtm = new GTMFilter(5);
            matches = gtm.Filter(scene, matches);
            if (matches == null)
            {
                Console.WriteLine("No matches found after GTM Filter");
                return false;
            }

            ComputedCorrespondence corr = new ComputedCorrespondence
            {
                Correspondence = matches,
                ModelFeaturesGuid = modelFeat.Guid,
                DataFeaturesGuid = dataFeat.Guid
            };
            Save(overlap.ProjectName, corr);
            overlap.MatchGuid = corr.Guid;
            return true;
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
            Observation obs0 = Observation.Find(Pipeline.DynamoContext, m.ProjectName, m.ObservationName0);
            Observation obs1 = Observation.Find(Pipeline.DynamoContext, m.ProjectName, m.ObservationName1);
            Project project = Project.Find(Pipeline.DynamoContext, m.ProjectName);
            Overlap overlap = Overlap.Find(Pipeline.DynamoContext, obs0.Name, obs1.Name, m.ProjectName);
            
            if (overlap.Status != Overlap.StatusType.Proposed) //someone has already processed this overlap
            {
                m.DeleteMessage(SQSClient, config.JobQueue);
                return 0;
            }
            
            var res = DoMatch(overlap, obs0, obs1, project, m);
            overlap.Status = res ? Overlap.StatusType.Matched : Overlap.StatusType.Rejected;
            overlap.Status = Overlap.StatusType.Matched;
            {
                return 0; //another worker tried to update this overlap. allow message to return to queue
            }
            
            m.DeleteMessage(SQSClient, config.JobQueue);
            return 0;
        }
    }
}
