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
    [Verb("local-build-texture", HelpText = "backproject a mesh texture and/or index image")]
    public class LocalBuildTextureOptions : LandformCommandOptions
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

        [Option(HelpText = "Don't generate texture image", Default = false)]
        public bool NoTexture { get; set; }

        [Option(HelpText = "Don't generate index image", Default = false)]
        public bool NoIndex { get; set; }

        // observation filtering related (landform standard)
        [Option(HelpText = "Only use specific cameras, comma separated (FrontHazcamLeft, FrontHazcamRight, RearHazcamLeft, RearHazcamRight, NavcamLeft, NavcamRight, MastcamLeft, MastcamRight, MAHLI)", Default = null)]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Only use observations fromfspecific site drives SSSSSDDDDD, comma separated, wildcard xxxxx", Default = null)]
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

    public class LocalBuildTexture : LandformCommand
    {
        private LocalBuildTextureOptions options;

        private MissionSpecific mission;
        private RoverMasker masker;

        private string outputPath;
        private string imageExt;
        private string meshExt;

        public LocalBuildTexture(LocalBuildTextureOptions options) : base(options)
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

            SiteDrive[] siteDrives = SiteDrive.ParseList(options.OnlyForSiteDrives);

            string[] cameras = StringHelper.ParseList(options.OnlyForCameras);

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

            var imageObservations = observationCache.GetAllObservations().Where(obs => obs.ObservationType == imageObs).ToList();

            pipeline.LogInfo("backprojecting observations");
            var backprojectResults = Backproject.BackprojectObservations(pipeline, frameCache, observationCache,
                                           inputMesh, options.OutputTextureResolution, sc, imageObservations, 
                                           options.UsePriors, options.OnlyAligned, meshFrame, mission, options.BackprojectGoodnessSamplingPct);

            Image indexImage = null;
            if (!options.NoIndex)
            {
                pipeline.LogInfo("creating backproject index");
                indexImage = new Image(3, options.OutputTextureResolution, options.OutputTextureResolution);
                Backproject.FillIndexImage(backprojectResults, indexImage);

                if (options.WriteDebug)
                {
                    pipeline.LogInfo("saving backproject index image");
                    PathHelper.EnsureExists(outputPath);
                    indexImage.Save<float>(Path.Combine(outputPath, "backprojectIndex.tif"));

                    pipeline.LogInfo("generating false color image");
                    Image previewImg = GeneratePreviewImage(options.OutputTextureResolution, indexImage);

                    pipeline.LogInfo("saving backproject index false color image and textured mesh");
                    SaveDebugImage(previewImg, "backprojectIndexFalseColor");
                    SaveDebugMesh(inputMesh, "backprojectMesh", "backprojectIndexFalseColor" + imageExt);
                }
            }

            Image fullTex = null;
            if (!options.NoTexture)
            {
                fullTex = new Image(3, options.OutputTextureResolution, options.OutputTextureResolution);
                bool inpaint = true;
                Backproject.FillOutputTexture(pipeline, backprojectResults, fullTex, inpaint);
                if (options.WriteDebug)
                {
                    pipeline.LogInfo("saving backproject texture and textured mesh");
                    SaveDebugImage(fullTex, "backprojectTexture");
                    SaveDebugMesh(inputMesh, "backprojectMesh", "backprojectTexture" + imageExt);
                }
            }

            pipeline.LogInfo("saving to project storage");
            if (sceneMesh != null)
            {
                if (indexImage != null)
                {
                    var indexProd = new TiffDataProduct(indexImage);
                    pipeline.SaveDataProduct(project, indexProd);
                    sceneMesh.BackprojectIndexGuid = indexProd.Guid;
                }
                if (fullTex != null)
                {
                    var texProd = new PngDataProduct(fullTex);
                    pipeline.SaveDataProduct(project, texProd);
                    sceneMesh.TextureGuid = texProd.Guid;
                }
                sceneMesh.Save(pipeline);
            }
            else
            {
                SceneMesh.Create(pipeline, project, meshFrame, texture: fullTex, backprojectIndex: indexImage);
            }

            stopwatch.Stop();
            pipeline.LogInfo("elapsed time {0:F3}s", 0.001 * stopwatch.ElapsedMilliseconds);

            return 0;
        }

        private static Image GeneratePreviewImage(int outRes, Image indexImage)
        {
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

            return previewImg;
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
