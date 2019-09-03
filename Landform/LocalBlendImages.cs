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
using ColorMine.ColorSpaces;
using OPS.Util;
using OPS.MathExtensions;
using OPS.Imaging;
using OPS.RayTrace;
using OPS.Geometry;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TilingServer;

namespace OPS.Landform
{
    [Verb("local-blend-imges", HelpText = "blend observation images")]
    public class LocalBlendImagesOptions : LandformCommandOptions
    {
        [Value(1, Required = false, Default = null, HelpText = "Scene mesh, search project storage if omitted")]
        public string InputMesh { get; set; }

        [Option(HelpText = "Mesh coordinate frame: a numeric sitedrive SSSSSDDDDD or root", Default = "root")]
        public string MeshFrame { get; set; }

        [Option(HelpText = "Resolution of backproject texture image, preferably power of two", Default = 4096)]
        public int TextureResolution { get; set; }

        [Option(HelpText = "Percentage of pixels to test before picking a texture during backprojection", Default = 0.1)]
        public double BackprojectGoodnessSamplingPct { get; set; }

        [Option(HelpText = "Shrinkwrap mesh grid resolution", Default = 1024)]
        public int GridResolution { get; set; }

        [Option(HelpText = "Shrinkwrap mesh projection axis (X, Y, Z)", Default = VertexProjection.ProjectionAxis.Z)]
        public VertexProjection.ProjectionAxis ProjectionAxis { get; set; }

        [Option(HelpText = "Shrinkwrap mode (Project, NearestPoint)", Default = Shrinkwrap.ShrinkwrapMode.Project)]
        public Shrinkwrap.ShrinkwrapMode ShrinkwrapMode { get; set; }

        [Option(HelpText = "Shrinkwrap miss behaviour (None, Delaunay, Inpaint)", Default = Shrinkwrap.ProjectionMissResponse.Delaunay)]
        public Shrinkwrap.ProjectionMissResponse ShrinkwrapMiss { get; set; }

        [Option(Required = false, HelpText = "acceptable error in solving the linear system", Default = LimberDMG.DEF_RESIDUAL_EPSILON)]
        public double ResidualEpsilon { get; set; }

        [Option(Required = false, HelpText = "number of iterations of relaxation to perform between multigrid iterations", Default = LimberDMG.DEF_NUM_RELAXATION_STEPS)]
        public int NumRelaxationSteps { get; set; }

        [Option(Required = false, HelpText = "number of multigrid iterations to perform", Default = LimberDMG.DEF_NUM_MULTIGRID_ITERATIONS)]
        public int NumMultigridIterations { get; set; }

        [Option(Required = false, HelpText = "higher values will cause sharper transitions between images but better conform to the inputs", Default = LimberDMG.DEF_LAMBDA)]
        public double Lambda { get; set; }

        [Option(HelpText = "Redo all", Default = false)]
        public bool Redo { get; set; }

        [Option(HelpText = "Redo shrinkwrap mesh", Default = false)]
        public bool RedoShrinkwrapMesh { get; set; }

        [Option(HelpText = "Redo shrinkwrap texture", Default = false)]
        public bool RedoShrinkwrapTexture { get; set; }

        [Option(HelpText = "Redo blended shrinkwrap texture", Default = false)]
        public bool RedoShrinkwrapBlendedTexture { get; set; }

        [Option(HelpText = "Redo blended observation textures", Default = false)]
        public bool RedoBlendedObservationTextures { get; set; }

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

        [Option(HelpText = "Output debug products", Default = false)]
        public bool WriteDebug { get; set; }

        [Option(HelpText = "Debug output directory, or omit to save to project storage", Default = null)]
        public string DebugOutputFolder { get; set; }

