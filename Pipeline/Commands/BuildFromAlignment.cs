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
        }



        class FrameCache
        {
            ConcurrentDictionary<string, Frame> frames = new ConcurrentDictionary<string, Frame>();

            DynamoDBContext dynamoContext;
            string projectName;

            public FrameCache(DynamoDBContext dynamoContext, string projectName)
            {
                this.dynamoContext = dynamoContext;
                this.projectName = projectName;
            }

            public Frame GetFrame(string name)
            {
                if(!frames.ContainsKey(name))
                {
                    var frame = Frame.Find(dynamoContext, projectName, name);
                    if (frame == null)
                    {
                        logger.Warn("No frame found for observation: " + name);
                        return null;
                    }
                    frames.TryAdd(name, frame);
                }
                return frames[name];
            }
        }

        UncertainRigidTransform ObservationToRoot(Observation obs, FrameCache frameCache)
        {
            Frame frame = frameCache.GetFrame(obs.FrameName);
            UncertainRigidTransform transform = null;
            while (frame != null) 
            {
                // Base case, we are the observation frame, initilize transform to the transform for this observation
                if(transform == null)
                {
                    transform = FrameTransform.Find(DynamoContext, frame).Transform;
                }
                else
                {
                    // Otherwise we are a parent transform -  Read the transform and combine it with the existing transform
                    var parentTransform = FrameTransform.Find(DynamoContext, frame).Transform;
                    transform =  transform * parentTransform; // TODO: this or the reverse of this
                }
                frame = frame.GetParent(DynamoContext);
            }
            return transform;
        }

        Mesh BuildPointCloud(Observation obs, UncertainRigidTransform observationToRoot, int stepSize = 1)
        {
            S3ImageRef s3ref = new S3ImageRef(obs.Url);
            
            Image img = this.Load(s3ref, true);
            Mesh result = new Mesh();
            for (int r = 0; r < img.Height; r += stepSize)
            {
                for (int c = 0; c < img.Width; c += stepSize)
                {
                    double range = img[0, r, c];
                    if (range > MathE.EPSILON && range < 64)
                    {
                        var observationPoint = img.CameraModel.Unproject(new Vector2(c, r), range);
                        var dist = observationToRoot.TransformPoint(observationPoint);
                        var rootPoint = dist.XnaMean;
                        result.Vertices.Add(new Vertex(rootPoint));
                    }
                }
            }
            return result;
        }

        public int Run()
        {
            var pointObs = Observation.FindByType(DynamoContext, options.ProjectName, "Points").ToArray();

            FrameCache cache = new FrameCache(DynamoContext, options.ProjectName);

            Parallel.ForEach(pointObs, obs =>
            {
                if (!obs.UseForReconstruction)
                {
                    logger.Info("Skipping observation: " + obs.Name);
                    return;
                }
                logger.Info("Processing observation: " + obs.Name);
                var transform = ObservationToRoot(obs, cache);
                var frame = cache.GetFrame(obs.FrameName);
                var otherObservations = Observation.Find(DynamoContext, frame);

                var m = BuildPointCloud(obs, transform);
                m.Save(Path.Combine(options.OutputDirectory, obs.Name + ".ply"));
            });

            return 0;
        }
    }
}
