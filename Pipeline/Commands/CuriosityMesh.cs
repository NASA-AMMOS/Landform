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
using OPS.Pipeline.Tiling;
using OPS.Pipeline.TileServer;
using MathNet.Numerics.LinearAlgebra;

namespace OPS.Pipeline
{
    [Verb("curiositymesh", HelpText = "Reads an alignment solution in the database and builds a mesh with tiling scheme")]
    public class CuriosityMeshOptions
    {
        [Value(0, Required = true, HelpText = "Name of alignment project to use")]
        public string AlignmentProjectName { get; set; }

        [Value(1, Required = true, HelpText = "Name of tiling project to use")]
        public string TilingProjectName { get; set; }

        [Value(2, Required = true, HelpText = "MSLICE AWS profile")]
        public string MSliceProfile { get; set; }
    }

    /// <summary>
    /// Read an alignment solution and build a mesh/tiling scheme
    /// </summary>
    public class CuriosityMesh : PipelineCore
    {
        CuriosityMeshOptions options;

        static ILog logger = LogManager.GetLogger(typeof(CuriosityMesh));

        public CuriosityMesh(CuriosityMeshOptions options) : base(dynamoPrefix: TileServerConfig.Instance.VenueName)
        {
            this.AddProfile("s3://landlords-dev/", TileServerConfig.Instance.Profile);
            this.AddProfile("s3://red-product/", options.MSliceProfile);
            this.options = options;
        }

        //TODO: duplicated from BuildFromAlignment, should decide where this lives
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

        /// <summary>
        /// Given a points product and a frame transfrom, build a point cloud; optionally apply colors and normals from image/normal products respectively
        /// </summary>
        /// <param name="s3ref"></param>
        /// <param name="observationToRoot"></param>
        /// <param name="imgObs"></param>
        /// <param name="normObs"></param>
        /// <param name="stepSize"></param>
        /// <returns></returns>
        Mesh BuildPointCloud(S3ImageRef s3ref, UncertainRigidTransform observationToRoot, Observation imgObs = null, Observation normObs = null, int stepSize = 1)
        {
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
            if (normObs != null)
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
                            }
                            else
                            {
                                continue;
                            }
                            if (normal.LengthSquared() < 1e-8)
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

        /// <summary>
        /// Convencience function for BuildPointCloud(S3ImageRef s3ref, ... )
        /// </summary>
        /// <param name="obs"></param>
        /// <param name="observationToRoot"></param>
        /// <param name="imgObs"></param>
        /// <param name="normObs"></param>
        /// <param name="stepSize"></param>
        /// <returns></returns>
        Mesh BuildPointCloud(Observation obs, UncertainRigidTransform observationToRoot, Observation imgObs = null, Observation normObs = null, int stepSize = 1)
        {
            S3ImageRef s3ref = new S3ImageRef(obs.Url);
            return BuildPointCloud(s3ref, observationToRoot, imgObs, normObs, stepSize);
            
        }

        //TODO: In progress
        /// <summary>
        /// Build a sparse point cloud from orbital data
        /// </summary>
        /// <param name="s3ref"></param>
        /// <param name="observationToRoot"></param>
        /// <param name="stepSize"></param>
        /// <returns></returns>
        Mesh BuildOrbitalCloud(S3ImageRef s3ref, UncertainRigidTransform observationToRoot, int stepSize = 200)
        {
            Image img = this.Load(s3ref, true);

            var obsToRoot = observationToRoot.Mean;

            Mesh result = new Mesh();
            for (int r = 0; r < img.Height; r += stepSize)
            {
                for (int c = 0; c < img.Width; c += stepSize)
                {
                    double range = img[0, r, c];
                    var observationPoint = new Vector3(r, c, range);
                    var rootPoint = Vector3.Transform(observationPoint, Matrix.Transpose(obsToRoot)); //observationPoint dist.XnaMean;
                    var res = new Vertex(rootPoint);
                    res.Normal = new Vector3(0,0,1);                           
                    result.Vertices.Add(res);
                }
            }
            return result;
        }
   
        //Also for building transforms.json, probably don't need in future
        /*class TransformRecord
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
        }*/

        public int Run()
        {
            //Read in observations from alignment project
            var alignmentProject = Project.Find(DynamoContext, options.AlignmentProjectName);

            FrameCache cache = new FrameCache(DynamoContext, options.AlignmentProjectName);

            //Use the same logic as the alignment to get best range products
            var triples = FindBestTriples(RoverObservation.Find(DynamoContext, options.AlignmentProjectName));

            //For transfomrs.json
            //ConcurrentBag<TransformRecord> records = new ConcurrentBag<TransformRecord>();

            ConcurrentBag<Mesh> meshes = new ConcurrentBag<Mesh>();

            //Project each range product to build point cloud
            Parallel.ForEach(triples, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount }, triple =>
            {
                // Temporary filter to limit to single site drive =============================================
                {
                    PDSMetadata metadata = null;
                    this.Storage(triple.Image.Url).GetStorageStream(triple.Image.Url, stream =>
                    {
                        metadata = new PDSMetadata(stream);
                    });

                    PDSParser parser = new PDSParser(metadata);
                    if (parser.Drive != 1472)
                    {
                        logger.Info("Skipped mesh");
                        return;
                    }
                }
                // =============================================================================================

                if (triple.Point == null || triple.Normal == null)
                {
                    return;
                }

                // Get the root transform for each observation (cached by frame) from the alignment project
                var transform = ObservationToRoot(triple.Point, cache);
                var frame = cache.GetFrame(triple.Point.FrameName);

                //For generating a transforms.json file, unsure whether this is still needed
                /*
                int[] rmc = null;
                {
                    Image img = this.Load(new S3ImageRef(triple.Image.Url), true);
                    var pds = new PDSParser((PDSMetadata)img.Metadata);
                    rmc = pds.MotionCounter;
                }

                records.Add(new TransformRecord(transform.Mean, rmc, triple.Point.Name));*/

                //Build the point cloud!
                var m = BuildPointCloud(triple.Point, transform, triple.Image, triple.Normal);
                m.HasColors = true;

                meshes.Add(m);
                logger.Info("Completed " + meshes.Count() + " of " + triples.Count() + " meshes.");
            });
            //Transforms file
            //File.WriteAllText(Path.Combine(options.OutputDirectory, "transforms.json"), JsonHelper.ToJson(records, true));