        [Option(HelpText = "Debug mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public string MeshFormat { get; set; }

        [Option(HelpText = "Debug image format, e.g. png, jpg, help for list", Default = "png")]
        public string ImageFormat { get; set; }
    }

    public class LocalBlendImages : LandformCommand
    {
        private LocalBlendImagesOptions options;

        private Project project;
        private MissionSpecific mission;
        private RoverMasker masker;

        private string meshFrame;

        private int resolution;

        private SiteDrive[] siteDrives;

        private string outputPath;
        private string imageExt;
        private string meshExt;

        private FrameCache frameCache;
        private ObservationCache observationCache;
        private Dictionary<int, Observation> indexedObservations = new Dictionary<int, Observation>();

        private SceneMesh shrinkwrapSceneMesh;

        private Mesh shrinkwrapMesh;

        private Image shrinkwrapTexture;
        private Image shrinkwrapBackprojectIndex;
        private Image shrinkwrapBlendedTexture;

        public LocalBlendImages(LocalBlendImagesOptions options) : base(options)
        {
            this.options = options;

            if (options.Redo) 
            {
                options.RedoShrinkwrapMesh = true;
                options.RedoShrinkwrapTexture = true;
                options.RedoShrinkwrapBlendedTexture = true;
                options.RedoBlendedObservationTextures = true;
            }
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

            project = Project.Find(pipeline, options.ProjectName);
            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }
            mission = MissionSpecific.GetInstance(project.Mission);
            masker = mission.GetMasker();

            meshFrame = options.MeshFrame;
            FrameTransform.ParseFrameName(ref meshFrame, out bool specificSiteDrive);
            if (!specificSiteDrive && meshFrame != "root")
            {
                pipeline.LogError("unsupported mesh frame: " + meshFrame);
                return 1;
            }

            resolution = options.TextureResolution;
            if ((resolution & (resolution - 1)) != 0)
            {
                pipeline.LogWarn("resolution {0} not a power of two", resolution);
            }

            var adjustedSources = FrameTransform.ParseSources(options.AdjustedTransformSources);
            var priorSources = FrameTransform.ParseSources(options.PriorTransformSources);

            string dir = "texturing/BlendProducts";
            dir = FrameTransform.AppendSourcesPath(dir, adjustedSources, priorSources, options.UsePriors);
            outputPath = pipeline.GetLocalDebugFolder(options.DebugOutputFolder, dir, project.Name);
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

            siteDrives = SiteDrive.ParseList(options.OnlyForSiteDrives);

            string[] cameras = StringHelper.ParseList(options.OnlyForCameras);

            frameCache = new FrameCache(pipeline, project.Name);
            frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, options.UsePriors);

            string imageObs = ObservationType.Image.ToString();
            string maskObs = ObservationType.RoverMask.ToString();
            observationCache = new ObservationCache(pipeline, project.Name);
            observationCache.
                Preload(obs => obs.UseForReconstruction &&
                        (obs.ObservationType == imageObs || obs.ObservationType == maskObs) &&
                        (siteDrives.Length == 0 || siteDrives.Any(sd => sd == ((RoverObservation)obs).SiteDrive)) &&
                        (cameras.Length == 0 || cameras.Any(cam => cam == ((RoverObservation)obs).Sensor)));

            foreach (var obs in observationCache.GetAllObservations())
            {
                if (obs.ObservationType == imageObs)
                {
                    indexedObservations[obs.Index] = obs;
                }
            }

            string what = "";
            try
            {
                what = "shrinkwrap mesh";
                LoadOrGenerateShrinkwrapMesh();

                what = "shrinkwrap texture";
                LoadOrGenerateShrinkwrapTexture();

                what = "blended texture";
                LoadOrGenerateBlendedTexture();

                what = "blended observation images";
                GenerateBlendedObservationImages();
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

        private void LoadOrGenerateShrinkwrapMesh()
        {
            shrinkwrapSceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame, siteDrives, MeshVariant.Shrinkwrap);
            if (shrinkwrapSceneMesh == null)
            {
                shrinkwrapSceneMesh = SceneMesh.Create(pipeline, project, meshFrame, siteDrives,MeshVariant.Shrinkwrap);
            }

            if (shrinkwrapSceneMesh.MeshGuid != Guid.Empty && !options.RedoShrinkwrapMesh)
            {
                pipeline.LogInfo("loading existing shrinkwrap mesh from database");
                shrinkwrapMesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, shrinkwrapSceneMesh.MeshGuid).Mesh;
                return;
            }

            Mesh inputMesh = null;
            if (!string.IsNullOrEmpty(options.InputMesh))
            {
                pipeline.LogInfo("loading input mesh from {0}", options.InputMesh);
                inputMesh = Mesh.Load(pipeline.GetFileCached(options.InputMesh, "meshes"));
            }
            else
            {
                SceneMesh sm = SceneMesh.Find(pipeline, project.Name, meshFrame, siteDrives, MeshVariant.Default);
                if (sm != null && sm.MeshGuid != Guid.Empty)
                {
                    pipeline.LogInfo("loading scene mesh from database");
                    inputMesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, sm.MeshGuid).Mesh;
                }
                else
                {
                    throw new Exception("no input mesh specified and no scene mesh in database");
                }
            }

