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
        // input related
        [Value(1, Required = true, Default = null, HelpText = "path to the mesh to build an index for")]
        public string InputMesh { get; set; }

        [Option(HelpText = "path to mesh to use for occlusions, if not provided will use the input mesh", Default = null)]
        public string OcclusionMesh { get; set; }

        // output generation related
        [Option(HelpText = "maximum image resolution per tile", Default = 256)]
        public int OutputTextureResolution { get; set; }

        [Option(HelpText = "percentage of pixels to test before picking a texture during backprojection", Default = 0.1)]
        public double BackprojectGoodnessSamplingPct { get; set; }

        [Option(HelpText = "Output coordinate frame: rover, a numeric sitedrive SSSSSDDDDD, or root", Default = "root")]
        public string OutputFrame { get; set; }

        // observation filtering related (landform standard)
        [Option(HelpText = "Only use observations from a specific site)", Default = -1)]
        public int OnlyForSite { get; set; }

        [Option(HelpText = "Only use observations from a specific drive (can be combined with OnlyForSite)", Default = -1)]
        public int OnlyForDrive { get; set; }

        [Option(HelpText = "Output directory, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }
       
        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,LandformBEV,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Use adjusted transforms only", Default = false)]
        public bool OnlyAligned { get; set; }
      
        // debug related
        [Option(HelpText = "Output bounding box and frustum hull meshes", Default = false)]
        public bool OutputDebugInfo { get; set; }
    }

    public class LocalBuildBackprojectIndex : LandformCommand
    {
        private LocalBuildBackprojectIndexOptions options;

        private MissionSpecific mission;
        private RoverMasker masker;
        
        struct ObservationIndex
        {
            public float Index;
            public Vector2 SourcePixel;
            public Vector2 DestPixel;
        }

        public LocalBuildBackprojectIndex(LocalBuildBackprojectIndexOptions options) : base(options)
        {
            if (options.Cloud)
            {
                throw new NotImplementedException("cloud operation not implemented yet");
            }

            this.options = options;

            var outputFrame = options.OutputFrame.ToLower().Trim();

            if (options.OutputFrame == "rover")
                throw new NotImplementedException("only root and numeric sitedrive are currently supported");

            if (options.UsePriors && options.OnlyAligned)
                throw new InvalidOperationException("cannot specify both --usepriors and --onlyaligned");
        }

        public int Run()
        {
            pipeline.LogInfo("Running local-build-backprojectindex command");

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
            var adjustedSources = FrameTransform.ParseSources(options.AdjustedTransformSources);
            var priorSources = FrameTransform.ParseSources(options.PriorTransformSources);
            var outputFrame = options.OutputFrame.ToLower().Trim();
            string dir = outputFrame + "Frame" + FrameTransform.CreateSourcesPath(adjustedSources, priorSources, options.UsePriors);
            string outputPath = pipeline.GetLocalDebugFolder(options.OutputFolder, dir + "/backprojectindex/", options.ProjectName);
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

            if (!inputMesh.HasUVs)
            {
                pipeline.LogError("input mesh needs UVs");
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
            Dictionary<Observation, ConvexHull> obsToHull = Backproject.BuildConvexHulls(pipeline, outputPath, frameCache, observationCache, options.OutputFrame, options.UsePriors, options.OnlyAligned, imageObservations);
            imageObservations = imageObservations.Where(x => obsToHull.ContainsKey(x));

            // coarse frustum test: get all observations that intersect mesh hull
            pipeline.LogInfo("performing coarse intersections");
            ConvexHull meshHull = new ConvexHull(inputMesh);
            List<Observation> intersectingObservations = GetIntersectingObservations(meshHull, imageObservations, obsToHull, outputPath).ToList();
            pipeline.LogInfo("Found {0} observations instersecting mesh", intersectingObservations.Count());

            // TODO: create index datastructure and serialization

            // collect the destination points to sample
            pipeline.LogInfo("collecting sampling points in destination texture");
            MeshOperator meshOp = new MeshOperator(inputMesh);
            List<PixelPoint> pointsToBackproject = meshOp.SampleUVSpace(options.OutputTextureResolution, options.OutputTextureResolution);
            if (options.OutputDebugInfo)
            {
                Image validUVImg = new Image(1, options.OutputTextureResolution, options.OutputTextureResolution);
                foreach(var pixelPt in pointsToBackproject)
                {
                    validUVImg[0, (int)pixelPt.Pixel.Y, (int)pixelPt.Pixel.X] = 1.0f;
                }

                validUVImg.Save<byte>(Path.Combine(outputPath, "backprojectValidUV.png"));

            }
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

                var contributedPixels = Backproject.BackprojectObservation(pipeline, frameCache, observationCache, sc, (RoverObservation)obs, obsToHull[obs], options.OutputFrame, options.UsePriors, options.OnlyAligned, masker, pointsToBackproject, null);                
                if (contributedPixels.Count() > 0)
                {
                    float obsIndex = GetObservationIndex(obs);
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
                            SourcePixel = contributedPixel.Source,
                            DestPixel = contributedPixel.Dest
                        });
                    }

                    //remove points that successfully backprojected
                    pointsToBackproject = pointsToBackproject.Where(pt => !contributedPixels.Where(cp => cp.Dest == pt.Pixel).Any()).ToList();
                }
            }

            Image outputImage = new Image(3, options.OutputTextureResolution, options.OutputTextureResolution);
            foreach(var entry in indexEntries)
            {
                outputImage.SetBandValues((int)entry.DestPixel.Y, (int)entry.DestPixel.X, new float[] { entry.Index, (float)entry.SourcePixel.Y, (float)entry.SourcePixel.X });
            }

            if (options.OutputDebugInfo)
            {
                Image previewImg = new Image(3, options.OutputTextureResolution, options.OutputTextureResolution);
                Dictionary<float, Vector3> colorsByIndex = new Dictionary<float, Vector3>();
                Random random = NumberHelper.MakeRandomGenerator();
                for (int idxPixel = 0; idxPixel < options.OutputTextureResolution * options.OutputTextureResolution; idxPixel++)
                {
                    float index = outputImage.GetBandValues(idxPixel)[0];
                    if (index == 0)
                        continue;

                    if(!colorsByIndex.ContainsKey(index))
                    {
                        colorsByIndex.Add(index, new Vector3(random.NextDouble(), random.NextDouble(), random.NextDouble()));
                    }

                    previewImg.SetBandValues(idxPixel, colorsByIndex[index].ToFloatArray());
                }

                previewImg.Save<byte>(Path.Combine(outputPath, "backprojectPreview.png"));
            }

            outputImage.Save<float>(Path.Combine(outputPath, "backprojectIndex.tif"));
            
            return 0;
        }

        static float placeholderIndex = 0;
        private float GetObservationIndex(Observation obs)
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
    }
}
