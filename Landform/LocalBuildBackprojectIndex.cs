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
        [Value(1, Required = false, Default = null, HelpText = "Mesh to backproject, search project storage if omitted")]
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

            //try to load SceneMesh record from database even if options.InputMesh is going to override it
            //because if it exists we will update it below with the generated backproject index image
            SceneMesh sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame);

            Mesh inputMesh = null;
            if (!string.IsNullOrEmpty(options.InputMesh))
            {
                pipeline.LogInfo("loading input mesh from {0}", options.InputMesh);
                inputMesh = Mesh.Load(pipeline.GetFileCached(options.InputMesh, "meshes"));
            }
            else if (sceneMesh != null)
            {
                if (sceneMesh.MeshGuid != Guid.Empty)
                {
                    pipeline.LogInfo("loading scene mesh in frame {0} from database", meshFrame);
                    inputMesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, sceneMesh.MeshGuid).Mesh;
                }
                else
                {
                    pipeline.LogError("scene mesh in frame {0} in database but without mesh", meshFrame);
                    return 1;
                }
            }
            else
            {
                pipeline.LogError("no input mesh specified and no scene mesh in frame {0} in database", meshFrame);
                return 1;
            }

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
                occlusionMesh = Mesh.Load(pipeline.GetFileCached(options.OcclusionMesh, "meshes"));
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

            var imageObservations = observationCache.GetAllObservations().Where(obs => obs.ObservationType == imageObs);

            var obsToHull = Backproject.BuildConvexHulls(pipeline, frameCache, meshFrame, options.UsePriors,
                                                         options.OnlyAligned, imageObservations);
            if (options.WriteDebug)
            {
                pipeline.LogInfo("saving {0} observation hull meshes", obsToHull.Count);
                foreach (var entry in obsToHull)
                {
                    SaveDebugMesh(entry.Value.Mesh, entry.Key + "_hull");
                }
            }
            
            imageObservations = imageObservations.Where(obs => obsToHull.ContainsKey(obs.Name));

            //coarse frustum test: get all observations that intersect mesh hull
            pipeline.LogInfo("performing coarse intersections");
            var meshHull = new ConvexHull(inputMesh);
            if (options.WriteDebug)
            {
                pipeline.LogInfo("saving full mesh hull");
                SaveDebugMesh(meshHull.Mesh, "fullMeshHull");
            }
            var intersecting = new ConcurrentBag<string>();
            CoreLimitedParallel.ForEach(imageObservations, obs =>
            {
                if (meshHull.Intersects(obsToHull[obs.Name]))
                {
                    intersecting.Add(obs.Name);
                }
            });
            var intersectingObservations = intersecting
                .Distinct()
                .Select(obsName => observationCache.GetObservation(obsName))
                .ToList();
            pipeline.LogInfo("found {0} observations intersecting mesh", intersectingObservations.Count());
                
            pipeline.LogInfo("collecting sampling points in destination texture");
            int outRes = options.OutputTextureResolution; 
            var meshOp = new MeshOperator(inputMesh);
            List<PixelPoint> pointsToBackproject = meshOp.SampleUVSpace(outRes, outRes);
            if (options.WriteDebug)
            {
                pipeline.LogInfo("saving backproject validity image");
                Image validUV = new Image(1, outRes, outRes);
                foreach (var pixelPt in pointsToBackproject)
                {
                    validUV[0, (int)pixelPt.Pixel.Y, (int)pixelPt.Pixel.X] = 1.0f;
                }
                SaveDebugImage(validUV, "backprojectValidUV");
            }

            pipeline.LogInfo("calculating spatial density");

            //for each source image, sweep through all valid destination pixels (not atlas gutter pixels)
            var projectedPixelDistances = ProjectedPixelDistances.Calculate(frameCache, sc, meshHull, obsToHull,
                                                                            options.BackprojectGoodnessSamplingPct,
                                                                            meshFrame, options.UsePriors,
                                                                            options.OnlyAligned, pointsToBackproject,
                                                                            intersectingObservations, pipeline);
            //sort observations by decreasing goodness
            intersectingObservations
                .Sort((obs1, obs2) => projectedPixelDistances[obs1.Name].CompareTo(projectedPixelDistances[obs2.Name]));

            //for each source image in order from good to bad
            //try to backproject all valid destination pixels (not atlas gutter pixels)
            //greedily remove points that successfully backprojected
            pipeline.LogInfo("backprojecting observations");
            var indexEntries = new List<ObservationIndex>(pointsToBackproject.Count);
            Image fullTex = options.WriteDebug ? new Image(3, outRes, outRes) : null;
            int i = 0;
            int np = pointsToBackproject.Count;
            foreach (var obs in intersectingObservations)
            {
                if (pointsToBackproject.Count == 0)
                {
                    break;
                }

                pipeline.LogInfo("backprojecting observation {0} {1}/{2}, {3}/{4} remaining pixels",
                                 obs.Name, ++i, intersectingObservations.Count, pointsToBackproject.Count, np);

                //backproject the destination pixels to find which source pixels should be used
                var hits = Backproject.BackprojectObservation(pipeline, frameCache, observationCache, sc,
                                                              (RoverObservation)obs, obsToHull[obs.Name],
                                                              meshFrame, options.UsePriors,
                                                              options.OnlyAligned, masker, pointsToBackproject,
                                                              fullTex);

                pipeline.LogInfo("observation {0} backprojected to {1} pixels", obs.Name, hits.Count);

                if (hits.Count > 0)
                {
                    foreach (var hit in hits)
                    {
                        indexEntries.Add(new ObservationIndex()
                         {
                            Index = obs.Index,
                            SourcePixel = hit.Value,
                            DestPixel = hit.Key
                        });
                    }

                    pointsToBackproject = pointsToBackproject.Where(pt => !hits.ContainsKey(pt.Pixel)).ToList();
                }
            }
            if (options.WriteDebug)
            {
                pipeline.LogInfo("saving backproject texture and textured mesh");
                SaveDebugImage(fullTex, "backprojectTexture");
                SaveDebugMesh(inputMesh, "backprojectMesh", "backprojectTexture" + imageExt);
            }

            pipeline.LogInfo("populating output");
            Image indexImage = new Image(3, outRes, outRes);
            foreach (var entry in indexEntries)
            {
                indexImage.SetBandValues((int)entry.DestPixel.Y, (int)entry.DestPixel.X,
                                         new float[] { entry.Index,
                                                       (float)entry.SourcePixel.Y, (float)entry.SourcePixel.X });
            }

            if (options.WriteDebug)
            {
                pipeline.LogInfo("saving backproject index image");
                PathHelper.EnsureExists(outputPath);
                indexImage.Save<float>(Path.Combine(outputPath, "backprojectIndex.tif"));

                pipeline.LogInfo("generating false color image");
                Image previewImg = new Image(3, outRes, outRes);
                var colorsByIndex = new Dictionary<float, Vector3>();
                Random rand = NumberHelper.MakeRandomGenerator();
                for (int idxPixel = 0; idxPixel < outRes * outRes; idxPixel++)
                {
                    float index = indexImage.GetBandValues(idxPixel)[0];
                    if (index < Observation.MIN_INDEX)
                    {
                        continue;
                    }
                    if (!colorsByIndex.ContainsKey(index))
                    {
                        colorsByIndex.Add(index, new Vector3(rand.NextDouble(), rand.NextDouble(), rand.NextDouble()));
                    }
                    previewImg.SetBandValues(idxPixel, colorsByIndex[index].ToFloatArray());
                }

                pipeline.LogInfo("saving false color image");
                SaveDebugImage(previewImg, "backprojectIndexFalseColor");
            }

            pipeline.LogInfo("saving backproject index to project storage");
            if (sceneMesh != null)
            {
                var indexProd = new TiffDataProduct(indexImage);
                pipeline.SaveDataProduct(project, indexProd);
                sceneMesh.BackprojectIndexGuid = indexProd.Guid;
                sceneMesh.Save(pipeline);
            }
            else
            {
                SceneMesh.Create(pipeline, project, meshFrame, null, indexImage);
            }

            stopwatch.Stop();
            pipeline.LogInfo("elapsed time {0:F3}s", 0.001 * stopwatch.ElapsedMilliseconds);

            return 0;
        }

        private void SaveDebugImage(Image img, string name)
        {
            PathHelper.EnsureExists(outputPath);
            img.Save<byte>(Path.Combine(outputPath, name + imageExt));
        }

        private void SaveDebugMesh(Mesh mesh, string name, string texture = null)
        {
            PathHelper.EnsureExists(outputPath);
            mesh.Save(Path.Combine(outputPath, name + meshExt), texture);
        }
    }
}