            if (inputMesh == null || inputMesh.Faces.Count == 0)
            {
                throw new Exception("failed to load input mesh or input mesh empty");
            }

            pipeline.LogInfo("generating shrinkwrap mesh in frame {0}, grid resolution {1}, " +
                             "projection axis {2}, mode {3}, miss behavior {4}",
                             meshFrame, options.GridResolution, options.ProjectionAxis, options.ShrinkwrapMode,
                             options.ShrinkwrapMiss);

            Mesh gridMesh = Shrinkwrap.BuildGrid(inputMesh, options.GridResolution, options.GridResolution,
                                                 options.ProjectionAxis);

            shrinkwrapMesh = Shrinkwrap.Wrap(inputMesh, gridMesh, options.ShrinkwrapMode, options.ProjectionAxis,
                                             options.ShrinkwrapMiss);

            if (shrinkwrapMesh.Faces.Count == 0)
            {
                throw new Exception("shrinkwrap mesh empty");
            }

            if (options.WriteDebug)
            {
                pipeline.LogInfo("saving shrinkwrap mesh");
                SaveDebugMesh(shrinkwrapMesh, "shrinkwrapMesh");
            }

            pipeline.LogInfo("created shrinkwrap mesh with {0} faces", shrinkwrapMesh.Faces.Count);

            var meshProd = new PlyGZDataProduct(shrinkwrapMesh);
            pipeline.SaveDataProduct(project, meshProd);
            shrinkwrapSceneMesh.MeshGuid = meshProd.Guid;
            shrinkwrapSceneMesh.Save(pipeline);
        }

