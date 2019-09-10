using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
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
    public class TextureCommandOptionsBase : LandformCommandOptions
    {
        [Value(1, Required = false, Default = null, HelpText = "Scene mesh, search project storage if omitted")]
        public string InputMesh { get; set; }

        [Option(HelpText = "Occlusion mesh in same frame as input mesh, defaults to input mesh", Default = null)]
        public string OcclusionMesh { get; set; }

        [Option(HelpText = "Mesh coordinate frame: a numeric sitedrive SSSSSDDDDD or root", Default = "root")]
        public string MeshFrame { get; set; }

        [Option(HelpText = "Backproject texture resolution, should be power of two", Default = 4096)]
        public int TextureResolution { get; set; }

        [Option(HelpText = "Percentage of pixels to test before picking a texture during backprojection", Default = 0.1)]
        public double BackprojectGoodnessSamplingPct { get; set; }

        [Option(Required = false, HelpText = "Blur radius", Default = 7)]
        public int BlurRadius { get; set; }

        [Option(HelpText = "Redo all", Default = false)]
        public bool Redo { get; set; }

        [Option(HelpText = "Redo blurred observation textures", Default = false)]
        public bool RedoBlurredObservationTextures { get; set; }

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

        [Option(HelpText = "Output debug products", Default = false)]
        public bool WriteDebug { get; set; }

        [Option(HelpText = "Debug output directory, or omit to save to project storage", Default = null)]
        public string DebugOutputFolder { get; set; }

        [Option(HelpText = "Debug mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public string MeshFormat { get; set; }

        [Option(HelpText = "Debug image format, e.g. png, jpg, help for list", Default = "png")]
        public string ImageFormat { get; set; }
    }

    [Verb("local-build-texture", HelpText = "backproject a mesh texture and/or index image")]
    public class LocalBuildTextureOptions : TextureCommandOptionsBase
    {
        [Option(HelpText = "Don't generate texture image", Default = false)]
        public bool NoTexture { get; set; }

        [Option(HelpText = "Don't generate index image", Default = false)]
        public bool NoIndex { get; set; }

        [Option(HelpText = "Texture variant (Original, Blurred, Blended)", Default = Backproject.TextureVariant.Original)]
        public Backproject.TextureVariant TextureVariant { get; set; }
    }

    public class TextureCommandBase : LandformCommand
    {
        private TextureCommandOptionsBase baseOptions;

        protected Project project;
        protected MissionSpecific mission;
        protected RoverMasker masker;

        protected string meshFrame;
        protected int resolution;
        protected SiteDrive[] siteDrives;

        protected string outputPath;
        protected string imageExt;
        protected string meshExt;

        protected FrameCache frameCache;
        protected ObservationCache observationCache;
        protected List<Observation> imageObservations;

        protected SceneMesh sceneMesh;
        protected Mesh mesh;
        protected SceneCaster sceneCaster;
        protected Dictionary<Pixel, Backproject.ObsPixel> backprojectResults;

        protected TextureCommandBase(TextureCommandOptionsBase options) : base(options)
        {
            this.baseOptions = options;
            options.RedoBlurredObservationTextures |= options.Redo;
        }

        protected virtual bool ParseArgumentsAndLoadCaches(string debugDir)
        {
            if (baseOptions.UsePriors && baseOptions.OnlyAligned)
            {
                throw new Exception("cannot specify both --usepriors and --onlyaligned");
            }

            project = Project.Find(pipeline, baseOptions.ProjectName);
            if (project == null)
            {
                throw new Exception("project not found: " + baseOptions.ProjectName);
            }
            mission = MissionSpecific.GetInstance(project.Mission);
            masker = mission.GetMasker();

            meshFrame = baseOptions.MeshFrame;
            FrameTransform.ParseFrameName(ref meshFrame, out bool specificSiteDrive);
            if (!specificSiteDrive && meshFrame != "root")
            {
                throw new Exception("unsupported mesh frame: " + meshFrame);
            }

            resolution = baseOptions.TextureResolution;
            if ((resolution & (resolution - 1)) != 0)
            {
                pipeline.LogWarn("resolution {0} not a power of two", resolution);
            }

            var adjustedSources = FrameTransform.ParseSources(baseOptions.AdjustedTransformSources);
            var priorSources = FrameTransform.ParseSources(baseOptions.PriorTransformSources);

            debugDir = FrameTransform.AppendSourcesPath(debugDir, adjustedSources, priorSources, baseOptions.UsePriors);
            outputPath = pipeline.GetLocalDebugFolder(baseOptions.DebugOutputFolder, debugDir, baseOptions.ProjectName);

            if (baseOptions.WriteDebug)
            {
                meshExt = MeshSerializers.Instance.CheckFormat(baseOptions.MeshFormat, pipeline);
                if (meshExt == null)
                {
                    return false; //help
                }
                pipeline.LogInfo("writing {0} debug meshes to {1}", meshExt, outputPath);
            
                imageExt = ImageSerializers.Instance.CheckFormat(baseOptions.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return false; //help
                }
                pipeline.LogInfo("writing {0} debug images to {1}", imageExt, outputPath);
            }

            siteDrives = SiteDrive.ParseList(baseOptions.OnlyForSiteDrives);

            string[] cameras = StringHelper.ParseList(baseOptions.OnlyForCameras);
            
            frameCache = new FrameCache(pipeline, baseOptions.ProjectName);
            frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, baseOptions.UsePriors);
            
            string imageObs = ObservationType.Image.ToString();
            string maskObs = ObservationType.RoverMask.ToString();
            observationCache = new ObservationCache(pipeline, baseOptions.ProjectName);
            observationCache.
                Preload(obs => obs.UseForReconstruction &&
                        (obs.ObservationType == imageObs || obs.ObservationType == maskObs) &&
                        (siteDrives.Length == 0 || siteDrives.Any(sd => sd == ((RoverObservation)obs).SiteDrive)) &&
                        (cameras.Length == 0 || cameras.Any(cam => cam == ((RoverObservation)obs).Sensor)));

            imageObservations =
                observationCache.GetAllObservations().Where(obs => obs.ObservationType == imageObs).ToList();

            return true;
        }

        protected void GenerateBlurredObservationImages()
        {
            pipeline.LogInfo("creating blurred observation images");

            int no = imageObservations.Count;
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(imageObservations, obs => {

                    if (!baseOptions.RedoBlurredObservationTextures && obs.BlurredGuid != Guid.Empty)
                    {
                        Interlocked.Increment(ref nc);
                        return;
                    }

                    Interlocked.Increment(ref np);

                    pipeline.LogInfo("creating blurred image for observation {0}, processing {1} in parallel, " +
                                     "completed {2}/{3}", obs.Name, np, nc, no);

                    Image img = pipeline.LoadImage(obs.Url);

                    //notes from TerrainTools PDSImageRoutines.cs
                    //"Used to do a guass blur 4 with photoshop"
                    //the current code is: img.SmoothBlur(13, 13)
                    Image blurredImage = img.GaussianBoxBlur(baseOptions.BlurRadius);

                    if (baseOptions.WriteDebug)
                    {
                        SaveDebugImage(blurredImage, obs.Name + "_blurred");
                    }

                    var imgProd = new PngDataProduct();
                    pipeline.SaveDataProduct(project, imgProd);
                    obs.BlurredGuid = imgProd.Guid;
                    obs.Save(pipeline);

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });
        }

        protected void BuildSceneCaster()
        {
            Mesh occlusionMesh = null;
            if (!string.IsNullOrEmpty(baseOptions.OcclusionMesh))
            {
                pipeline.LogInfo("loading occlusion mesh {0}", baseOptions.OcclusionMesh);
                occlusionMesh = Mesh.Load(pipeline.GetFileCached(baseOptions.OcclusionMesh, "meshes"));
                if (occlusionMesh == null)
                {
                    throw new Exception("failed to load occlusion mesh");
                }
                if (occlusionMesh.Faces.Count == 0)
                {
                    throw new Exception("occlusion mesh empty");
                }
            }
            else
            {
                occlusionMesh = mesh;
            }

            pipeline.LogInfo("building occlusion data structures");
            sceneCaster = new SceneCaster();
            sceneCaster.AddMesh(occlusionMesh, null, Matrix.Identity); //NOTE: can't change mesh after adding to collider
            sceneCaster.Build();
        }

        protected void BackprojectObservations()
        {
            pipeline.LogInfo("backprojecting {0} observations", imageObservations.Count);
            backprojectResults =
                Backproject.BackprojectObservations(pipeline, frameCache, observationCache, mesh, resolution,
                                                    sceneCaster, imageObservations, baseOptions.UsePriors,
                                                    baseOptions.OnlyAligned, meshFrame, mission,
                                                    baseOptions.BackprojectGoodnessSamplingPct);
        }

        protected Image GenerateBackprojectIndex()
        {
            pipeline.LogInfo("creating backproject index");
            Image index = new Image(3, resolution, resolution);
            Backproject.FillIndexImage(backprojectResults, index);
            
            pipeline.LogInfo("saving backproject index");
            var indexProd = new TiffDataProduct(index);
            pipeline.SaveDataProduct(project, indexProd);
            sceneMesh.BackprojectIndexGuid = indexProd.Guid;
            sceneMesh.Save(pipeline);
            
            if (baseOptions.WriteDebug)
            {
                pipeline.LogInfo("saving backproject index false color image and textured mesh");
                Image previewImg = Backproject.GenerateIndexPreviewImage(index);
                string name = sceneMesh.Name + "_backprojectIndexFalseColor";
                SaveDebugImage(previewImg, name);
                SaveDebugMesh(mesh, name, name + imageExt);
            }

            return index;
        }

        protected Image GenerateBackprojectTexture(Backproject.TextureVariant textureVariant)
        {
            pipeline.LogInfo("creating backproject texture");
            Image texture = new Image(3, resolution, resolution);
            Backproject.FillOutputTexture(pipeline, backprojectResults, texture, textureVariant);
            
            pipeline.LogInfo("saving backproject texture");
            var texProd = new PngDataProduct(texture);
            pipeline.SaveDataProduct(project, texProd);
            sceneMesh.Save(pipeline);
            
            switch (textureVariant)
            {
                case Backproject.TextureVariant.Original: sceneMesh.TextureGuid = texProd.Guid; break;
                case Backproject.TextureVariant.Blurred: sceneMesh.BlurredTextureGuid = texProd.Guid; break;
                case Backproject.TextureVariant.Blended: sceneMesh.BlendedTextureGuid = texProd.Guid; break;
                default: throw new Exception("unknown texture variant " + textureVariant);
            }
            
            if (baseOptions.WriteDebug)
            {
                pipeline.LogInfo("saving backproject texture and textured mesh");
                string name = sceneMesh.Name + "_backprojectTexture_" + textureVariant.ToString();
                SaveDebugImage(texture, name);
                SaveDebugMesh(mesh, name, name + imageExt);
            }

            return texture;
        }

        protected void SaveDebugImage(Image img, string name)
        {
            PathHelper.EnsureExists(outputPath);
            img.Save<byte>(Path.Combine(outputPath, name + imageExt));
        }

        protected void SaveDebugMesh(Mesh mesh, string name, string texture = null)
        {
            PathHelper.EnsureExists(outputPath);
            mesh.Save(Path.Combine(outputPath, name + meshExt), texture);
        }
    }

    public class LocalBuildTexture : TextureCommandBase
    {
        private LocalBuildTextureOptions options;

        public LocalBuildTexture(LocalBuildTextureOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            try
            {
                if (!ParseArgumentsAndLoadCaches("meshing/TextureProducts"))
                {
                    return 0; //help
                }
            }
            catch (Exception ex)
            {
                pipeline.LogError(ex.Message);
                return 1;
            }

            string what = "";
            try
            {
                what = "observation textures";
                EnsureOrGenerateObservationTextures();

                what = "input mesh";
                LoadInputMesh();

                what = "occlusion datastructures";
                BuildSceneCaster();
                
                what = "backprojection";
                BackprojectObservations();

                if (!options.NoIndex)
                {
                    what = "backproject index";
                    GenerateBackprojectIndex();
                }

                if (!options.NoTexture)
                {
                    what = "backproject texture";
                    GenerateBackprojectTexture(options.TextureVariant);
                }
            }
            catch (Exception ex)
            {
                pipeline.LogError("failed to load or generate {0}: {1}", what, ex.Message);
                return 1;
            }

            stopwatch.Stop();
            pipeline.LogInfo("elapsed time {0:F3}s", 0.001 * stopwatch.ElapsedMilliseconds);

            return 0;
        }

        private void EnsureOrGenerateObservationTextures()
        {
            switch (options.TextureVariant)
            {
                case Backproject.TextureVariant.Original: break;
                case Backproject.TextureVariant.Blurred: GenerateBlurredObservationImages(); break;
                case Backproject.TextureVariant.Blended: EnsureBlendedObservationImages(); break;
                default: throw new Exception("unknown texture variant " + options.TextureVariant);
            }
        }

        private void EnsureBlendedObservationImages()
        {
            foreach (var obs in imageObservations)
            {
                if (obs.BlendedGuid == Guid.Empty)
                {
                    throw new Exception(string.Format("no blended texture for observation {0}, run local-blend-images"));
                }
            }
        }

        protected override bool ParseArgumentsAndLoadCaches(string debugDir)
        {
            if (options.NoIndex && options.NoTexture)
            {
                throw new Exception("cannot specify both --noindex and --notexture");
            }

            return base.ParseArgumentsAndLoadCaches(debugDir);
        }

        private void LoadInputMesh()
        {
            sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame);

            if (!string.IsNullOrEmpty(options.InputMesh))
            {
                pipeline.LogInfo("loading input mesh from {0}", options.InputMesh);
                mesh = Mesh.Load(pipeline.GetFileCached(options.InputMesh, "meshes"));
            }
            else if (sceneMesh != null)
            {
                if (sceneMesh.MeshGuid != Guid.Empty)
                {
                    pipeline.LogInfo("loading scene mesh in frame {0} from database", meshFrame);
                    mesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, sceneMesh.MeshGuid).Mesh;
                }
                else
                {
                    throw new Exception("scene mesh in database but without mesh");
                }
            }
            else
            {
                throw new Exception("no input mesh specified and no scene mesh in database");
            }

            if (mesh == null)
            {
                throw new Exception("failed to load input mesh");
            }

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("input mesh empty");
            }

            if (!mesh.HasUVs)
            {
                throw new Exception("input mesh needs UVs");
            }

            if (sceneMesh == null)
            {
                sceneMesh = SceneMesh.Create(pipeline, project, meshFrame, siteDrives, MeshVariant.Default, mesh);
            }
        }
    }
}
