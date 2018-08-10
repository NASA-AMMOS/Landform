using CommandLine;
using OPS.Geometry;
using OPS.MathExtensions;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using OPS.Cloud;
using System.Collections.Concurrent;
using Amazon.DynamoDBv2.DataModel;
using log4net;
using Microsoft.Xna.Framework;
using System.IO;
using OPS.Util;
using Amazon.DynamoDBv2.Model;

namespace OPS.Pipeline
{
    [Verb("curiositymesh", HelpText = "Reads an alignment solution in the database and builds a mesh")]
    public class CuriosityMeshOptions
    {
        [Value(0, Required = true, HelpText = "Name of project to use")]
        public string ProjectName { get; set; }

        [Value(1, Required = true, HelpText = "Prefix for database")]
        public string DynamoDBPrefix { get; set; }

        [Value(2, Required = true, HelpText = "Landform AWS profile")]
        public string LandformProfile { get; set; }

        [Value(3, Required = true, HelpText = "MSLICE AWS profile")]
        public string MSliceProfile { get; set; }
    }

    public class CuriosityMesh : PipelineCore
    {
        CuriosityMeshOptions options;

        static ILog logger = LogManager.GetLogger(typeof(CuriosityMesh));

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
                if (!frames.ContainsKey(name))
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

        public CuriosityMesh(CuriosityMeshOptions options) : base(dynamoPrefix: options.DynamoDBPrefix)
        {
            this.AddProfile("s3://landlords-dev/", options.LandformProfile);
            this.AddProfile("s3://red-product/", options.MSliceProfile);
            this.options = options;
        }
        UncertainRigidTransform ObservationToRoot(Observation obs, FrameCache frameCache)
        {
            Frame frame = frameCache.GetFrame(obs.FrameName);
            UncertainRigidTransform transform = null;
            while (frame != null)
            {
                // Base case, we are the observation frame, initilize transform to the transform for this observation
                if (transform == null)
                {
                    transform = FrameTransform.Find(DynamoContext, frame).Transform;
                }
                else
                {
                    // Otherwise we are a parent transform -  Read the transform and combine it with the existing transform
                    var parentTransform = FrameTransform.Find(DynamoContext, frame).Transform;
                    transform = parentTransform * transform;
                }
                frame = frame.GetParent(DynamoContext);
            }
            return transform;
        }

