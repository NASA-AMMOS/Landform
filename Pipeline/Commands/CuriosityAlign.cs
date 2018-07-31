using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.S3;
using OPS.Cloud;
using OPS.Util;
using OPS.Plumbing;
using Amazon.S3.Model;
using OPS.Alignment;
using static OPS.Pipeline.IngestImage;
using MathNet.Numerics.LinearAlgebra;
using OPS.Geometry;
using log4net;
using OPS.Imaging;
using Amazon.DynamoDBv2.Model;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using OpenTK.Graphics.ES11;
using System.Collections.Concurrent;

namespace OPS.Pipeline
{

    [Verb("curiosityalign", HelpText = "Aligns a range of sols for curiosity and stores the solution in the database")]
    public class CuriosityAlignOptions
    {
        [Value(0, Required = true, HelpText = "Name of project to create")]
        public string ProjectName { get; set; }

        [Value(1, Required = true, HelpText = "Start sol to use in alignment - inclusive")]
        public int StartSol { get; set; }

        [Value(2, Required = true, HelpText = "End sol to use in alignment - inclusive")]
        public int EndSol { get; set; }

        [Value(3, Required = true, HelpText = "Prefix for database")]
        public string DynamoDBPrefix { get; set; }

        [Value(4, Required = true, HelpText = "Landform AWS profile")]
        public string LandformProfile { get; set; }

        [Value(5, Required = true, HelpText = "MSLICE AWS profile")]
        public string MSliceProfile { get; set; }

        [Option(HelpText = "Skip ingestion step", Default = false)]
        public bool SkipIngest { get; set; }

        [Option(HelpText = "Optional directory to save debug output files to", Default = null)]
        public string DebugOutputFolder { get; set; }
    }


    public class CuriosityAlign : PipelineCore
    {
        CuriosityAlignOptions options;

        static ILog logger = LogManager.GetLogger(typeof(CuriosityAlign));
        public LazyComputation<Observation, DetectedFeatures> Features;
        public LazyComputation<Overlap, ComputedCorrespondence> Matches;
        public LazyComputation<Observation, PngDataProduct> Masks;
        const int MIN_MATCHES = 20;
        public static ASIFTDetector detector = new ASIFTDetector(maxSimulatedDimension: 1024);

        public CuriosityAlign( CuriosityAlignOptions options) : base(dynamoPrefix: options.DynamoDBPrefix)
        {
            this.AddProfile("s3://landlords-dev/", options.LandformProfile);
            this.AddProfile("s3://red-product/", options.MSliceProfile);
            this.options = options;

            Features = new LazyComputation<Observation, DetectedFeatures>(this, (o) => o.FeaturesGuid, ComputeImageFeatures);
            Matches = new LazyComputation<Overlap, ComputedCorrespondence>(this, (o) => o.MatchGuid, ComputeCorrespondence);
            Masks = new LazyComputation<Observation, PngDataProduct>(this, (o) => o.MaskGuid, ComputeMask);
        }

        public List<string> GetDirectoriesToCrawl()
        {
            string opgsPattern = "s3://red-product/proj/msl/redops/ods/surface/sol/{0}/opgs/rdr/";
            string msssPattern = "s3://red-product/ods/surface/sol/{0}/soas/rdr/";

            List<string> directoriesToCheck = new List<string>();
            for (int sol = options.StartSol; sol <= options.EndSol; sol++)
            {
                foreach (string pattern in new string[] { opgsPattern, msssPattern })
                {

                    string dir = string.Format(pattern, sol.ToString().PadLeft(5, '0'));
                    foreach (string folder in Storage(dir).SearchFolders(dir))
                    {
                        directoriesToCheck.Add(folder);
                    }
                }
            }
            return directoriesToCheck;
        }