            var largeMesh = Mesh.Merge(meshes.ToArray());

            //Orbital mesh will come later
            /*
            var orbitalRef = new S3ImageRef("s3://12landlords/TerrainSourceAssets/basemaps/out_deltaradii_smg_1m.tif");
            var identityTransform = new UncertainRigidTransform(Matrix.CreateScale(new Vector3(1, 1, 1)), CreateMatrix.Diagonal<double>(new double[] { 0.0, 0.0, 0.0, 0, 0, 0 }));
            var orbitalMesh = BuildOrbitalCloud(orbitalRef, identityTransform);
            orbitalMesh.HasNormals = true;
            */

            largeMesh.HasNormals = true;
            largeMesh.HasColors = false;
            logger.Info("Attempting to mesh " + largeMesh.Vertices.Count() + " vertices");
            
            //Mesh the point cloud
            largeMesh = PoissonReconstruction.Reconstruct(largeMesh, 1, 10);

            //Get the tiling project (must be same venue as alignment project)
            var tilingProject = TilingProject.Find(DynamoContext, options.TilingProjectName);    
            string meshName = "FullMesh";
            string s3MeshOutputUrl = TileServerConfig.Instance.ChunkUrl(options.TilingProjectName, meshName + ".ply");
            TemporaryFile.GetAndDelete(".ply", tempFile => {
                largeMesh.Save(tempFile);
                this.Storage(s3MeshOutputUrl).UploadFile(tempFile, s3MeshOutputUrl);
            });

            //Create a mesh operator to tile the mesh
            MeshOperator mo = new MeshOperator(largeMesh, buildFaceTree: false, buildUVFaceTree: false);

            //Upload a chunk pointing to the full mesh
            TilingInput.Create(DynamoContext, meshName, tilingProject, s3MeshOutputUrl, null, null);

            //Generate the tiling scheme (variable options to come)
            GenericTilingSchemeOptions gtsOpts = new GenericTilingSchemeOptions();
            gtsOpts.tileSplitCriteria = new VertexSplitCriteria(2000);
            gtsOpts.splitDims = new SplitDim[] { SplitDim.X, SplitDim.Y };
            gtsOpts.splitType = SplitType.Unweighted;
            gtsOpts.treeType = TreeType.N_ary;
            GenericTilingScheme gts = new GenericTilingScheme(gtsOpts);

            TileNode root = gts.SubDivide(mo);
            
            //Upload Tiling Nodes
            Action<TileNode> upload = (tn) =>
            {
                ThroughputManager.Run(() => TilingNode.Create(DynamoContext, tn.id, tilingProject, null, null, tn.Parent?.id, tn.Children.Select(c => c.id).ToList(), null, null, tn.Bounds));
                logger.Info("Created tile " + tn.id);
            };

            root.ApplyRecursive(upload);

            return 0;
        }
      
        /// <summary>
        /// Helper that gets an image/points/normals tuple for a single frame
        /// </summary>
        /// <param name="frameObservations"></param>
        /// <returns></returns>
        public static ImgPntNormTuple FindBestTriple(IEnumerable<RoverObservation> frameObservations)
        {
            var list = frameObservations.Where(ob => ob.UseForReconstruction).ToList();

            list.Sort(MSLProject.RoverObservationComparison);
            var imageList = list.Where(ob => ob.ObservationType == ObservationType.Image.ToString()).ToList();
            var pointList = list.Where(ob => ob.ObservationType == ObservationType.Points.ToString()).ToList();
            var normList = list.Where(ob => ob.ObservationType == ObservationType.Normals.ToString()).ToList();
            if (pointList.Count > 0)
            {
                foreach (var imageObs in imageList)
                {
                    bool linear = MSLProject.IsLinear(imageObs);
                    foreach (var pointObs in pointList)
                    {
                        if (linear == MSLProject.IsLinear(pointObs) && imageObs.Width == pointObs.Width && imageObs.Height == pointObs.Height)
                        {
                            foreach(var normObs in normList)
                            {
                                if(linear == MSLProject.IsLinear(normObs) && imageObs.Width == normObs.Width && imageObs.Height == normObs.Height)
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

        /// <summary>
        /// Get an image/points/normals for each frame, with same preference function as alignment
        /// </summary>
        /// <param name="observations"></param>
        /// <returns></returns>
        public static IEnumerable<ImgPntNormTuple> FindBestTriples(IEnumerable<RoverObservation> observations)
        {
            List<ImgPntNormTuple> results = new List<ImgPntNormTuple>();
            // Filter any that should not be used for reconstruction
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

        /// <summary>
        /// Similar to ImagePointPair, but also contains normals information
        /// </summary>
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