        private void LoadOrGenerateShrinkwrapTexture()
        {
            if (shrinkwrapSceneMesh.TextureGuid != Guid.Empty &&
                shrinkwrapSceneMesh.BackprojectIndexGuid != Guid.Empty &&
                !options.RedoShrinkwrapBlendedTexture)
            {
                pipeline.LogInfo("loading shrinkwrap texture from database");
                var texGuid = shrinkwrapSceneMesh.TextureGuid;
                var indexGuid = shrinkwrapSceneMesh.BackprojectIndexGuid;
                shrinkwrapTexture = pipeline.GetDataProduct<PngDataProduct>(project, texGuid).Image;
                shrinkwrapBackprojectIndex = pipeline.GetDataProduct<TiffDataProduct>(project, indexGuid).Image;
                return;
            }

            pipeline.LogInfo("building occlusion data structures for shrinkwrap texture backproject");
            var sc = new SceneCaster();
            sc.AddMesh(shrinkwrapMesh, null, Matrix.Identity); //NOTE: can't change mesh after adding to collider
            sc.Build();

            string imageObs = ObservationType.Image.ToString();
            var imageObservations =
                observationCache.GetAllObservations().Where(obs => obs.ObservationType == imageObs).ToList();

            var backprojectResults =
                Backproject.BackprojectObservations(pipeline, frameCache, observationCache, shrinkwrapMesh, resolution,
                                                    sc, imageObservations, options.UsePriors, options.OnlyAligned,
                                                    meshFrame, mission, options.BackprojectGoodnessSamplingPct);

            pipeline.LogInfo("creating backproject index");
            shrinkwrapBackprojectIndex = new Image(3, resolution, resolution);
            Backproject.FillIndexImage(backprojectResults, shrinkwrapBackprojectIndex);
            
            if (options.WriteDebug)
            {
                pipeline.LogInfo("saving backproject index image");
                PathHelper.EnsureExists(outputPath);
                shrinkwrapBackprojectIndex.Save<float>(Path.Combine(outputPath, "shrinkwrapBackprojectIndex.tif"));
                
                pipeline.LogInfo("generating false color image");
                Image previewImg = Backproject.GenerateIndexPreviewImage(shrinkwrapBackprojectIndex);
                
                pipeline.LogInfo("saving backproject index false color image and textured mesh");
                SaveDebugImage(previewImg, "shrinkwrapBackprojectIndexFalseColor");
                SaveDebugMesh(shrinkwrapMesh, "shrinkwrapBackprojectMesh",
                              "shrinkwrapBackprojectIndexFalseColor" + imageExt);
            }

            shrinkwrapTexture = new Image(3, resolution, resolution);
            Backproject.FillOutputTexture(pipeline, backprojectResults, shrinkwrapTexture);
            if (options.WriteDebug)
            {
                pipeline.LogInfo("saving shrinkwrap backproject texture and textured mesh");
                SaveDebugImage(shrinkwrapTexture, "shrinkwrapBackprojectTexture");
                SaveDebugMesh(shrinkwrapMesh, "shrinkwrapBackprojectMesh", "shrinkwrapBackprojectTexture" + imageExt);
            }

            pipeline.LogInfo("created {0}x{0} shrinkwrap texture ", resolution);

            var texProd = new PngDataProduct(shrinkwrapTexture);
            pipeline.SaveDataProduct(project, texProd);
            shrinkwrapSceneMesh.TextureGuid = texProd.Guid;

            var indexProd = new TiffDataProduct(shrinkwrapBackprojectIndex);
            pipeline.SaveDataProduct(project, indexProd);
            shrinkwrapSceneMesh.BackprojectIndexGuid = indexProd.Guid;

            shrinkwrapSceneMesh.Save(pipeline);
        }

