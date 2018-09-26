using CommandLine;
using log4net;
using OPS.Alignment;
using OPS.Cloud;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using OPS.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using System.IO;
using Amazon.DynamoDBv2.DataModel;
using OPS.MathExtensions;
using System.Collections.Concurrent;
using OPS.Util;

namespace OPS.Pipeline
{
    [Verb("buildfromalignment", HelpText = "Reads an alignment solution in the database and builds a pointcloud")]
    public class BuildFromAlignmentOptions
    {
        [Value(0, Required = true, HelpText = "Name of project to use")]
        public string ProjectName { get; set; }

        [Value(1, Required = true, HelpText = "Prefix for database")]
        public string DynamoDBPrefix { get; set; }

        [Value(2, Required = true, HelpText = "Output directory")]
        public string OutputDirectory { get; set; }

        [Value(3, Required = true, HelpText = "Landform AWS profile")]
        public string LandformProfile { get; set; }

        [Value(4, Required = true, HelpText = "MSLICE AWS profile")]
        public string MSliceProfile { get; set; }
    }


    public class BuildFromAlignment : PipelineCore
    {
        BuildFromAlignmentOptions options;

        static ILog logger = LogManager.GetLogger(typeof(BuildFromAlignment));
        const int MIN_MATCHES = 20;
        public static ASIFTDetector detector = new ASIFTDetector(maxSimulatedDimension: 1024);

        public BuildFromAlignment(BuildFromAlignmentOptions options) : base(dynamoPrefix: options.DynamoDBPrefix)
        {
            this.AddProfile("s3://landlords-dev/", options.LandformProfile);
            this.AddProfile("s3://red-product/", options.MSliceProfile);
            this.options = options;
            PathHelper.EnsureExists(options.OutputDirectory);
        }

        static public Matrix ObservationToRoot(DynamoDBContext dynamoContext, Observation obs, FrameCache frameCache)
        {
            Frame frame = frameCache.GetFrame(obs.FrameName);
            //Start with the initial transform
            Matrix transform = FrameTransform.Find(dynamoContext, frame).Transform.Mean;
            while ((frame = frame.GetParent(dynamoContext)) != null) 
            {
                //Read the parent transform and combine it with the existing transform
                var parentTransform = FrameTransform.Find(dynamoContext, frame).Transform.Mean;
                transform = parentTransform * transform;          
            }
            return transform;
        }

        Mesh BuildPointCloud(Observation obs, Matrix obsToRoot, Observation imgObs = null, int stepSize = 1)
        {
            S3ImageRef s3ref = new S3ImageRef(obs.Url);
            
            Image img = this.Load(s3ref, true);
            Image mask = RoverMask.Build(img);

            if(mask == null)
            {
                throw new Exception("Images used in reconstruction should have masks!");
            }

            Image colorImg = null;
            if (imgObs != null)
            {
                colorImg = Load(new S3ImageRef(imgObs.Url));
            }

            Mesh result = new Mesh();
            for (int r = 0; r < img.Height; r += stepSize)
            {
                for (int c = 0; c < img.Width; c += stepSize)
                {
                    if (mask[0, r, c] != 0)
                    {
                        double range = img[0, r, c];
                        if (range > MathE.EPSILON && range < 64)
                        {
                            var observationPoint = img.CameraModel.Unproject(new Vector2(c, r), range);
                            var rootPoint = Vector3.Transform(observationPoint, Matrix.Transpose(obsToRoot)); //observationPoint dist.XnaMean;
                            var res = new Vertex(rootPoint);
                            if (colorImg != null)
                            {
                                if (img.Bands == 3)
                                {
                                    res.Color = new Vector4(colorImg[0, r, c], colorImg[1, r, c], colorImg[2, r, c], 1);
                                }
                                else
                                {
                                    res.Color = new Vector4(colorImg[0, r, c], colorImg[0, r, c], colorImg[0, r, c], 1);
                                }
                            }
                            result.Vertices.Add(res);
                        }
                    }
                }
            }
            return result;
        }


        class TransformRecord
        {
            public double[] transform;
            public int[] rmc;
            public string product;

            public TransformRecord(Matrix m, int[] rmc, string product)
            {
 
                transform = new double[] { m.M11, m.M21, m.M31, m.M41,
                                           m.M12, m.M22, m.M32, m.M42,
                                           m.M13, m.M23, m.M33, m.M43,
                                           m.M14, m.M24, m.M34, m.M44};
                this.rmc = rmc;
                this.product = product;
            }
        } 

        public int Run()
        {
            var pointObs = Observation.FindByType(DynamoContext, options.ProjectName, "Points").ToArray();

            FrameCache cache = new FrameCache(DynamoContext, options.ProjectName);

            ConcurrentBag<TransformRecord> records = new ConcurrentBag<TransformRecord>();
            Serial.ForEach(pointObs, new ParallelOptions() { MaxDegreeOfParallelism= Environment.ProcessorCount }, rngObs =>
            {
                if (!rngObs.UseForReconstruction)
                {
                    logger.Info("Skipping observation: " + rngObs.Name);
                    return;
                }
                logger.Info("Processing observation: " + rngObs.Name);
                var transform = ObservationToRoot(DynamoContext, rngObs, cache);
                var frame = cache.GetFrame(rngObs.FrameName);

                int[] rmc = null;
                {
                    
                    Image img = this.Load(new S3ImageRef(rngObs.Url), true);
                    var parser = new PDSParser((PDSMetadata)img.Metadata);
                    rmc = parser.MotionCounter;
                }


                records.Add(new TransformRecord(transform, rmc, rngObs.Name));
                var obsPair =  MSLProject.FindBestPair(RoverObservation.Find(DynamoContext, frame).Where(ob => ob.UseForReconstruction).ToList());
                Observation imgObs = null;
                if(obsPair != null)
                {
                    imgObs = obsPair.Image;
                }

                var m = BuildPointCloud(rngObs, transform, imgObs);
                m.HasColors = true;
                if (m.Vertices.Count > 0)
                {
                    m.Save(Path.Combine(options.OutputDirectory, rngObs.Name + ".ply"));
                }
            });
            File.WriteAllText(Path.Combine(options.OutputDirectory, "transforms.json"), JsonHelper.ToJson(records, true));

            return 0;
        }
    }
}