        public int Run()
        {
            string bucket = "landlords-dev";
            // Read existing alignment if there is one, if not create new project
            EnsureTablesExist();
            WaitForTables();
            try
            {
                this.S3Client.EnsureBucketExists(bucket);
            }
            catch (Amazon.S3.AmazonS3Exception)
            {
                // s3rver errors on EnsureBucketExists if bucket exists.
                // move along, nothing to see
            }
            Project project = Project.Find(this.DynamoContext, options.ProjectName);
            if (project == null)
            {
                project = Project.Create(this.DynamoContext, options.ProjectName, "s3://"+ bucket +"/curiosity-align/products/", "s3://"+ bucket+"/curiosity-align/images/");
                project.Save(this.DynamoContext);
            }

            Frame rootFrame = Frame.FindOrCreate(this.DynamoContext, project, MSLProject.ROOT_FRAME_NAME);
            if (rootFrame == null)
            {
                throw new Exception("Root Frame not found");
            }
            FrameTransform.FindOrCreate(this.DynamoContext, rootFrame, new UncertainRigidTransform(Matrix.CreateScale(new Vector3(1,1,1)), CreateMatrix.Diagonal<double>(new double[] { 0.0, 0.0, 0.0, 0, 0, 0 })), TransformSource.Prior);

            
            // Crawl MSL S3 bucket and look for files that aren't in our project            
            IngestPDSImage ingester = new IngestPDSImage(this, options.ProjectName);
            ConcurrentBag<Observation> obs = new ConcurrentBag<Observation>();
            

            if (!options.SkipIngest)
            {
                Parallel.ForEach(GetDirectoriesToCrawl(), folder =>
                {
                    Parallel.ForEach(Storage(folder).SearchObjects(folder, "*.IMG", false), url =>
                    {                        
                        S3ImageRef s3ref = new S3ImageRef(url);
                        try
                        {
                            Result res = ThroughputManager.Run(() => ingester.Ingest(s3ref));
                            if (res != null && res.Observation != null)
                            {
                                obs.Add(res.Observation);
                                logger.Info("Ingested: " + url);
                            }
                        }
                        catch (RawMetadataNullValueException e)
                        {
                            logger.Error("Error ingesting: " + url);
                            logger.Error(e.Message);
                            logger.Error(e.StackTrace);
                        }
                    });
                });
            }

            //logger.Info("Observation count: " + obs.Count);

            // Look up image priors for new images
            // Download new images from S3
            logger.Info("Find best point image pairs");

            DetectOverlaps detector = new DetectOverlaps(this);
            var bestImages = MSLProject.FindBestPairs(RoverObservation.Find(DynamoContext, project.Name)).Select(p => p.Image).ToList();
            logger.Info("Detect overlaps from " + bestImages.Count + " best images");
            detector.Run(bestImages, logger).ToList();                
            
            List<Overlap> overlaps = Overlap.Find(DynamoContext, project.Name).ToList();
            logger.Info("Overlaps detected: " + overlaps.Count);
            int existingGuids = 0;
            int emptyGuids = 0;
            foreach (Overlap ol in overlaps)
            {
                if(ol.MatchGuid == Guid.Empty)
                {
                    emptyGuids++;
                } else
                {
                    existingGuids++;
                }
            }
            logger.Info("Match guid found for " + existingGuids + " overlaps");

            // Generate feature discriptors and stuff, store in database
            logger.Info("Generate matches");
            int i = 0;
            foreach (Overlap ol in overlaps)
            {
                i++;
                Matches.Get(ol.ProjectName, ol);
                logger.Info("Completed " + i + " of " + overlaps.Count + " matches");
            }

            // Run bundle adjustment
            var bsg = new BuildSceneGraph(this);
            var bsg_options = new BuildSceneGraph.Options();
            bsg_options.GetTransform = bsg.StandardFrameTransform;
            Frame frame = Frame.Find(this.DynamoContext, project.Name, MSLProject.ROOT_FRAME_NAME);
            AlignmentScene scene = bsg.Build(frame, bsg_options);
            foreach (var node in scene.ImageToNode.Values)
            {
                node.AddComponent<AdjustedNode>();
            }
            new BundleAdjuster(this).Adjust(scene, options.DebugOutputFolder);
            foreach (var pair in scene.ImageToNode)
            {
                var imgRef = pair.Key;
                var node = pair.Value;
                var f = Frame.Find(DynamoContext, options.ProjectName, ((ObservationImageRef)imgRef).Observation.FrameName);
                FrameTransform ft = FrameTransform.Find(DynamoContext, f);
                ft.Transform = node.GetComponent<NodeUncertainTransform>().UncertainTransform;
                DynamoContext.Save<FrameTransform>(ft);
            }

            // Save results to database
            project.Save(this.DynamoContext);

            return 0;
        }

