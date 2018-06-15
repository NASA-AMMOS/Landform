using Amazon.DynamoDBv2.Model;
using CommandLine;
using Emgu.CV.Util;
using log4net;
using OPS.Alignment;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Plumbing;
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
        public HayabusaIngester(PipelineCore pipeline) : base(pipeline) { }

        public override Result Ingest(S3ImageRef imgRef)
        {
            var name = imgRef.DisplayName;
            var existing = Observation.Find(DynamoDB, HayabusaPipeline.ProjectName, name);
            if (existing != null)
            {
                return new Result(Status.Duplicate, existing);
            }

            var project = Project.Find(DynamoDB, HayabusaPipeline.ProjectName);
            var rootFrame = Frame.FindOrCreate(DynamoDB, project, "root");
            var frame = Frame.FindOrCreate(DynamoDB, project, name, rootFrame);
            var res = Observation.Create(DynamoDB, frame, name, imgRef.Url, ObservationType.Image.ToString(), "", true);
            return new Result(Status.Added, res);
        }
    }

    public class HayabusaPipeline : PipelineCore
    {
        public static readonly string ProjectName = "hayabusa";
        static ILog logger = LogManager.GetLogger(typeof(HayabusaPipeline));

        public LazyComputation<Observation, ImageDataProduct> Masks;
        public LazyComputation<Observation, DetectedFeatures> Features;
        public static ASIFTDetector detector = new ASIFTDetector();

        public HayabusaPipelineOptions Options;
        public HayabusaPipeline(HayabusaPipelineOptions opt)
            : base(true, true, opt.DynamoDBPrefix, opt.S3ServiceUrl, opt.DynamoDBServiceUrl)
        {
            Options = opt;
            Masks = new LazyComputation<Observation, ImageDataProduct>(this, (o) => o.MaskGuid, ComputeMask);
            Features = new LazyComputation<Observation, DetectedFeatures>(this, (o) => o.FeaturesGuid, ComputeImageFeatures);
        }

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
            foreach (var obs in observations)
            {
                Features.Get(obs.ProjectName, obs);
            }

            for (int i = 0; i < observations.Count - 1; i++)
            {
                var pair = new UnorderedImagePair(new ObservationImageRef(observations[i]), new ObservationImageRef(observations[i + 1]));
                Overlap overlap = Overlap.Find(DynamoContext, observations[i].Name, observations[i + 1].Name, project.Name);
                if (overlap == null)
                {
                    overlap = Overlap.Create(DynamoContext, observations[i], observations[i + 1]);
                    logger.InfoFormat("Created overlap {0}", overlap.CombinedName);
                    overlap.Status = Overlap.StatusType.Proposed;
                    if (!overlap.TrySave(DynamoContext))
                    {
                        throw new Exception("i don't want to deal with this");
                    }
                }
            }

            return 0;
        }

        private List<Observation> IngestDiskImages(Project project)
        {
            List<Observation> obs = new List<Observation>();

            var ingester = new HayabusaIngester(this);
            foreach (var fn in Directory.EnumerateFiles(Options.InputPath))
            {
                var diskRef = new DiskImageRef(Path.Combine(Options.InputPath, fn));
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

        private Image Load(Observation obs)
        {
            var img = Load(new ObservationImageRef(obs));
            // haha trust me
            var focalMM = 120.71;
            var pixelSizeMM = 0.012;
            var focalPix = focalMM / pixelSizeMM;
            img.CameraModel = new HayabusaCameraModel(focalPix, 0, img.Width / 1024.0); // -2.8e-5
            return img;
        }

        private ImageDataProduct ComputeMask(Observation obs)
        {
            // very hayabusa-specific - flood fill black from corner
            var img = Load(obs);
            var mask = new Image(1, img.Width, img.Height);

            // intialize with all valid
            for (int row = 0; row < img.Height; row++)
            {
                for (int col = 0; col < img.Width; col++)
                {
                    mask[0, row, col] = 1;
                }
            }

            Queue<KeyValuePair<int, int>> q = new Queue<KeyValuePair<int, int>>();
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
                if (grayscale > 20 / 255.0)
                {
                    continue;
                }

                mask[0, y, x] = 0;

                q.Enqueue(new KeyValuePair<int, int>(x + 1, y));
                q.Enqueue(new KeyValuePair<int, int>(x, y + 1));
                q.Enqueue(new KeyValuePair<int, int>(x - 1, y));
                q.Enqueue(new KeyValuePair<int, int>(x, y - 1));
            }
            var res = new ImageDataProduct(mask, ".png", typeof(byte));
            Save(obs.ProjectName, res);
            obs.MaskGuid = res.Guid;
            obs.Save(DynamoContext);
            return res;
        }

        public DetectedFeatures ComputeImageFeatures(Observation obs)
        {
            var img = Load(obs);
            var mask = Masks.Get(obs.ProjectName, obs);

            ImageFeature[] features;
            lock (detector)
            {
                try
                {
                    features = detector.Detect(img, mask.Image).ToArray();
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

        /*public ComputedCorrespondence Match(Overlap overlap)
        {

        }*/
    }
}