        Mesh BuildPointCloud(Observation obs, UncertainRigidTransform observationToRoot, Observation imgObs = null, Observation normObs = null, int stepSize = 1)
        {
            S3ImageRef s3ref = new S3ImageRef(obs.Url);

            Image img = this.Load(s3ref, true);
            Image mask = RoverMask.Build(img);

            if (mask == null)
            {
                throw new Exception("Images used in reconstruction should have masks!");
            }

            Image colorImg = null;
            if (imgObs != null)
            {
                colorImg = Load(new S3ImageRef(imgObs.Url));
            }
            Image normImg = null;
            if(normObs != null)
            {
                normImg = Load(new S3ImageRef(normObs.Url));
            }

            var obsToRoot = observationToRoot.Mean;
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
                            Vector3 normal = new Vector3();
                            if (normImg != null)
                            {
                                if (normImg.Bands == 3)
                                {
                                    normal = new Vector3(normImg[0, r, c], normImg[1, r, c], normImg[2, r, c]);
                                }
                            } else
                            {
                                continue;
                            }
                            if(normal.LengthSquared() < 1e-8)
                            {
                                continue;
                            }
                            var observationNormalPoint = observationPoint + normal;
                            var rootPoint = Vector3.Transform(observationPoint, Matrix.Transpose(obsToRoot)); //observationPoint dist.XnaMean;
                            var rootNormalPoint = Vector3.Transform(observationNormalPoint, Matrix.Transpose(obsToRoot));
                            var res = new Vertex(rootPoint);
                            res.Normal = rootNormalPoint - rootPoint;
                            if (colorImg != null)
                            {
                                if (colorImg.Bands == 3)
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
            var project = Project.Find(DynamoContext, options.ProjectName);
            string s3url = project.ProductPath;
     
            var pointObs = Observation.FindByType(DynamoContext, options.ProjectName, "Points").ToArray();

            FrameCache cache = new FrameCache(DynamoContext, options.ProjectName);

            var triples = FindBestTriples(RoverObservation.Find(DynamoContext, options.ProjectName));

            ConcurrentBag<TransformRecord> records = new ConcurrentBag<TransformRecord>();
            ConcurrentBag<Mesh> meshes = new ConcurrentBag<Mesh>();
            Parallel.ForEach(triples, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount }, triple =>
            {
                PDSMetadata metadata = null;
                this.Storage(triple.Image.Url).GetStorageStream(triple.Image.Url, stream =>
                {
                    metadata = new PDSMetadata(stream);
                });

                PDSParser parser = new PDSParser(metadata);
                if(parser.Drive != 1472)
                {
                    logger.Info("Skipped mesh");
                    return;
                }

                if (triple.Point == null || triple.Normal == null)
                {
                    return;
                }
                var transform = ObservationToRoot(triple.Point, cache);
                var frame = cache.GetFrame(triple.Point.FrameName);

                int[] rmc = null;
                {
                    Image img = this.Load(new S3ImageRef(triple.Image.Url), true);
                    var pds = new PDSParser((PDSMetadata)img.Metadata);
                    rmc = pds.MotionCounter;
                }

                records.Add(new TransformRecord(transform.Mean, rmc, triple.Point.Name));

                var m = BuildPointCloud(triple.Point, transform, triple.Image, triple.Normal);
                m.HasColors = true;

                meshes.Add(m);
                logger.Info("Completed " + meshes.Count() + " of " + triples.Count() + " meshes.");
            });
            //File.WriteAllText(Path.Combine(options.OutputDirectory, "transforms.json"), JsonHelper.ToJson(records, true));
            int count = 0;
            foreach(Mesh m in meshes)
            {
                count += m.Vertices.Count();
            }
            var largeMesh = Mesh.Merge(meshes.ToArray());
            //var largeMesh = Mesh.Load(@"C:\Users\conductor\Desktop\temp\largeCloud.ply");
            largeMesh.HasNormals = true;
            largeMesh.HasColors = true;

            logger.Info("Attempting to mesh " + largeMesh.Vertices.Count() + " vertices");

            //largeMesh.Save(@"C:\Users\conductor\Desktop\temp\largeCloud.ply");
            largeMesh.HasColors = false;
            //MeshOperator mo = new MeshOperator(largeMesh);
            //largeMesh = mo.Clip(new BoundingBox(new Vector3(0.25,1.3,10000), new Vector3(13,10,-10000)));
            largeMesh.HasColors = true;

            largeMesh.Save(@"C:\Users\conductor\Desktop\temp\largeCloud4.ply");
            largeMesh.HasColors = false;
            
            //var largeMesh = Mesh.Load(@"C:\Users\conductor\Desktop\temp\largeCloud4.ply");
            largeMesh.HasColors = false;
            largeMesh = PoissonReconstruction.PoissonReconstruct(largeMesh, 1,12);
            string filename = @"C:\Users\conductor\Desktop\temp\largeMesh4.ply";
            string newFilename = @"C:\Users\conductor\Desktop\temp\largeMeshCopy.ply";
            largeMesh.Save(filename);

            //project.MeshGuid = Guid.NewGuid().ToString();
            //this.Storage(s3url).UploadFile(filename, s3url + project.MeshGuid);

            return 0;
        }

        public static Observation FindBestNorms(RoverObservation pointObs, IEnumerable<RoverObservation> frameObservations)
        {
            frameObservations = frameObservations.Where(ob => ob.UseForReconstruction).ToList();
            var normalList = frameObservations.Where(ob => ob.ObservationType == ObservationType.Normals.ToString()).ToList();
            normalList.Sort(RoverObservationComparison);
            foreach(var normObs in normalList)
            {
                bool linear = IsLinear(pointObs);
                if(linear == IsLinear(normObs) && pointObs.Width == normObs.Width && pointObs.Height == normObs.Height)
                {
                    return normObs;
                }
            }
            return null;
        }