        private void LoadOrGenerateBlendedTexture()
        {
            if (shrinkwrapSceneMesh.BlendedTextureGuid != Guid.Empty && !options.RedoShrinkwrapBlendedTexture)
            {
                pipeline.LogInfo("loading shrinkwrap blended texture from database");
                var texGuid = shrinkwrapSceneMesh.BlendedTextureGuid;
                shrinkwrapBlendedTexture = pipeline.GetDataProduct<PngDataProduct>(project, texGuid).Image;
                return;
            }

            if (shrinkwrapTexture.Width != resolution || shrinkwrapTexture.Height != resolution ||
                shrinkwrapBackprojectIndex.Width != resolution || shrinkwrapBackprojectIndex.Height != resolution)
            {
                throw new Exception(string.Format("existing backproject texture or index not {0}x{0}, " +
                                                  "re-run with --redoshrinkwraptexture", resolution, resolution));
            }
            
            pipeline.LogInfo("stitching {0}x{0} image with LimberDMG, residual epsilon {1}, {2} relaxation steps, " +
                             "{3} multigrid iterations, lambda {4}",
                             resolution, options.ResidualEpsilon, options.NumRelaxationSteps,
                             options.NumMultigridIterations, options.Lambda);

            Image index = new Image(1, resolution, resolution);
            Image flags = new Image(3, resolution, resolution);
            for (int r = 0; r < resolution; r++)
            {
                for (int c = 0; c < resolution; c++)
                {
                    int obsIndex = (int)shrinkwrapBackprojectIndex[0, r, c];

                    index[0, r, c] = obsIndex;

                    var obs = indexedObservations[obsIndex];

                    bool hasGray = true;
                    bool hasColor = obs.Bands == 3;
                    bool orbital = false; //TODO

                    byte lumaFlag = (byte)(hasGray ? LimberDMG.Flags.NONE : LimberDMG.Flags.NO_DATA);
                    byte chromaFlag = (byte)(hasColor ? LimberDMG.Flags.NONE : LimberDMG.Flags.NO_DATA);

                    if (orbital)
                    {
                        lumaFlag |= (byte)LimberDMG.Flags.GRADIENT_ONLY;
                        if (hasColor)
                        {
                            chromaFlag |= (byte)LimberDMG.Flags.GRADIENT_ONLY;
                        }
                    }

                    flags[0, r, c] = (float)lumaFlag;
                    flags[1, r, c] = flags[2, r, c] = (float)chromaFlag;
                }
            }

            var dmg = new LimberDMG(options.ResidualEpsilon, options.NumRelaxationSteps, options.NumMultigridIterations,
                                    options.Lambda, LimberDMG.EdgeBehavior.Clamp, LimberDMG.ColorConversion.RGBToLAB,
                                    msg => pipeline.LogVerbose(msg));
            shrinkwrapBlendedTexture = dmg.StitchImage(shrinkwrapTexture, index, flags);

            pipeline.LogInfo("created {0}x{0} shrinkwrap blended texture ", resolution);

            if (options.WriteDebug)
            {
                pipeline.LogInfo("saving shrinkwrap blended texture and textured mesh");
                SaveDebugImage(shrinkwrapBlendedTexture, "shrinkwrapBlendedTexture");
                SaveDebugMesh(shrinkwrapMesh, "shrinkwrapMeshBlendedTextured", "shrinkwrapBlendedTexture" + imageExt);
            }

            var texProd = new PngDataProduct(shrinkwrapBlendedTexture);
            pipeline.SaveDataProduct(project, texProd);
            shrinkwrapSceneMesh.BlendedTextureGuid = texProd.Guid;

            shrinkwrapSceneMesh.Save(pipeline);
        }

