using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;
using OPS.RayTrace;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OPS.Landform
{
    [Verb("local-build-backprojectindex", HelpText = "builds an index of images to use for texturing a mesh")]
    public class LocalBuildBackprojectIndexOptions : LandformCommandOptions
    {
        [Value(1, Required = true, Default = null, HelpText = "path to the mesh to build an index for")]
        public string InputMesh { get; set; }

        [Option(HelpText = "path to mesh to use for occlusions, if not provided will usee the input mesh", Default = null)]
        public string OcclusionMesh { get; set; }

        [Option(HelpText = "Only use observations from a specific site)", Default = -1)]
        public int OnlyForSite { get; set; }

        [Option(HelpText = "Only use observations from a specific drive (can be combined with OnlyForSite)", Default = -1)]
        public int OnlyForDrive { get; set; }

        [Option(HelpText = "Output directory, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        [Option(HelpText = "Output coordinate frame: rover, a numeric sitedrive SSSSSDDDDD, or root", Default = "root")]
        public string OutputFrame { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,LandformBEV,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Use adjusted transforms only", Default = false)]
        public bool OnlyAligned { get; set; }

        [Option(HelpText = "tiling scheme (axis letters indicate the up direction):  Bin, QuadX, QuadY, QuadZ, Oct", Default = TilingScheme.Bin)]
        public TilingScheme TilingScheme { get; set; }

        [Option(HelpText = "target maximum faces per tile", Default = 2000)]
        public int FacesPerTile { get; set; }

        [Option(HelpText = "maximum image resolution per tile", Default = 256)]
        public int OutputTextureResolution { get; set; }

        [Option(HelpText = "Output bounding box and frustum hull meshes", Default = false)]
        public bool OutputDebugInfo { get; set; }

        [Option(HelpText = "Don't inpaint output to fill seams and holes when backprojecting", Default = false)]
        public bool DontInpaint { get; set; }

        [Option(HelpText = "Debug function that skips all tiles except that one with this name", Default = null)]
        public string OnlyTileNamed { get; set; }

        [Option(Required = false, Default = SkirtMode.None, HelpText = "Axis to use as up in quad tree tiling")]
        public SkirtMode SkirtAxis { get; set; }

        [Option(Required = false, Default = "b3dm", HelpText = "Mesh Extension")]
        public string MeshExtension { get; set; }

        [Option(Required = false, Default = "jpg", HelpText = "Image Extension")]
        public string ImageExtension { get; set; }

        [Option(HelpText = "clip the full mesh to this half this length on the x and y axes, centered at 0,0,0", Default = 0.0)]
        public double ClipExtent { get; set; }

        [Option(HelpText = "percentage of pixels to test when deciding to split a tile based on resolution (speed vs quality)", Default = 0.1)]
        public double SplitByTexturePctToTest { get; set; }

        [Option(HelpText = "percentage of pixels tested that should satisfy the requirement to avoid splitting a tile", Default = 0.5)]
        public double SplitByTexturePctSatisfied { get; set; }

        [Option(HelpText = "the area of source pixels mapped to a single destination pixel that would trigger a split", Default = 4.5)]
        public double SplitByTextureSamplingRatio { get; set; }

        [Option(HelpText = "percentage of pixels to test before picking a texture during backprojection", Default = 0.1)]
        public double BackprojectGoodnessSamplingPct { get; set; }

        [Option(Required = false, HelpText = "a url to a bucket in the form: s3://<bucket>/<path>/ ")]
        public string OutputS3Bucket { get; set; }

        [Option(Required = false, HelpText = "the aws profile used for credentials for uploading tileset")]
        public string AWSProfile { get; set; }

        [Option(Required = false, Default = "us-gov-west-1", HelpText = "the aws endpoint for the destination tileset bucket")]
        public string AWSRegion { get; set; }
    }

    public class LocalBuildBackprojectIndex : LandformCommand
    {
        private LocalBuildBackprojectIndexOptions options;

        private MissionSpecific mission;
        private RoverMasker masker;
        
        struct ObservationIndex
        {
            public int Index;
            public Vector2 Pixel;
        }

        public LocalBuildBackprojectIndex(LocalBuildBackprojectIndexOptions options) : base(options)
        {
            if (options.Cloud)
            {
                throw new NotImplementedException("cloud operation not implemented yet");
            }

            this.options = options;

            var outputFrame = options.OutputFrame.ToLower().Trim();

            bool providedBucket = !string.IsNullOrEmpty(options.OutputS3Bucket);
            bool providedProfile = !string.IsNullOrEmpty(options.AWSProfile);
            if (providedBucket != providedProfile)
            {
                pipeline.LogError("To save tileset to the cloud you must provide the OutputS3Bucket and AWSProfile (and optionally AWSRegion) options");
                this.options.AWSProfile = string.Empty;
                this.options.OutputS3Bucket = string.Empty;
            }

            if (options.OutputFrame == "rover")
                throw new NotImplementedException("only root and numeric sitedrive are currently supported");

            if (options.UsePriors && options.OnlyAligned)
                throw new InvalidOperationException("cannot specify both --usepriors and --onlyaligned");
        }

        public int Run()
        {
            pipeline.LogInfo("Running local-build-meshes command");

            //collect project data
            var project = Project.Find(pipeline, options.ProjectName);
            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }
            mission = MissionSpecific.GetInstance(project.Mission);
            masker = mission.GetMasker();

            //create directory for output
            var adjustedSources = ParseSources(options.AdjustedTransformSources);
            var priorSources = ParseSources(options.PriorTransformSources);
            var outputFrame = options.OutputFrame.ToLower().Trim();
            string dir = outputFrame + "Frame" + CreateSourcesPath(adjustedSources, priorSources);
            string outputPath = pipeline.GetLocalDebugFolder(options.OutputFolder, "backprojectindex/" + dir, options.ProjectName);
            PathHelper.EnsureExists(outputPath);

            //get transforms
            pipeline.LogInfo("Populating frame and observation cache");
            FrameCache frameCache = new FrameCache(pipeline, options.ProjectName);
            frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, options.UsePriors);

            ObservationCache observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache
                .Preload(obs => obs.UseForReconstruction &&
                         ((options.OnlyForSite == -1) || options.OnlyForSite == ((RoverObservation)obs).Site) &&
                         ((options.OnlyForDrive == -1) || options.OnlyForDrive == ((RoverObservation)obs).Drive));

            // load input full mesh
            Mesh inputMesh = LoadMesh(options.InputMesh);
            if (inputMesh == null)
            {
                pipeline.LogError("failed to build or load input mesh {0}", options.InputMesh);
                return 1;
            }

            //set up raycasting for occlusion
            pipeline.LogInfo("Building occlusion data structures");
            Mesh occlusionMesh = null;
            if (string.IsNullOrEmpty(options.OcclusionMesh))
            {
                occlusionMesh = new Mesh(inputMesh); //can't change mesh after adding to collider
            }
            else
            {
                occlusionMesh = LoadMesh(options.OcclusionMesh);
                if (occlusionMesh == null)
                {
                    pipeline.LogError("failed to build or load occlusion mesh {1}", options.OcclusionMesh);
                    return 1;
                }
            }
            SceneCaster sc = new SceneCaster();
            sc.AddMesh(occlusionMesh, null, Matrix.Identity);
            sc.Build();

            //get image observations
            string imageObsType = ObservationType.Image.ToString();
            IEnumerable<Observation> imageObservations = observationCache.GetAllObservations().Where(obs => obs.ObservationType == imageObsType);

            //build convex hulls for image observations
            pipeline.LogInfo("Building observation hulls");
            Dictionary<Observation, ConvexHull> obsToHull = null;
            BuildConvexHulls(outputPath, frameCache, observationCache, imageObservations, out obsToHull);
            imageObservations = imageObservations.Where(x => obsToHull.ContainsKey(x));

            // coarse frustum test: get all observations that intersect mesh hull
            pipeline.LogInfo("performing coarse intersections");
            ConvexHull meshHull = new ConvexHull(inputMesh);
            List<Observation> intersectingObservations = GetIntersectingObservations(meshHull, imageObservations, obsToHull, outputPath).ToList();
            pipeline.LogInfo("Found {0} observations instersecting mesh", intersectingObservations.Count());

            // TODO: create index datastructure and serialization

            // collect the destination points to sample
            pipeline.LogInfo("collecting sampling points in destination texture");
            MeshOperator meshOp = new MeshOperator(inputMesh, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
            List<PixelPoint> pointsToBackproject = meshOp.SampleUVSpace(options.OutputTextureResolution, options.OutputTextureResolution);

            //calculate goodness (spatial density)
            Dictionary<Observation, double> spatialDensityByObs = CalculateSpatialDensity(frameCache, sc, obsToHull, pointsToBackproject, intersectingObservations);

            //sort the list of observations by goodness
            intersectingObservations.Sort((obs1, obs2) => spatialDensityByObs[obs1].CompareTo(spatialDensityByObs[obs2]));

            //for each source image, sweep through all valid destination pixels (not atlas gutter pixels)
            List<ObservationIndex> indexEntries = new List<ObservationIndex>();
            foreach (var obs in intersectingObservations)
            {
                //quit if done
                if (pointsToBackproject.Count == 0)
                    break;

                var contributedPixels = BackprojectObservation(frameCache, observationCache, sc, (RoverObservation)obs, obsToHull[obs], ref pointsToBackproject, null);
                
                if (contributedPixels.Count() > 0)
                {
                    int obsIndex = GetObservationIndex(obs);
                    pipeline.LogInfo("Obs index {0}: {1}", obsIndex, obs.Name);
                    
                    if (options.OutputDebugInfo)
                    {
                        obsToHull[obs].Mesh.Save(Path.Combine(outputPath, obs.Name + "_chull.ply"));

                        Image dbgimg = pipeline.LoadImage(obs.Url);
                        dbgimg.Save<byte>(Path.Combine(outputPath, obs.Name + ".png"));
                    }

                    foreach (var contributedPixel in contributedPixels)
                    {
                        indexEntries.Add(new ObservationIndex()
                        {
                            Index = obsIndex,
                            Pixel = contributedPixel.Pixel
                        });
                    }
                }
            }
            
            //TODO: save image          
            return 0;
        }

        static int placeholderIndex = 0;
        private int GetObservationIndex(Observation obs)
        {
            return placeholderIndex++;
        }

        IEnumerable<Observation> GetIntersectingObservations(ConvexHull meshHull, IEnumerable<Observation> imageObservations, Dictionary<Observation, ConvexHull> obsToHull, string outputPath)
        {
            List<Observation> intersectingObservations = new List<Observation>();
            foreach (var obs in imageObservations)
            {
                if (!obsToHull.ContainsKey(obs))
                    continue;

                if (meshHull.Intersects(obsToHull[obs]))
                {
                    pipeline.LogDebug("intersecting observation {0}:{1}", intersectingObservations.Count(), obs.Name);
                    if (options.OutputDebugInfo)
                    {
                        obsToHull[obs].Mesh.Save(Path.Combine(outputPath, obs.Name + "_ihull.ply"));
                    }
                    intersectingObservations.Add(obs);
                }
            }
            return intersectingObservations;
        }
         
        private Dictionary<Observation, double> CalculateSpatialDensity(FrameCache frameCache, SceneCaster sc, Dictionary<Observation, ConvexHull> obsToHull, List<PixelPoint> pointsToBackproject, IEnumerable<Observation> intersectingObservations)
        {
            Dictionary<Observation, double> spatialDensityByObs = new Dictionary<Observation, double>();

            //select a coarse sampling of the points to backproject
            //to get a rough sorting of texture quality
            double percentagePointsToTest = options.BackprojectGoodnessSamplingPct;

            //simple sample which skips enough points to return the requested amount of points
            int subsampledPts = Math.Max(1, (int)(pointsToBackproject.Count * percentagePointsToTest));
            int skipPoints = pointsToBackproject.Count / subsampledPts;
            List<PixelPoint> pointsToTestSamplingDensity =
                pointsToBackproject.Where((pt, index) => index % skipPoints == 0).ToList();

            //calculate the median spatial density for the requested pixels per observation
            foreach (var obs in intersectingObservations.Cast<RoverObservation>())
            {
                CameraModel cameraModel = (CameraModel)JsonHelper.FromJson(obs.CameraModel);

                //test hull (protect against bad ray calculations from camera model)
                if (!obsToHull.ContainsKey(obs))
                    continue;

                var obsToOutput = frameCache.GetObservationTransform(obs, options.OutputFrame,
                                                                     options.UsePriors, options.OnlyAligned);
                if (obsToOutput == null)
                {
                    continue;
                }

                List<double> minDistances = new List<double>(capacity: pointsToTestSamplingDensity.Count());
                foreach (var pt in pointsToTestSamplingDensity)
                {
                    if (!obsToHull[obs].Contains(pt.Point))
                        continue;

                    //Issue #523: want median or average in case glancing angle?
                    //want a term that looks for consistancy in spacing? implies dead on?
                    minDistances.Add(TextureSplitCriteria
                                     .GetMinPixelSpreadInMeters(sc, cameraModel, obsToOutput.Mean,
                                                                obsToHull[obs], pt.Pixel, pt.Point,
                                                                obs.Width, obs.Height));
                }

                //store the median of the min distances
                double medianDistance = double.MaxValue;
                if (minDistances.Count() > 0)
                {
                    minDistances.Sort();
                    medianDistance = minDistances.ElementAt(minDistances.Count / 2);
                }

                spatialDensityByObs.Add(obs, medianDistance);
            }

            return spatialDensityByObs;
        }

        private void BuildConvexHulls(string leafTilesPath, FrameCache frameCache, ObservationCache observationCache,
                                      IEnumerable<Observation> imageObservations, out Dictionary<Observation,
                                      ConvexHull> obsToHull)
        {
            pipeline.LogInfo("Building convex hulls");
            obsToHull = new Dictionary<Observation, ConvexHull>();
            foreach (var obs in imageObservations)
            {
                pipeline.LogInfo("Building hull for {0}, {1}/{2} ({3}%)",
                                 obs.Name, obsToHull.Count(), imageObservations.Count(),
                                 (int)(100 * obsToHull.Count() / (float)imageObservations.Count()));
                var meshObs = new MeshObservations() { Texture = obs };
                var meshOpts = new MeshObservations.MeshOptions()
                {
                    Frame = options.OutputFrame,
                    UsePriors = options.UsePriors
                };
                ConvexHull obsHull = meshObs.BuildFrustumHull(pipeline, frameCache, meshOpts,
                                                              uncertaintyInflated: false);
                if (obsHull != null)
                {
                    obsToHull.Add(obs, obsHull);

                    if (options.OutputDebugInfo)
                    {
                        obsHull.Mesh.Save(Path.Combine(leafTilesPath, obs.Name + "_hull.ply"));
                    }
                }
                else
                {
                    pipeline.LogWarn("failed to build hull for {0}", obs.Name);
                }
            }
        }

        private CameraInstance ToCameraInstance(RoverObservation obs, Dictionary<Observation, ConvexHull> obsToHull,
                                                FrameCache frameCache)
        {
            var xform = frameCache.GetObservationTransform(obs, options.OutputFrame, options.UsePriors);
            if (xform == null)
            {
                return null;
            }
            CameraInstance camInst = new CameraInstance();
            camInst.cameraToMesh = xform.Mean;
            camInst.meshToCamera = Matrix.Invert(camInst.cameraToMesh);
            camInst.cameraModel = (CameraModel)JsonHelper.FromJson(obs.CameraModel);
            camInst.hullInMesh = obsToHull[obs];
            camInst.widthPixels = obs.Width;
            camInst.heightPixels = obs.Height;
            return camInst;
        }

        private Mesh LoadMesh(string pathToMesh)
        {
            pipeline.LogInfo("Loading input mesh from {0}", pathToMesh);
            Mesh mesh = Mesh.Load(pathToMesh);
            if (mesh == null)
            {
                pipeline.LogError("Loading mesh from {0} failed.", pathToMesh);
                return null;
            }

            if (mesh.Vertices.Count() == 0 || mesh.Faces.Count() == 0)
            {
                pipeline.LogError("mesh {0} was invalid. mesh has {1} vertices and {2} faces", pathToMesh, mesh.Vertices.Count(), mesh.Faces.Count());
                return null;
            }
            return mesh;
        }

        private List<PixelPoint> BackprojectObservation(FrameCache frameCache, ObservationCache obsCache, SceneCaster sc,
                                           RoverObservation obs, ConvexHull obsHull,
                                           ref List<PixelPoint> pointsToBackproject, Image outputImage)
        {
            var xform = frameCache.GetObservationTransform(obs, options.OutputFrame,
                                                           options.UsePriors, options.OnlyAligned);
            if (xform == null)
            {
                return null;
            }

            List<PixelPoint> backprojectedPoints = new List<PixelPoint>();

            Matrix obsToMesh = xform.Mean;
            Matrix meshToObs = Matrix.Invert(obsToMesh);
            CameraModel camera = (CameraModel)JsonHelper.FromJson(obs.CameraModel);

            Image img = pipeline.LoadImage(obs.Url);

            //want the version with border pixels and invalid pixels
            string maskType = ObservationType.RoverMask.ToString();
            var maskObs =
                obsCache
                .GetAllObservationsForFrame(frameCache.GetFrame(obs.FrameName))
                .Where(o => o.ObservationType == maskType)
                .FirstOrDefault(); ;
            Image mask =
                FeatureDetecting.MakeMask(pipeline, masker, maskObs == null ? null : maskObs.Url, img, obs.Name);
            int pointsToBackprojectCount = pointsToBackproject.Count();
            List<PixelPoint> failedToBackproject = new List<PixelPoint>();
            while (pointsToBackproject.Count() > 0)
            {
                var pixelpoint = pointsToBackproject.First();
                pointsToBackproject.RemoveAt(0);

                Vector3 meshPos = pixelpoint.Point;

                bool failedToBackprojectPoint = true;

                // validate surface point is in the frustum to avoid camera model issues with offscreen points
                if (obsHull.Contains(meshPos))
                {
                    //project into observation
                    Vector3 obsPos = Vector3.Transform(meshPos, meshToObs);
                    Vector2 obsPixel = camera.Project(obsPos, out double rangeMeshToImage);

                    //sanity check
                    if (rangeMeshToImage <= 0 ||
                        (int)obsPixel.X < 0 || (int)obsPixel.X >= obs.Width ||
                        (int)obsPixel.Y < 0 || (int)obsPixel.Y >= obs.Height)
                        throw new InvalidDataException("should have been caught by frustum test");

                    //test if rover masked or missing data (any neighbor pixels that are set to zero
                    // will cause the bilinear sample to be less than 1
                    if (mask.BilinearSample(0, (float)obsPixel.Y, (float)obsPixel.X) >= 0.9)
                    {
                        // raycast the scene to test if the desired position is occluded by terrain
                        if (!TextureSplitCriteria
                            .IsOccluded(camera, obsPixel, meshPos, sc, rangeMeshToImage, obsToMesh))
                        {
                            //copy src image data to dst image data
                            if (outputImage != null)
                            {
                                float[] samples = img.SampleAsColor(obsPixel);
                                outputImage.SetAsColor(samples, (int)pixelpoint.Pixel.Y, (int)pixelpoint.Pixel.X);

                                //mark mask as valid
                                outputImage.SetMaskValue((int)pixelpoint.Pixel.Y, (int)pixelpoint.Pixel.X, false);
                            }

                            failedToBackprojectPoint = false;
                        }
                    }
                }

                //add to failed
                if (failedToBackprojectPoint)
                {
                    failedToBackproject.Add(pixelpoint);
                }
            }

            backprojectedPoints = pointsToBackproject.Where(pt => !failedToBackproject.Contains(pt)).ToList();
            pointsToBackproject = failedToBackproject;
            return backprojectedPoints;
        }

        //TODO: share
        private string CreateSourcesPath(TransformSource[] adjustedSources, TransformSource[] priorSources)
        {
            string sourcesString = string.Empty;
            if (options.UsePriors)
            {
                sourcesString += "/prior";
                if (priorSources.Length > 0)
                {
                    sourcesString += "_" + String.Join("_", priorSources);
                }
            }
            else
            {
                sourcesString += "/best";
                if (priorSources.Length > 0)
                {
                    sourcesString += "_" + String.Join("_", priorSources);
                }
                if (adjustedSources.Length > 0)
                {
                    sourcesString += "_" + String.Join("_", adjustedSources);
                }
            }

            return sourcesString;
        }

        private TransformSource[] ParseSources(string sources)
        {
            return (sources ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => Enum.Parse(typeof(TransformSource), s.Trim(), ignoreCase: true))
                .Cast<TransformSource>()
                .ToArray();
        }
    }
}