        public static ImagePointPair FindBestPair(IEnumerable<RoverObservation> frameObservations)
        {
            var list = frameObservations.Where(ob => ob.UseForReconstruction).ToList();

            list.Sort(RoverObservationComparison);
            var imageList = list.Where(ob => ob.ObservationType == ObservationType.Image.ToString()).ToList();
            var pointList = list.Where(ob => ob.ObservationType == ObservationType.Points.ToString()).ToList();
            if (pointList.Count > 0)
            {
                foreach (var imageObs in imageList)
                {
                    bool linear = IsLinear(imageObs);
                    foreach (var pointObs in pointList)
                    {
                        if (linear == IsLinear(pointObs) && imageObs.Width == pointObs.Width && imageObs.Height == pointObs.Height)
                        {
                            return new ImagePointPair(imageObs, pointObs);
                        }
                    }
                }
            }
            // If we didn't find any range products to match our image products than just return the first image
            if (imageList.Count > 0)
            {
                return new ImagePointPair(imageList.First(), null);
            }
            return null;
        }

        /// <summary>
        /// Return -1 if a < b
        /// return 1 if  a > b
        /// return 0 if equal
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        static int RoverObservationComparison(RoverObservation a, RoverObservation b)
        {
            // sort first by producer
            if (a.Producer == RoverProductProducer.MSSS.ToString() && b.Producer == RoverProductProducer.OPGS.ToString())
            {
                return -1;
            }
            if (a.Producer == RoverProductProducer.OPGS.ToString() && b.Producer == RoverProductProducer.MSSS.ToString())
            {
                return 1;
            }
            // sort second by linear-ness
            var linearA = IsLinear(a);
            var linearB = IsLinear(b);
            if (!linearA && linearB)
            {
                return -1;
            }
            if (linearA && !linearB)
            {
                return 1;
            }
            // sort by version
            return int.Parse(b.Version) - int.Parse(a.Version);
        }

        static bool IsLinear(RoverObservation observation)
        {
            return ((CameraModel)JsonHelper.FromJson(observation.CameraModel)).Linear;
        }

        public static ImgPntNormTuple FindBestTriple(IEnumerable<RoverObservation> frameObservations)
        {
            var list = frameObservations.Where(ob => ob.UseForReconstruction).ToList();

            list.Sort(RoverObservationComparison);
            var imageList = list.Where(ob => ob.ObservationType == ObservationType.Image.ToString()).ToList();
            var pointList = list.Where(ob => ob.ObservationType == ObservationType.Points.ToString()).ToList();
            var normList = list.Where(ob => ob.ObservationType == ObservationType.Normals.ToString()).ToList();
            if (pointList.Count > 0)
            {
                foreach (var imageObs in imageList)
                {
                    bool linear = IsLinear(imageObs);
                    foreach (var pointObs in pointList)
                    {
                        if (linear == IsLinear(pointObs) && imageObs.Width == pointObs.Width && imageObs.Height == pointObs.Height)
                        {
                            foreach(var normObs in normList)
                            {
                                if(linear == IsLinear(normObs) && imageObs.Width == normObs.Width && imageObs.Height == normObs.Height)
                                {
                                    return new ImgPntNormTuple(imageObs, pointObs, normObs);
                                }
                            }
                            return new ImgPntNormTuple(imageObs, pointObs, null);                        
                        }
                    }
                }
            }
            // If we didn't find any range products to match our image products than just return the first image
            if (imageList.Count > 0)
            {
                return new ImgPntNormTuple(imageList.First(), null, null);
            }
            return null;
        }

        public static IEnumerable<ImgPntNormTuple> FindBestTriples(IEnumerable<RoverObservation> observations)
        {
            List<ImgPntNormTuple> results = new List<ImgPntNormTuple>();
            // Filter any that should not be used for observation
            observations = observations.Where(ob => ob.UseForReconstruction);
            var frameGroups = observations.GroupBy(ob => ob.FrameName);
            foreach (var frameGroup in frameGroups)
            {
                var r = FindBestTriple(frameGroup);
                if (r != null)
                {
                    results.Add(r);
                }
            }
            return results;
        }

        public class ImagePointPair
        {
            public Observation Image;
            public Observation Point;

            public ImagePointPair() { }

            public ImagePointPair(Observation img, Observation pnt)
            {
                Image = img;
                Point = pnt;
            }
        }

        public class ImgPntNormTuple
        {
            public Observation Image;
            public Observation Point;
            public Observation Normal;

            public ImgPntNormTuple() { }

            public ImgPntNormTuple(Observation img, Observation pnt, Observation norm)
            {
                Image = img;
                Point = pnt;
                Normal = norm;
            }
        }
    }
}