        private void GenerateBlendedObservationImages()
        {
            pipeline.LogInfo("collecting backprojected pixels for each observation");

            //obs index => (obsPixelCol, obsPixelRow) => (sumBlendedR, sumBlendedG, sumBlendedB, num)
            var winners = new Dictionary<int, Dictionary<Vector2, Vector4>>();
            
            for (int r = 0; r < resolution; r++)
            {
                for (int c = 0; c < resolution; c++)
                {
                    int obsIndex = (int)shrinkwrapBackprojectIndex[0, r, c];
                    if (!winners.ContainsKey(obsIndex))
                    {
                        winners[obsIndex] = new Dictionary<Vector2, Vector4>();
                    }
                    var winnersForObs = winners[obsIndex];

                    int obsPixelRow = (int)shrinkwrapBackprojectIndex[1, r, c];
                    int obsPixelCol = (int)shrinkwrapBackprojectIndex[2, r, c];
                    Vector2 obsPixel = new Vector2(obsPixelCol, obsPixelRow);

                    float blendedR = shrinkwrapBlendedTexture[0, r, c];
                    float blendedG = shrinkwrapBlendedTexture[1, r, c];
                    float blendedB = shrinkwrapBlendedTexture[2, r, c];

                    if (!winnersForObs.ContainsKey(obsPixel))
                    {
                        winnersForObs[obsPixel] = new Vector4(blendedR, blendedG, blendedB, 1);
                    }
                    else
                    {
                        winnersForObs[obsPixel] += new Vector4(blendedR, blendedG, blendedB, 1);
                    }
                }
            }

            pipeline.LogInfo("creating blended observation images");

            double maxLuminance = (new Rgb() { R = 255, G = 255, B = 255 }).To<Lab>().L;

            int no = indexedObservations.Count;
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(indexedObservations, entry => {

                    int obsIndex = entry.Key;
                    Observation obs = entry.Value;

                    if (!options.RedoBlendedObservationTextures && obs.BlendedGuid != Guid.Empty)
                    {
                        Interlocked.Increment(ref nc);
                        return;
                    }

                    Interlocked.Increment(ref np);

                    Image blendedImage = null;
                    if (winners.ContainsKey(obsIndex))
                    {
                        pipeline.LogInfo("creating blended image for observation {0}, processing {1} in parallel, " +
                                         "completed {2}/{3}", obs.Name, np, nc, no);

                        if (obs.Bands != 3 && obs.Bands != 1)
                        {
                            pipeline.LogWarn("blending observation image {0} with {1} bands not supported",
                                             obs.Name, obs.Bands);
                            Interlocked.Increment(ref nc);
                            return;
                        }

                        Image img = pipeline.LoadImage(obs.Url);

                        var diffImage = new Image(img.Bands, img.Width, img.Height);
                        diffImage.CreateMask(true); //all pixels initially masked

                        foreach (var winner in winners[obsIndex])
                        {
                            Vector2 obsPixel = winner.Key;
                            Vector4 blendedSum = winner.Value;
                            Vector3 blendedRGB = new Vector3(blendedSum.X, blendedSum.Y, blendedSum.Z) / blendedSum.W;

                            int or = (int)obsPixel.Y;
                            int oc = (int)obsPixel.X;

                            float[] diff = null;
                            if (obs.Bands == 3)
                            {
                                Vector3 d = blendedRGB - new Vector3(img[0, or, oc], img[1, or, oc], img[2, or, oc]);
                                diff = new float[] { (float)d.X, (float)d.Y, (float)d.Z };
                            }
                            else
                            {
                                float br = (float)blendedRGB.X;
                                float bg = (float)blendedRGB.Y;
                                float bb = (float)blendedRGB.Z;
                                double luminance = (new Rgb() { R = 255 * br, G = 255 * bg, B = 255 * bb }).To<Lab>().L;
                                diff = new float[] { (float)(luminance / maxLuminance) - img[0, or, oc] };
                            }
                                 
                            diffImage.SetBandValues(or, oc, diff);
                            diffImage.SetMaskValue(or, oc, false);
                        }

                        if (winners[obsIndex].Count >= 3)
                        {
                            Rasterizer.BarycentricInterpolate(diffImage);
                        }

                        diffImage.Inpaint();

                        void saveWithMarkedWinners(string suffix)
                        {
                            Image dbgImage = new Image(diffImage);
                            if (dbgImage.Bands == 1)
                            {
                                dbgImage.AddBand();
                                dbgImage.AddBand();
                            }
                            
                            float[] winnerColor = new float[] { 0, 1, 0 };
                            foreach (var pixel in winners[obsIndex].Keys)
                            {
                                dbgImage.SetBandValues((int)pixel.Y, (int)pixel.X, winnerColor);
                            }
                            
                            SaveDebugImage(dbgImage, obs.Name + suffix);
                        };

                        if (options.WriteDebug)
                        {
                            saveWithMarkedWinners("_diff");
                        }

                        for (int b = 0; b < img.Bands; b++)
                        {
                            for (int r = 0; r < img.Height; r++)
                            {
                                for (int c = 0; c < img.Width; c++)
                                {
                                    diffImage[b, r, c] = MathE.Clamp01(diffImage[b, r, c] + img[b, r, c]);
                                }
                            }
                        }

                        if (options.WriteDebug)
                        {
                            saveWithMarkedWinners("_blended");
                        }

                        blendedImage = diffImage;
                    }
                    else
                    {
                        pipeline.LogInfo("no mesh points backprojected to observation {0}", obs.Name);
                    }

                    obs.BlendedGuid = Guid.Empty;
                    if (blendedImage != null)
                    {
                        var imgProd = new PngDataProduct(blendedImage);
                        pipeline.SaveDataProduct(project, imgProd);
                        obs.BlendedGuid = imgProd.Guid;
                    }
                    obs.Save(pipeline);

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });
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