        public ComputedCorrespondence ComputeCorrespondence(Overlap overlap)
        {
            if (overlap.Status == Overlap.StatusType.Rejected) return null;

            AlignmentScene scene = new AlignmentScene();
            UnorderedImagePair pair;
            // Initialize scene
            {
                var obs0 = Observation.Find(this.DynamoContext, overlap.ProjectName, overlap.ObservationNameOne);
                var obs1 = Observation.Find(this.DynamoContext, overlap.ProjectName, overlap.ObservationNameTwo);
                var ref0 = new ObservationImageRef(obs0);
                var ref1 = new ObservationImageRef(obs1);
                pair = new UnorderedImagePair(ref0, ref1);

                var feat0 = Features.Get(obs0.ProjectName, obs0);
                var feat1 = Features.Get(obs1.ProjectName, obs1);
                if(feat0 == null || feat1 == null)
                {
                    logger.Info("Unable to load features for " + overlap.CombinedName);
                    overlap.Status = Overlap.StatusType.Rejected;
                    overlap.TrySave(this.DynamoContext);
                    return null;
                }

                scene.DetectedFeatures[ref0] = feat0.Features;
                scene.DetectedFeatures[ref1] = feat1.Features;

                if (scene.DetectedFeatures[ref0] == null || scene.DetectedFeatures[ref1] == null) {
                    logger.Info("Unable to load features for " + overlap.CombinedName);
                    overlap.Status = Overlap.StatusType.Rejected;
                    overlap.TrySave(this.DynamoContext);
                    return null;
                }

                var frame0 = Frame.Find(this.DynamoContext, obs0.ProjectName, obs0.FrameName);
                var frame1 = Frame.Find(this.DynamoContext, obs1.ProjectName, obs1.FrameName);

                Action<ObservationImageRef, Frame> handlePriors = (imgRef, frame) =>
                {
                    if (frame.PriorIds.Count < 1) return;
                    var prior = TransformPrior.Find(this.DynamoContext, frame.ProjectName, frame.PriorIds[0]);

                    var node = new SceneNode(imgRef.DisplayName, scene.Root.Transform);
                    node.GetOrAddComponent<NodeUncertainTransform>().UncertainTransform = prior.Transform;
                    scene.ImageToNode[imgRef] = node;
                };
                handlePriors(ref0, frame0);
                handlePriors(ref1, frame1);
            }

            logger.DebugFormat("Matching {0}", overlap.CombinedName);

            var model = (ObservationImageRef)pair.One;
            var data = (ObservationImageRef)pair.Two;

            // Construct list of match filters to apply
            List<IMatchFilter> filters = new List<IMatchFilter>();
            if (scene.ImageToNode.ContainsKey(model) && scene.ImageToNode.ContainsKey(data))
            {
                var kgf = new KnownGeometryFilter(this, (imgRef) => scene.ImageToNode[imgRef]);
                kgf.MajorAxisThreshold = double.PositiveInfinity;
                filters.Add(kgf);
            }
            else
            {
                return null;
            }

            // Brute force match descriptors

            this.Load(model);
            this.Load(data);

            IFeatureMatcher bfm = new CascadeHashingMatcher();
            var matches = bfm.Match(scene, pair);
            if (matches.Count < MIN_MATCHES)
            {
                logger.Info("No matches for " + overlap.CombinedName);
                overlap.Status = Overlap.StatusType.Rejected;
                overlap.TrySave(this.DynamoContext);
                return null;
            }

            filters.Add(new MoisanStivalFilter(this));
            filters.Add(new GTMFilter());
            foreach (var filter in filters)
            {
                int oldCount = matches.Count;
                matches = filter.Filter(scene, matches);
                logger.DebugFormat("* {0}: {1} -> {2}", filter.GetType().Name, oldCount, matches.Count);
                if (matches.Count < MIN_MATCHES)
                {
                    logger.Info("No matches for " + overlap.CombinedName);
                    overlap.Status = Overlap.StatusType.Rejected;
                    overlap.TrySave(this.DynamoContext);
                    return null;
                }
            }

            var matchImage = MatchImage.Create(this, matches, scene.DetectedFeatures[model], scene.DetectedFeatures[data]);
            if(options.DebugOutputFolder != null)
            {
                PathHelper.EnsureExists(options.DebugOutputFolder);
                matchImage.Save<byte>(Path.Combine(options.DebugOutputFolder, overlap.CombinedName + ".png"));
            }

            var res = new ComputedCorrespondence
            {
                ModelFeaturesGuid = model.Observation.FeaturesGuid,
                DataFeaturesGuid = data.Observation.FeaturesGuid,
                Correspondence = matches
            };
            this.Save(overlap.ProjectName, res);
            overlap.MatchGuid = res.Guid;
            overlap.Status = Overlap.StatusType.Matched;
            overlap.TrySave(this.DynamoContext);

            return res;
        }



