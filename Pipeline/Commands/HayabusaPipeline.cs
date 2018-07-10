using Amazon.DynamoDBv2.Model;
using CommandLine;
using Emgu.CV.Util;
using log4net;
using MathNet.Numerics.LinearAlgebra;
using Newtonsoft.Json;
using OPS.Alignment;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Plumbing;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline
{
    [Verb("hayabusa", HelpText = "Hayabusa reconstruction pipeline")]
    public class HayabusaPipelineOptions
    {
        [Value(0, Required = true, HelpText = "Folder containing input images")]
        public string InputPath { get; set; }

        [Value(1, Required = false, HelpText = "S3 bucket to use", Default = "hayabusa-landform")]
        public string S3Bucket { get; set; }

        [Value(2, Required = false, HelpText = "Prefix to use for Dynamo DB tables", Default = "")]
        public string DynamoDBPrefix { get; set; }

        [Value(3, Required = false, HelpText = "URL to use for Dynamo DB", Default = "http://localhost:8000")]
        public string DynamoDBServiceUrl { get; set; }
        
        [Value(4, Required = false, HelpText = "URL to use for S3", Default = "http://localhost:4568")]
        public string S3ServiceUrl { get; set; }
    }

    public class HayabusaIngester : IngestImage
    {
        struct PriorInfo
        {
            public string sclk;
            public double et;
            public double[][] pose;
        }
        Dictionary<string, PriorInfo> priors;


        public HayabusaIngester(PipelineCore pipeline) : base(pipeline)
        {
            priors = JsonConvert.DeserializeObject<Dictionary<string, PriorInfo>>(File.ReadAllText("D:\\imagestomatch\\hayabusa\\HAY-A-SPICE-6-V1.0\\newpriors.json"));
        }

        //TODO: This used to take an S3ImageRef, undefined for other ImageRef types
        public override Result Ingest(S3ImageRef imgRef)
        {
            var name = imgRef.DisplayName;
            var existing = Observation.Find(DynamoDB, HayabusaPipeline.ProjectName, name);

            var project = Project.Find(DynamoDB, HayabusaPipeline.ProjectName);
            var rootFrame = Frame.FindOrCreate(DynamoDB, project, "root");
            var frame = Frame.FindOrCreate(DynamoDB, project, name, rootFrame);


            if (priors.ContainsKey(name))
            {
                if (frame.PriorIds.Count < 1)
                {
                    var p = priors[name];
                    Microsoft.Xna.Framework.Matrix m = new Microsoft.Xna.Framework.Matrix();
                    for (int i = 0; i < 4; i++)
                    {
                        for (int j = 0; j < 4; j++)
                        {
                            m[j, i] = p.pose[i][j];
                        }
                    }
                    m.Translation *= 1000; // km -> m

                    double fifthDegSqr = Math.Pow(0.2 * Math.PI / 180, 2);
                    double oneMeter = 1;
                    double posCov = oneMeter * oneMeter;
                    var cov = CreateMatrix.Diagonal(new double[] { posCov, posCov, posCov, fifthDegSqr, fifthDegSqr, fifthDegSqr });

                    UncertainRigidTransform ut = new UncertainRigidTransform(m, cov);
                    var prior = TransformPrior.Create(DynamoDB, frame, ut);
                    frame.PriorIds.Add(prior.Id);
                    frame.Save(DynamoDB);
                }
            }

            if (existing != null)
            {
                return new Result(Status.Duplicate, existing);
            }

            var res = Observation.Create(DynamoDB, frame, name, imgRef.Url, ObservationType.Image.ToString(), "", true);
            return new Result(Status.Added, res);
        }
    }

    public class HayabusaPipeline : PipelineCore
    {
        public static readonly string ProjectName = "hayabusa";
        static ILog logger = LogManager.GetLogger(typeof(HayabusaPipeline));

        public LazyComputation<Observation, PngDataProduct> Masks;
        public LazyComputation<Observation, DetectedFeatures> Features;
        public LazyComputation<Overlap, ComputedCorrespondence> Matches;
        public static ASIFTDetector detector = new ASIFTDetector(maxSimulatedDimension: 1024);

        public HayabusaPipelineOptions Options;
        public HayabusaPipeline(HayabusaPipelineOptions opt)
            : base(true, true, opt.DynamoDBPrefix, opt.S3ServiceUrl, opt.DynamoDBServiceUrl)
        {
            Options = opt;
            Masks = new LazyComputation<Observation, PngDataProduct>(this, (o) => o.MaskGuid, ComputeMask);
            Features = new LazyComputation<Observation, DetectedFeatures>(this, (o) => o.FeaturesGuid, ComputeImageFeatures);
            Matches = new LazyComputation<Overlap, ComputedCorrespondence>(this, (o) => o.MatchGuid, ComputeCorrespondence);
        }

        public static readonly int MIN_MATCHES = 20;

        public int Run()
        {
            EnsureTablesExist();
            try
            {
                S3Client.EnsureBucketExists(Options.S3Bucket);
            }
            catch (Amazon.S3.AmazonS3Exception)
            {
                // s3rver errors on EnsureBucketExists if bucket exists.
                // move along, nothing to see
            }

            // make sure hayabusa project exists
            var project = Project.Find(DynamoContext, ProjectName);
            if (project == null)
            {
                project = Project.Create(DynamoContext, ProjectName, "s3://" + Options.S3Bucket + "/hayabusa/products/", "s3://" + Options.S3Bucket + "/hayabusa/images/");
                project.Save(DynamoContext);
            }

            var observations = IngestDiskImages(project);
            Parallel.ForEach(observations, obs =>
            {
                Features.Get(obs.ProjectName, obs);
            });

            List<Overlap> overlaps = new List<Overlap>();

            for (int i = 0; i < observations.Count; i++)
            {
                for (int j = i + 1; j < observations.Count; j++)
                {
                    var pair = new UnorderedImagePair(new ObservationImageRef(observations[i]), new ObservationImageRef(observations[j]));
                    Overlap overlap = Overlap.Find(DynamoContext, observations[i].Name, observations[j].Name, project.Name);
                    if (overlap == null)
                    {
                        overlap = Overlap.Create(DynamoContext, observations[i], observations[j]);
                        logger.InfoFormat("Created overlap {0}", overlap.CombinedName);
                    }
                    overlaps.Add(overlap);
                }
            }

            Serial.ForEach(overlaps, ol =>
            {
                Matches.Get(ol.ProjectName, ol);
            });

            return 0;
        }

        private List<Observation> IngestDiskImages(Project project)
        {
            List<Observation> obs = new List<Observation>();

            var ingester = new HayabusaIngester(this);
            foreach (var fn in Directory.EnumerateFiles(Options.InputPath))
            {
                var diskRef = new DiskImageRef(Path.Combine(Options.InputPath, fn));

                if (Path.GetExtension(fn) != "png")
                {
                    var path = Path.Combine(Options.InputPath, "png/" + Path.ChangeExtension(Path.GetFileName(fn), ".png"));
                    if (!File.Exists(path))
                    {
                        var img = LoadWithModel(diskRef);
                        img.Save<byte>(path);
                    }
                }

                var s3Url = new S3Url(project.InputPath + Path.GetFileName(diskRef.Path));

                bool exists = true;
                try
                {
                    S3Client.GetObjectMetadata(s3Url.BucketName, s3Url.Prefix);
                }
                catch (Amazon.S3.AmazonS3Exception ex)
                {
                    if (ex.ErrorCode != "NotFound")
                    {
                        throw;
                    }
                    exists = false;
                }

                if (!exists)
                {
                    logger.InfoFormat("Uploading {0} to {1}", diskRef.DisplayName, s3Url.Url);
                    var resp = S3Client.PutObject(new Amazon.S3.Model.PutObjectRequest
                    {
                        BucketName = s3Url.BucketName,
                        Key = s3Url.Prefix,
                        FilePath = diskRef.Path,
                        ContentType = "application/octet-stream"
                    });
                }
                else
                {
                    logger.DebugFormat("Already uploaded {0}", diskRef.DisplayName);
                }

                var res = ingester.Ingest(new S3ImageRef(s3Url.Url));
                logger.InfoFormat("{0}: {1}", diskRef.DisplayName, res.Status.ToString());
                if (res.Observation != null)
                {
                    obs.Add(res.Observation);
                }
            }

            return obs;
        }

        private void EnsureTablesExist()
        {
            // make sure tables exist
            foreach (var t in new Type[] { typeof(Project), typeof(Observation), typeof(Overlap), typeof(Frame), typeof(FrameTransform), typeof(TransformPrior) })
            {
                var tn = Options.DynamoDBPrefix + CreateCloudTemplates.TableName(t);

                try
                {
                    DynamoDB.DescribeTable(new DescribeTableRequest(tn));
                }
                catch (ResourceNotFoundException)
                {
                    // Table already exists
                    logger.InfoFormat("Table {0}: creating", tn);
                    DynamoDB.CreateTable(CreateCloudTemplates.CreateTable(t, Options.DynamoDBPrefix));
                    continue;
                }

                logger.InfoFormat("Table {0}: exists", tn);
            }
        }

        private Image LoadWithModel(ImageRef imgRef)
        {
            var img = Load(imgRef);
            if (img.CameraModel == null)
            {
                img.ApplyStdDevStretch();
                // haha trust me
                var focalMM = 120.71;
                var pixelSizeMM = 0.012;
                var focalPix = focalMM / pixelSizeMM;
                img.CameraModel = new HayabusaCameraModel(focalPix, 0, img.Width / 1024.0); // -2.8e-5
            }
            return img;
        }

        public PngDataProduct ComputeMask(Observation obs)
        {
            // very hayabusa-specific - flood fill black from corner
            var img = LoadWithModel(new ObservationImageRef(obs));
            var mask = new Image(1, img.Width, img.Height);

            // intialize with all valid
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    mask[0, row, col] = 1;
                }
            }

            Queue<KeyValuePair<int, int>> q = new Queue<KeyValuePair<int, int>>(img.Width * 10);
            HashSet<KeyValuePair<int, int>> explored = new HashSet<KeyValuePair<int, int>>();
            q.Enqueue(new KeyValuePair<int, int>(0, 0));
            while (q.Count > 0)
            {
                var pt = q.Dequeue();
                if (explored.Contains(pt)) continue;
                explored.Add(pt);

                var x = pt.Key;
                var y = pt.Value;
                if (x < 0 || y < 0 || x >= img.Width || y >= img.Height)
                {
                    continue;
                }

                var pix = img.GetBandValues(y, x);
                var grayscale = pix.Sum() / pix.Length;
                if (grayscale > 10 / 255.0)
                {
                    continue;
                }

                mask[0, y, x] = 0;

                q.Enqueue(new KeyValuePair<int, int>(x + 1, y));
                q.Enqueue(new KeyValuePair<int, int>(x, y + 1));
                q.Enqueue(new KeyValuePair<int, int>(x - 1, y));
                q.Enqueue(new KeyValuePair<int, int>(x, y - 1));
            }

            var res = new PngDataProduct(mask);
            Save(obs.ProjectName, res);
            obs.MaskGuid = res.Guid;
            obs.Save(DynamoContext);
            return res;
        }

        public DetectedFeatures ComputeImageFeatures(Observation obs)
        {
            var img = LoadWithModel(new ObservationImageRef(obs));
            var mask = Masks.Get(obs.ProjectName, obs);

            ImageFeature[] features;
            lock (detector)
            {
                try
                {
                    features = detector.Detect(img, mask.Image).ToArray();
                    //detector.ComputeDescriptors(img, features);
                }
                catch (CvException ex)
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
            Save(obs.ProjectName, res);
            obs.FeaturesGuid = res.Guid;
            obs.Save(DynamoContext);
            return res;
        }

        public ComputedCorrespondence ComputeCorrespondence(Overlap overlap)
        {
            //if (overlap.Status == Overlap.StatusType.Rejected) return null;

            AlignmentScene scene = new AlignmentScene();
            UnorderedImagePair pair;
            // Initialize scene
            {
                var obs0 = Observation.Find(DynamoContext, overlap.ProjectName, overlap.ObservationNameOne);
                var obs1 = Observation.Find(DynamoContext, overlap.ProjectName, overlap.ObservationNameTwo);
                var ref0 = new ObservationImageRef(obs0);
                var ref1 = new ObservationImageRef(obs1);
                pair = new UnorderedImagePair(ref0, ref1);

                scene.DetectedFeatures[ref0] = Features.Get(obs0.ProjectName, obs0).Features;
                scene.DetectedFeatures[ref1] = Features.Get(obs1.ProjectName, obs1).Features;

                var frame0 = Frame.Find(DynamoContext, obs0.ProjectName, obs0.FrameName);
                var frame1 = Frame.Find(DynamoContext, obs1.ProjectName, obs1.FrameName);

                Action<ObservationImageRef, Frame> handlePriors = (imgRef, frame) =>
                {
                    if (frame.PriorIds.Count < 1) return;
                    var prior = TransformPrior.Find(DynamoContext, frame.ProjectName, frame.PriorIds[0]);

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

            LoadWithModel(model);
            LoadWithModel(data);

            BruteForceMatcher bfm = new BruteForceMatcher();
            var matches = bfm.Match(model, data, scene.DetectedFeatures[model], scene.DetectedFeatures[data]);
            if (matches.Count < MIN_MATCHES)
            {
                logger.Info("No matches for " + overlap.CombinedName);
                overlap.Status = Overlap.StatusType.Rejected;
                overlap.TrySave(DynamoContext);
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
                    overlap.TrySave(DynamoContext);
                    return null;
                }
            }

            var matchImage = MatchImage.Create(this, matches, scene.DetectedFeatures[model], scene.DetectedFeatures[data]);
            matchImage.Save<byte>("D:\\matches\\" + overlap.CombinedName + ".png");

            var res = new ComputedCorrespondence
            {
                ModelFeaturesGuid = model.Observation.FeaturesGuid,
                DataFeaturesGuid = data.Observation.FeaturesGuid,
                Correspondence = matches
            };
            Save(overlap.ProjectName, res);
            overlap.MatchGuid = res.Guid;
            overlap.Status = Overlap.StatusType.Matched;
            overlap.TrySave(DynamoContext);

            return res;
        }
    }
}
