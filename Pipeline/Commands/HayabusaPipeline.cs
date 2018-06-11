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

        [Value(1, Required = false, HelpText = "URL to use for Dynamo DB", Default = "http://localhost:8000")]
        public string DynamoDBServiceUrl { get; set; }
        
        [Value(2, Required = false, HelpText = "URL to use for S3", Default = "http://localhost:4568")]
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
            : base(true, true, "", opt.S3ServiceUrl, opt.DynamoDBServiceUrl)
        {
            Options = opt;
            Masks = new LazyComputation<Observation, ImageDataProduct>(this, (o) => o.MaskGuid, ComputeMask);
            Features = new LazyComputation<Observation, DetectedFeatures>(this, (o) => o.FeaturesGuid, ComputeImageFeatures);
        }

        public int Run()
        {
            // make sure hayabusa project exists
            var project = Project.Find(DynamoContext, ProjectName);
            if (project == null)
            {
                project = Project.Create(DynamoContext, ProjectName, "hayabusa/products", "hayabusa/images");
                project.Save(DynamoContext);
            }

            var ingester = new HayabusaIngester(this);
            foreach (var fn in Directory.EnumerateFiles(Options.InputPath))
            {
                var diskRef = new DiskImageRef(Path.Combine(Options.InputPath, fn));
                bool found = false;
                foreach (var s3File in Storage.SearchObjects(project.InputPath, diskRef.DisplayName))
                {
                    if (Path.GetFileNameWithoutExtension(s3File) == diskRef.DisplayName)
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                {
                    logger.DebugFormat("Already uploaded {0}", diskRef.DisplayName);
                }
                else
                {
                    var s3Url = new S3Url(Options.InputPath + "/" + Path.GetFileName(diskRef.Path)).ToString();
                    logger.InfoFormat("Uploading {0} to {1}", diskRef.DisplayName, s3Url);
                    Storage.UploadFile(diskRef.Path, s3Url);

                    var res = ingester.Ingest(new S3ImageRef(s3Url));
                    logger.InfoFormat("{0}: {1}", diskRef.DisplayName, res.ToString());
                }
            }
            return 0;
        }

        private Image Load(Observation obs)
        {
            var img = Load(new ObservationImageRef(obs));
            // haha trust me
            img.CameraModel = new HayabusaCameraModel(120.71 / 1000, 0, img.Width / 1024.0); // -2.8e-5
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
            return new ImageDataProduct(img, "png", typeof(byte));
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
            return new DetectedFeatures
            {
                Features = features,
                ObservationName = obs.Name
            };
        }

        /*public ComputedCorrespondence Match(Overlap overlap)
        {

        }*/
    }
}