        public DetectedFeatures ComputeImageFeatures(Observation obs)
        {
            var img = this.Load(new ObservationImageRef(obs));
            var mask = Masks.Get(obs.ProjectName, obs);

            if(mask == null)
            {
                return null;
            }

            ImageFeature[] features;
            lock (detector)
            {
                try
                {
                    features = detector.Detect(img, mask.Image).ToArray();
                    //detector.ComputeDescriptors(img, features);
                }
                catch (Emgu.CV.Util.CvException ex)
                {
                    logger.Error("failed to detect for " + obs.Name, ex);
                    return null;
                }
            }
            features = features.OrderByDescending(f => ((SIFTFeature)f).Response).Take(10000).ToArray();
            var res = new DetectedFeatures
            {
                Features = features,
                ObservationName = obs.Name
            };
            this.Save(obs.ProjectName, res);
            obs.FeaturesGuid = res.Guid;
            obs.Save(this.DynamoContext);
            return res;
        }



        public PngDataProduct ComputeMask(Observation obs)
        {
            // NOT very hayabusa-specific - don't flood fill black from corner
            try
            {
                var img = this.Load(new ObservationImageRef(obs));
                var mask = new PngDataProduct(RoverMask.Build(img));
                return mask;
            } catch
            {
                obs.UseForReconstruction = false;
                return null;
            }
        }

        private void EnsureTablesExist()
        {
            // make sure tables exist
            foreach (var t in new Type[] { typeof(Project), typeof(Observation), typeof(Overlap), typeof(Frame), typeof(FrameTransform), typeof(TransformPrior) })
            {
                var tn = options.DynamoDBPrefix + CreateCloudTemplates.TableName(t);

                try
                {
                    this.DynamoDB.DescribeTable(new DescribeTableRequest(tn));
                }
                catch (ResourceNotFoundException)
                {
                    // Table already exists
                    logger.InfoFormat("Table {0}: creating", tn);
                    this.DynamoDB.CreateTable(CreateCloudTemplates.CreateTable(t, options.DynamoDBPrefix));
                    continue;
                }

                logger.InfoFormat("Table {0}: exists", tn);
            }
        }

        private void WaitForTables()
        {
            foreach (var t in new Type[] { typeof(Project), typeof(Observation), typeof(Overlap), typeof(Frame), typeof(FrameTransform), typeof(TransformPrior) })
            {
                var tn = options.DynamoDBPrefix + CreateCloudTemplates.TableName(t);
                string tableStatus = "";
                while (tableStatus != "ACTIVE")
                {
                    logger.Info("Waiting for table: " + CreateCloudTemplates.TableName(t));
                    try
                    {
                        var tableResponse = this.DynamoDB.DescribeTable(new DescribeTableRequest(tn));
                        tableStatus = tableResponse.Table.TableStatus;
                    }
                    catch (ResourceNotFoundException)
                    {
                        //Wait for table
                        System.Threading.Thread.Sleep(3000);
                    }                    
                }
            }
        }
    }
}