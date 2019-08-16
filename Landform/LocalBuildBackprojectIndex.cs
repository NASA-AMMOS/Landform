using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Diagnostics;
using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.RayTrace;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;

namespace OPS.Landform
{
    [Verb("local-build-backprojectindex", HelpText = "builds an index of images to use for texturing a mesh")]
    public class LocalBuildBackprojectIndexOptions : LandformCommandOptions
    {
        // input related
        [Value(1, Required = true, Default = null, HelpText = "Mesh to backproject")]
        public string InputMesh { get; set; }

        [Option(HelpText = "Occlusion mesh in same frame as input mesh, defaults to input mesh", Default = null)]
        public string OcclusionMesh { get; set; }

        [Option(HelpText = "Mesh coordinate frame: a numeric sitedrive SSSSSDDDDD or root", Default = "root")]
        public string MeshFrame { get; set; }

        // output related
        [Option(HelpText = "Image resolution for output texture", Default = 4096)]
        public int OutputTextureResolution { get; set; }

        [Option(HelpText = "Percentage of pixels to test before picking a texture during backprojection", Default = 0.1)]
        public double BackprojectGoodnessSamplingPct { get; set; }

        // observation filtering related (landform standard)
        [Option(HelpText = "Only use specific cameras, comma separated (FrontHazcamLeft, FrontHazcamRight, RearHazcamLeft, RearHazcamRight, NavcamLeft, NavcamRight, MastcamLeft, MastcamRight, MAHLI)", Default = null)]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Only use observations from specific site drives, comma separated", Default = null)]
        public string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,LandformBEV,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Use adjusted transforms only", Default = false)]
        public bool OnlyAligned { get; set; }

        // debug related
        [Option(HelpText = "Output debug products", Default = false)]
        public bool WriteDebug { get; set; }

        [Option(HelpText = "Debug output directory, or omit to save to project storage", Default = null)]
        public string DebugOutputFolder { get; set; }

        [Option(HelpText = "Debug mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public string MeshFormat { get; set; }

        [Option(HelpText = "Debug image format, e.g. png, jpg, help for list", Default = "png")]
        public string ImageFormat { get; set; }
    }

    public class LocalBuildBackprojectIndex : LandformCommand
    {
        private LocalBuildBackprojectIndexOptions options;

        private MissionSpecific mission;
        private RoverMasker masker;

        private string outputPath;
        private string imageExt;
        private string meshExt;

        struct ObservationIndex
        {
            public float Index;
            public Vector2 SourcePixel;
            public Vector2 DestPixel;
        }

        public LocalBuildBackprojectIndex(LocalBuildBackprojectIndexOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            if (options.UsePriors && options.OnlyAligned)
            {
                pipeline.LogError("cannot specify both --usepriors and --onlyaligned");
                return 1;
            }

            var project = Project.Find(pipeline, options.ProjectName);
            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }
            mission = MissionSpecific.GetInstance(project.Mission);
            masker = mission.GetMasker();

            var meshFrame = options.MeshFrame;
            FrameTransform.ParseFrameName(ref meshFrame, out bool specificSiteDrive);
            if (!specificSiteDrive && meshFrame != "root")
            {
                pipeline.LogError("unsupported mesh frame: " + meshFrame);
                return 1;
            }

            var adjustedSources = FrameTransform.ParseSources(options.AdjustedTransformSources);
            var priorSources = FrameTransform.ParseSources(options.PriorTransformSources);

            string dir = "meshing/TextureProducts";
            dir = FrameTransform.AppendSourcesPath(dir, adjustedSources, priorSources, options.UsePriors);
            outputPath = pipeline.GetLocalDebugFolder(options.DebugOutputFolder, dir, options.ProjectName);
            //don't ensure outputPath exists here, we may never need it

            if (options.WriteDebug)
            {
                meshExt = MeshSerializers.Instance.CheckFormat(options.MeshFormat, pipeline);
                if (meshExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} debug meshes to {1}", meshExt, outputPath);
            
                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} debug images to {1}", imageExt, outputPath);
            }

            SiteDrive[] siteDrives = (options.OnlyForSiteDrives ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => new SiteDrive(s.Trim()))
                .ToArray();

            string[] cameras = (options.OnlyForCameras ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            var frameCache = new FrameCache(pipeline, options.ProjectName);
            frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, options.UsePriors);

            string imageObs = ObservationType.Image.ToString();
            string maskObs = ObservationType.RoverMask.ToString();
            var observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.
                Preload(obs => obs.UseForReconstruction &&
                        (obs.ObservationType == imageObs || obs.ObservationType == maskObs) &&
                        (siteDrives.Length == 0 || siteDrives.Any(sd => sd == ((RoverObservation)obs).SiteDrive)) &&
                        (cameras.Length == 0 || cameras.Any(cam => cam == ((RoverObservation)obs).Sensor)));

            //TODO load input mesh from database
            pipeline.LogInfo("loading input mesh {0}", options.InputMesh);
            Mesh inputMesh = Mesh.Load(options.InputMesh);
            if (inputMesh == null)
            {
                pipeline.LogError("failed to load input mesh");
                return 1;
            }
            if (inputMesh.Faces.Count == 0)
            {
                pipeline.LogError("input mesh empty");
                return 1;
            }
            if (!inputMesh.HasUVs)
            {
                pipeline.LogError("input mesh needs UVs");
                return 1;
            }

            Mesh occlusionMesh = null;
            if (!string.IsNullOrEmpty(options.OcclusionMesh))
            {
                pipeline.LogInfo("loading occlusion mesh {0}", options.OcclusionMesh);
                occlusionMesh = Mesh.Load(options.OcclusionMesh);
                if (occlusionMesh == null)
                {
                    pipeline.LogError("failed to load occlusion mesh");
                    return 1;
                }
                if (occlusionMesh.Faces.Count == 0)
                {
                    pipeline.LogError("occlusion mesh empty");
                    return 1;
                }
            }
            else
            {
                occlusionMesh = new Mesh(inputMesh);
            }

            pipeline.LogInfo("building occlusion data structures");
            var sc = new SceneCaster();
            sc.AddMesh(occlusionMesh, null, Matrix.Identity); //NOTE: can't change mesh after adding to collider
            sc.Build();

            //get image observations
            string imageObsType = ObservationType.Image.ToString();
            IEnumerable<Observation> imageObservations = observationCache.GetAllObservations().Where(obs => obs.ObservationType == imageObsType);

            //build convex hulls for image observations
            pipeline.LogInfo("Building observation hulls");
            var obsToHullTmp = Backproject.BuildConvexHulls(pipeline, frameCache, meshFrame, options.UsePriors, options.OnlyAligned, imageObservations);
            var obsToHull = new Dictionary<Observation, ConvexHull>();
            foreach (var entry in obsToHullTmp)
            {
                obsToHull[observationCache.GetObservation(entry.Key)] = entry.Value;
            }
            imageObservations = imageObservations.Where(x => obsToHull.ContainsKey(x));

            // coarse frustum test: get all observations that intersect mesh hull
            pipeline.LogInfo("performing coarse intersections");
            ConvexHull meshHull = new ConvexHull(inputMesh);
            List<Observation> intersectingObservations = GetIntersectingObservations(meshHull, imageObservations, obsToHull, outputPath).ToList();
            pipeline.LogInfo("Found {0} observations intersecting mesh", intersectingObservations.Count());
                
            // collect the destination points to sample
            pipeline.LogInfo("collecting sampling points in destination texture");
            MeshOperator meshOp = new MeshOperator(inputMesh);
            List<PixelPoint> pointsToBackproject = meshOp.SampleUVSpace(options.OutputTextureResolution, options.OutputTextureResolution);
            if (options.WriteDebug)
            {
                pipeline.LogInfo("generating debug uv validity image");
                Image validUVImg = new Image(1, options.OutputTextureResolution, options.OutputTextureResolution);
                foreach (var pixelPt in pointsToBackproject)
                {
                    validUVImg[0, (int)pixelPt.Pixel.Y, (int)pixelPt.Pixel.X] = 1.0f;
                }

                PathHelper.EnsureExists(outputPath);
                validUVImg.Save<byte>(Path.Combine(outputPath, "backprojectValidUV.png"));
            }

            //calculate goodness (spatial density)
            pipeline.LogInfo("calculating spatial density");
            Dictionary<Observation, double> projectedPixelDistances = ProjectedPixelDistances.Calculate(frameCache, sc, obsToHull, options.BackprojectGoodnessSamplingPct, meshFrame, options.UsePriors, options.OnlyAligned, pointsToBackproject, intersectingObservations);

            //sort the list of observations by goodness
            intersectingObservations.Sort((obs1, obs2) => projectedPixelDistances[obs1].CompareTo(projectedPixelDistances[obs2]));

            //for each source image, sweep through all valid destination pixels (not atlas gutter pixels)
            pipeline.LogInfo("backprojecting observations");
            List<ObservationIndex> indexEntries = new List<ObservationIndex>();
            foreach (var obs in intersectingObservations)
            {
                //quit if done
                if (pointsToBackproject.Count == 0)
                    break;

                //backproject the destination pixels to find which source pixels should be used
                var contributedPixels = Backproject.BackprojectObservation(pipeline, frameCache, observationCache, sc, (RoverObservation)obs, obsToHull[obs], meshFrame, options.UsePriors, options.OnlyAligned, masker, pointsToBackproject, null);
                if (contributedPixels.Count() > 0)
                {
                    float obsIndex = GetObservationIndex(obs);
                    pipeline.LogInfo("Obs index {0}: {1}", obsIndex, obs.Name);

                    if (options.WriteDebug)
                    {
                        obsToHull[obs].Mesh.Save(Path.Combine(outputPath, obs.Name + "_chull.ply"));

                        Image dbgimg = pipeline.LoadImage(obs.Url);
                        PathHelper.EnsureExists(outputPath);
                        dbgimg.Save<byte>(Path.Combine(outputPath, obs.Name + ".png"));
                    }

                    foreach (var contributedPixel in contributedPixels)
                    {
                        indexEntries.Add(new ObservationIndex()
                        {
                            Index = obsIndex,
                            SourcePixel = contributedPixel.Value,
                            DestPixel = contributedPixel.Key
                        });
                    }

                    //remove points that successfully backprojected from this observation
                    pointsToBackproject = pointsToBackproject.Where(pt => !contributedPixels.ContainsKey(pt.Pixel)).ToList();
                }
            }

            //fill in output index
            pipeline.LogInfo("populating output index texture");
            Image outputImage = new Image(3, options.OutputTextureResolution, options.OutputTextureResolution);
            foreach (var entry in indexEntries)
            {
                outputImage.SetBandValues((int)entry.DestPixel.Y, (int)entry.DestPixel.X, new float[] { entry.Index, (float)entry.SourcePixel.Y, (float)entry.SourcePixel.X });
            }

            if (options.WriteDebug)
            {
                pipeline.LogInfo("generating debug preview image");
                Image previewImg = new Image(3, options.OutputTextureResolution, options.OutputTextureResolution);
                Dictionary<float, Vector3> colorsByIndex = new Dictionary<float, Vector3>();
                Random random = NumberHelper.MakeRandomGenerator();
                for (int idxPixel = 0; idxPixel < options.OutputTextureResolution * options.OutputTextureResolution; idxPixel++)
                {
                    float index = outputImage.GetBandValues(idxPixel)[0];
                    if (index == 0)
                        continue;

                    if (!colorsByIndex.ContainsKey(index))
                    {
                        colorsByIndex.Add(index, new Vector3(random.NextDouble(), random.NextDouble(), random.NextDouble()));
                    }

                    previewImg.SetBandValues(idxPixel, colorsByIndex[index].ToFloatArray());
                }

                PathHelper.EnsureExists(outputPath);
                previewImg.Save<byte>(Path.Combine(outputPath, "backprojectPreview.png"));
            }

            outputImage.Save<float>(Path.Combine(outputPath, "backprojectIndex.tif"));

            stopwatch.Stop();
            pipeline.LogInfo("elapsed time {0:F3}s", 0.001 * stopwatch.ElapsedMilliseconds);

            return 0;
        }

        //TODO: placeholder function to be replaced by the observation index in the database
        static float placeholderIndex = 0;
        private float GetObservationIndex(Observation obs)
        {
            return placeholderIndex++;
        }

        IEnumerable<Observation> GetIntersectingObservations(ConvexHull meshHull, IEnumerable<Observation> imageObservations, Dictionary<Observation, ConvexHull> obsToHull, string outputPath)
        {
            List<Observation> intersectingObservations = new List<Observation>();

            CoreLimitedParallel.ForEach(imageObservations, obs =>
            {
                if (obsToHull.ContainsKey(obs))
                {
                    if (meshHull.Intersects(obsToHull[obs]))
                    {
                        pipeline.LogDebug("intersecting observation {0}:{1}", intersectingObservations.Count(), obs.Name);
                        if (options.WriteDebug)
                        {
                            PathHelper.EnsureExists(outputPath);
                            obsToHull[obs].Mesh.Save(Path.Combine(outputPath, obs.Name + "_ihull.ply"));
                        }

                        lock (intersectingObservations)
                        {
                            intersectingObservations.Add(obs);
                        }
                    }
                }
            });
            return intersectingObservations;
        }
    }
}
