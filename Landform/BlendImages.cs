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
    public enum BlendStrategy { None, Auto, Barycentric, Inpaint };

    [Verb("blend-images", HelpText = "blend observation images")]
    public class BlendImagesOptions : TextureCommandOptions
    {
        [Option(HelpText = "Don't use existing leaves to build backproject index", Default = false)]
        public bool NoUseExistingLeaves { get; set; }

        [Option(HelpText = "Option disabled for this command - always uses blurred observation textures", Default = TextureVariant.Blurred)]
        public override TextureVariant TextureVariant { get; set; }

        [Option(HelpText = "Canned blend strategy (Default, Barycentric, Inpaint)", Default = BlendStrategy.Auto)]
        public BlendStrategy BlendStrategy { get; set; }

        [Option(HelpText = "Inpaint backprojected pixels in diff images by this many pixels, 0 to disable, negative for unlimited", Default = 0)]
        public int InpaintWinners { get; set; }

        [Option(HelpText = "Don't barycentric interpolate backprojected pixels in diff images", Default = false)]
        public bool NoBarycentricInterpolateWinners { get; set; }

        [Option(HelpText = "Barycentric interpolate max triangle side length in pixels", Default = 100)]
        public double BarycentricInterpolateMaxTriangleSideLengthPixels { get; set; }

        [Option(HelpText = "Inpaint final diff images by this many pixels, 0 to disable, negative for unlimited", Default = 20)]
        public int InpaintDiff { get; set; }

        [Option(Required = false, HelpText = "Diff image blur radius, 0 to disable", Default = 7)]
        public int BlurDiff { get; set; }

        [Option(HelpText = "Don't fill unknown areas in blended images with average diff", Default = false)]
        public bool NoFillBlendWithAverageDiff { get; set; }

        [Option(HelpText = "Shrinkwrap mesh grid resolution", Default = 1024)]
        public int GridResolution { get; set; }

        [Option(HelpText = "Shrinkwrap mesh projection axis (X, Y, Z)", Default = VertexProjection.ProjectionAxis.Z)]
        public VertexProjection.ProjectionAxis ProjectionAxis { get; set; }

        [Option(HelpText = "Shrinkwrap mode (Project, NearestPoint)", Default = Shrinkwrap.ShrinkwrapMode.Project)]
        public Shrinkwrap.ShrinkwrapMode ShrinkwrapMode { get; set; }

        [Option(HelpText = "Shrinkwrap Project miss behaviour (None, Delaunay, Inpaint)", Default = Shrinkwrap.ProjectionMissResponse.Delaunay)]
        public Shrinkwrap.ProjectionMissResponse ShrinkwrapMiss { get; set; }

        [Option(Required = false, HelpText = "Acceptable error in solving the linear system", Default = LimberDMG.DEF_RESIDUAL_EPSILON)]
        public double ResidualEpsilon { get; set; }

        [Option(Required = false, HelpText = "Number of iterations of relaxation to perform between multigrid iterations", Default = LimberDMG.DEF_NUM_RELAXATION_STEPS)]
        public int NumRelaxationSteps { get; set; }

        [Option(Required = false, HelpText = "Number of multigrid iterations to perform", Default = LimberDMG.DEF_NUM_MULTIGRID_ITERATIONS)]
        public int NumMultigridIterations { get; set; }

        [Option(Required = false, HelpText = "Higher values will cause sharper transitions between images but better conform to the inputs", Default = LimberDMG.DEF_LAMBDA)]
        public double Lambda { get; set; }

        [Option(HelpText = "Redo shrinkwrap mesh", Default = false)]
        public bool RedoShrinkwrapMesh { get; set; }

        [Option(HelpText = "Redo blurred texture", Default = false)]
        public bool RedoBlurredTexture { get; set; }

        [Option(HelpText = "Redo blended blended texture", Default = false)]
        public bool RedoBlendedTexture { get; set; }
    }

    public class BlendImages : TextureCommand
    {
        private const string OUT_DIR = "texturing/BlendProducts";

        private BlendImagesOptions options;

        private bool useExistingLeaves;
        private Image blurredTexture;
        private Image blendedTexture;

        public BlendImages(BlendImagesOptions options) : base(options)
        {
            this.options = options;

            if (options.Redo) 
            {
                options.RedoShrinkwrapMesh = true;
                options.RedoBlurredTexture = true;
                options.RedoBlendedTexture = true;
            }

            options.RedoBlurredTexture |= options.RedoShrinkwrapMesh;
            options.RedoBlendedTexture |= options.RedoBlurredTexture;
        }

        public int Run()
        {
            StartStopwatch();

            try
            {
                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }

                RunPhase("check/generate blurred observation images", BuildBlurredObservationImages);
                if (!useExistingLeaves)
                {
                    RunPhase("check/generate observation image masks", BuildObservationImageMasks);
                    RunPhase("build observation frustum hulls", BuildObsHulls);
                    RunPhase("load or generate shrinkwrap mesh", LoadOrBuildShrinkwrapMesh);
                }
                RunPhase("load or generate blurred texture", LoadOrBuildBlurredTexture);
                RunPhase("load or generate blended texture", LoadOrBuildBlendedTexture);
                RunPhase("generate blended observation images", BuildBlendedObservationImages);
                if (useExistingLeaves && !string.IsNullOrEmpty(tileList.ImageExt) && !options.NoSave)
                {
                    RunPhase("generate blended leaf textures", BuildBlendedLeafTextures);
                }
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex);
                return 1;
            }

            StopStopwatch();

            return 0;
        }

        private bool ParseArgumentsAndLoadCaches()
        {
            if (options.TextureVariant != TextureVariant.Blurred)
            {
                throw new Exception("this command only supports --texturevariant=Blurred");
            }

            if (!base.ParseArgumentsAndLoadCaches(OUT_DIR))
            {
                return false; //help
            }

            useExistingLeaves = false;
            if (!options.NoUseExistingLeaves)
            {
                var dsm = SceneMesh.Find(pipeline, project.Name, meshFrame, MeshVariant.Default, siteDrives);
                if (dsm != null && dsm.TileListGuid != Guid.Empty)
                {
                    sceneMesh = dsm;
                    useExistingLeaves = true;
                    pipeline.LogInfo("using existing leaves");
                    LoadTileList();
                }
            }

            if (!useExistingLeaves)
            {
                sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame, MeshVariant.Shrinkwrap, siteDrives);
                if (sceneMesh == null)
                {
                    sceneMesh = SceneMesh.Create(pipeline, project, meshFrame, MeshVariant.Shrinkwrap, siteDrives,
                                                 noSave: options.NoSave);
                }
            }

            if (options.BlendStrategy == BlendStrategy.Auto)
            {
                options.BlendStrategy = useExistingLeaves ? BlendStrategy.Inpaint : BlendStrategy.Barycentric;
            }

            switch (options.BlendStrategy)
            {
                case BlendStrategy.None: break;
                case BlendStrategy.Barycentric:
                    {
                        options.InpaintWinners = 0;
                        options.NoBarycentricInterpolateWinners = false;
                        options.InpaintDiff = 20;
                        options.BlurDiff = 7;
                        options.NoFillBlendWithAverageDiff = false;
                        break;
                    }
                case BlendStrategy.Inpaint:
                    {
                        options.InpaintWinners = 20;
                        options.NoBarycentricInterpolateWinners = true;
                        options.InpaintDiff = 0;
                        options.BlurDiff = 7;
                        options.NoFillBlendWithAverageDiff = false;
                        break;
                    }
                default: throw new Exception("unknown blend strategy: " + options.BlendStrategy);
            }

            return true;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            throw new NotImplementedException();
        }

        protected override void LoadTileList()
        {
            base.LoadTileList();

            if (!tileList.HasIndexImages)
            {
                throw new Exception("tile list missing backproject index images");
            }
        }

        private void LoadOrBuildShrinkwrapMesh()
        {
            void writeDebug()
            {
                if (options.WriteDebug)
                {
                    SaveMesh(mesh, sceneMesh.Name);
                }
            }

            if (sceneMesh.MeshGuid != Guid.Empty && !options.RedoShrinkwrapMesh)
            {
                pipeline.LogInfo("loading existing shrinkwrap mesh from database");
                mesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, sceneMesh.MeshGuid).Mesh;
                writeDebug();
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
                SceneMesh dsm = SceneMesh.Find(pipeline, project.Name, meshFrame, MeshVariant.Default, siteDrives);
                if (dsm != null && dsm.MeshGuid != Guid.Empty)
                {
                    pipeline.LogInfo("loading scene mesh from database");
                    inputMesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, dsm.MeshGuid).Mesh;
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

            pipeline.LogInfo("generating shrinkwrap mesh in frame {0} from input mesh with {1} faces" +
                             ": grid resolution {2}, projection axis {3}, mode {4}, miss behavior {5}",
                             meshFrame, Fmt.KMG(inputMesh.Faces.Count), options.GridResolution, options.ProjectionAxis,
                             options.ShrinkwrapMode, options.ShrinkwrapMiss);

            Mesh gridMesh = Shrinkwrap.BuildGrid(inputMesh, options.GridResolution, options.GridResolution,
                                                 options.ProjectionAxis);

            mesh = Shrinkwrap.Wrap(gridMesh, inputMesh, options.ShrinkwrapMode, options.ProjectionAxis,
                                   options.ShrinkwrapMiss);

            pipeline.LogInfo("built shrinkwrap mesh with {0} faces", Fmt.KMG(mesh.Faces.Count));

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("shrinkwrap mesh empty");
            }

            if (!mesh.HasUVs)
            {
                throw new Exception("shrinkwrap mesh needs UVs");
            }

            if (!options.NoSave)
            {
                pipeline.LogInfo("saving shrinkwrap mesh");
                sceneMesh.SetBounds(mesh.Bounds());
                var meshProd = new PlyGZDataProduct(mesh);
                pipeline.SaveDataProduct(project, meshProd);
                sceneMesh.MeshGuid = meshProd.Guid;
                sceneMesh.Save(pipeline);
            }

            writeDebug();
        }

        private void LoadOrBuildBlurredTexture()
        {
            if (sceneMesh.BlurredTextureGuid != Guid.Empty && sceneMesh.BackprojectIndexGuid != Guid.Empty &&
                !options.RedoBlurredTexture)
            {
                pipeline.LogInfo("loading blurred texture from database");
                var texGuid = sceneMesh.BlurredTextureGuid;
                var indexGuid = sceneMesh.BackprojectIndexGuid;
                blurredTexture = pipeline.GetDataProduct<PngDataProduct>(project, texGuid).Image;
                backprojectIndex = pipeline.GetDataProduct<TiffDataProduct>(project, indexGuid).Image;
                if (blurredTexture.Width != resolution || blurredTexture.Height != resolution ||
                    backprojectIndex.Width != resolution || backprojectIndex.Height != resolution)
                {
                    throw new Exception(string.Format("existing blurred texture or index not {0}x{0}, " +
                                                      "re-run with --redoblurredtexture", resolution, resolution));
                }
                if (options.WriteDebug)
                {
                    SaveBackprojectIndexDebug(backprojectIndex);
                    SaveBackprojectTextureDebug(blurredTexture, TextureVariant.Blurred);
                }
                return;
            }

            if (useExistingLeaves)
            {
                pipeline.LogInfo("creating blurred texture from existing leaves");
                BuildBackprojectIndexFromLeaves();
                BuildBackprojectResultsFromIndex();
            }
            else
            {
                pipeline.LogInfo("creating blurred texture from shrinkwrap mesh");
                BuildSceneCaster();
                BackprojectObservations();
                BuildBackprojectIndex();
            }

            blurredTexture = BuildBackprojectTexture(TextureVariant.Blurred);

            pipeline.LogInfo("created {0}x{0} blurred texture", resolution);
        }

        private void LoadOrBuildBlendedTexture()
        {
            void writeDebug()
            {
                if (options.WriteDebug)
                {
                    string name = sceneMesh.Name + "_backprojectTexture_" + TextureVariant.Blended;
                    SaveImage(blendedTexture, name);
                    if (mesh != null)
                    {
                        SaveMesh(mesh, name, name + imageExt);
                    }
                }
            } 

            if (sceneMesh.BlendedTextureGuid != Guid.Empty && !options.RedoBlendedTexture)
            {
                pipeline.LogInfo("loading blended texture from database");
                var texGuid = sceneMesh.BlendedTextureGuid;
                blendedTexture = pipeline.GetDataProduct<PngDataProduct>(project, texGuid).Image;
                writeDebug();
                return;
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
                    int obsIndex = (int)backprojectIndex[0, r, c];

                    index[0, r, c] = obsIndex;

                    var obs = obsIndex >= Observation.MIN_INDEX ? indexedImages[obsIndex] : null;

                    bool hasGray = obs != null;
                    bool hasColor = obs != null && obs.Bands == 3;
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
            blendedTexture = dmg.StitchImage(blurredTexture, index, flags);

            pipeline.LogInfo("created {0}x{0} blended texture", resolution);

            if (!options.NoSave)
            {
                var texProd = new PngDataProduct(blendedTexture);
                pipeline.SaveDataProduct(project, texProd);
                sceneMesh.BlendedTextureGuid = texProd.Guid;
                sceneMesh.Save(pipeline);
            }

            writeDebug();
        }

        private void BuildBlendedObservationImages()
        {
            pipeline.LogInfo("collecting backprojected pixels for each observation");

            //obs index => (obsCol, obsRow) => (sumBlendedR, sumBlendedG, sumBlendedB, num)
            var winners = new Dictionary<int, Dictionary<Vector2, Vector4>>();
            
            for (int r = 0; r < resolution; r++)
            {
                for (int c = 0; c < resolution; c++)
                {
                    int obsIndex = (int)backprojectIndex[0, r, c];

                    if (obsIndex < Observation.MIN_INDEX)
                    {
                        continue;
                    }

                    if (!winners.ContainsKey(obsIndex))
                    {
                        winners[obsIndex] = new Dictionary<Vector2, Vector4>();
                    }
                    var winnersForObs = winners[obsIndex];

                    int obsRow = (int)backprojectIndex[1, r, c];
                    int obsCol = (int)backprojectIndex[2, r, c];
                    Vector2 obsPixel = new Vector2(obsCol, obsRow);

                    float blendedR = blendedTexture[0, r, c];
                    float blendedG = blendedTexture[1, r, c];
                    float blendedB = blendedTexture[2, r, c];

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

            void writeDebug(Image img, Observation obs, string suffix, int markWinnersForObs = -1)
            {
                if (options.WriteDebug)
                {

                    if (markWinnersForObs >= Observation.MIN_INDEX)
                    {
                        img = new Image(img);
                        while (img.Bands < 3 )
                        {
                            img.AddBand();
                        }
                        
                        float[] winnerColor = new float[] { 0, 1, 0 };
                        foreach (var pixel in winners[markWinnersForObs].Keys)
                        {
                            img.SetBandValues((int)pixel.Y, (int)pixel.X, winnerColor);
                        }
                    }

                    SaveDebugWedgeImage(img, obs, suffix);
                }
            }
            
            int no = indexedImages.Count;
            var strat = options.BlendStrategy;
            pipeline.LogInfo("creating blended images for {0} observations{1}",
                             no, strat != BlendStrategy.None ? (", strategy " + strat) : "");
            pipeline.LogInfo("inpaint winners: {0}, barycentric interp: {1}, inpaint diff: {2}, blur diff: {3}, " +
                             "fill avg: {4}", options.InpaintWinners, !options.NoBarycentricInterpolateWinners,
                             options.InpaintDiff, options.BlurDiff, !options.NoFillBlendWithAverageDiff);

            if (!options.NoBarycentricInterpolateWinners && options.BarycentricInterpolateMaxTriangleSideLengthPixels > 0)
            {
                pipeline.LogInfo("barycentric interpolate max triangle side {0}px",
                                 options.BarycentricInterpolateMaxTriangleSideLengthPixels);
            }

            double maxLuminance = (new Rgb() { R = 255, G = 255, B = 255 }).To<Lab>().L;

            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(indexedImages, entry => {

                    int obsIndex = entry.Key;
                    Observation obs = entry.Value;

                    if (!winners.ContainsKey(obsIndex))
                    {
                        pipeline.LogWarn("cannot blend image for observation {0}, no mesh points backprojected to it",
                                         obs.Name);
                        if (!options.NoSave)
                        {
                            obs.BlendedGuid = Guid.Empty;
                            obs.Save(pipeline);
                        }
                        Interlocked.Increment(ref nc);
                        return;
                    }

                    if (obs.Bands != 3 && obs.Bands != 1)
                    {
                        pipeline.LogWarn("blending observation image {0} with {1} bands not supported",
                                         obs.Name, obs.Bands);
                        if (!options.NoSave)
                        {
                            obs.BlendedGuid = Guid.Empty;
                            obs.Save(pipeline);
                        }
                        Interlocked.Increment(ref nc);
                        return;
                    }
                    
                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("blending image for observation {0}, processing {1} in parallel, " +
                                         "completed {2}/{3}", obs.Name, np, nc, no);
                    }
                    
                    Image img = pipeline.LoadImage(obs.Url);
                    writeDebug(img, obs, "");
                    
                    Image blr = pipeline.GetDataProduct<PngDataProduct>(project, obs.BlurredGuid).Image;

                    var diffImage = new Image(img.Bands, img.Width, img.Height);
                    diffImage.CreateMask(true); //all pixels initially masked

                    var avgDiff = new float[obs.Bands];;
                    int numWinners = 0;
                    foreach (var winner in winners[obsIndex])
                    {
                        Vector2 obsPixel = winner.Key;
                        Vector4 blendedSum = winner.Value;
                        Vector3 blendedRGB = new Vector3(blendedSum.X, blendedSum.Y, blendedSum.Z) / blendedSum.W;
                        
                        int or = (int)obsPixel.Y;
                        int oc = (int)obsPixel.X;
                        if (or < 0 || or >= img.Height || oc < 0 || oc >= img.Width)
                        {
                            pipeline.LogWarn("backprojected pixel out of bounds in observation {0}", obs.Name);
                            continue;
                        }

                        float[] diff = null;
                        if (obs.Bands == 3)
                        {
                            Vector3 d = blendedRGB - new Vector3(blr[0, or, oc], blr[1, or, oc], blr[2, or, oc]);
                            diff = new float[] { (float)d.X, (float)d.Y, (float)d.Z };
                        }
                        else
                        {
                            float br = (float)blendedRGB.X;
                            float bg = (float)blendedRGB.Y;
                            float bb = (float)blendedRGB.Z;
                            double luminance = (new Rgb() { R = 255 * br, G = 255 * bg, B = 255 * bb }).To<Lab>().L;
                            diff = new float[] { (float)(luminance / maxLuminance) - blr[0, or, oc] };
                        }

                        for (int i = 0; i < obs.Bands; i++)
                        {
                            avgDiff[i] += diff[i];
                        }
                        
                        diffImage.SetBandValues(or, oc, diff);
                        diffImage.SetMaskValue(or, oc, false);

                        numWinners++;
                    }

                    if (numWinners > 0)
                    {
                        for (int i = 0; i < obs.Bands; i++)
                        {
                            avgDiff[i] /= numWinners;
                        }
                        
                        if (options.InpaintWinners != 0)
                        {
                            diffImage.Inpaint(options.InpaintWinners);
                        }
                        
                        if (!options.NoBarycentricInterpolateWinners && numWinners >= 3)
                        {
                            Func<Mesh, Face, bool> filter = null;
                            double ms = options.BarycentricInterpolateMaxTriangleSideLengthPixels;
                            if (ms > 0)
                            {
                                double ms2 = ms * ms;
                                filter = (mesh, tri) =>
                                {
                                    var v0 = mesh.Vertices[tri.P0];
                                    var v1 = mesh.Vertices[tri.P1];
                                    var v2 = mesh.Vertices[tri.P2];
                                    var d0 = Vector3.DistanceSquared(v0.Position, v1.Position);
                                    if (d0 > ms2) return false;
                                    var d1 = Vector3.DistanceSquared(v1.Position, v2.Position);
                                    if (d1 > ms2) return false;
                                    var d2 = Vector3.DistanceSquared(v2.Position, v0.Position);
                                    if (d2 > ms2) return false;
                                    return true;
                                };
                            }
                            Rasterizer.BarycentricInterpolate(diffImage, filter);
                        }
                        
                        if (options.InpaintDiff != 0)
                        {
                            diffImage.Inpaint(options.InpaintDiff);
                        }

                        if (options.BlurDiff > 0)
                        {
                            diffImage.GaussianBoxBlur(options.BlurDiff, blendMasked: true);
                        }

                        writeDebug(diffImage, obs, "_diff");
                        
                        Image blendedImage = diffImage; //yes, alias
                        for (int b = 0; b < img.Bands; b++)
                        {
                            for (int r = 0; r < img.Height; r++)
                            {
                                for (int c = 0; c < img.Width; c++)
                                {
                                    if (diffImage.IsValid(r, c))
                                    {
                                        blendedImage[b, r, c] = MathE.Clamp01(diffImage[b, r, c] + img[b, r, c]);
                                    }
                                    else if (!options.NoFillBlendWithAverageDiff)
                                    {
                                        blendedImage[b, r, c] = MathE.Clamp01(avgDiff[b] + img[b, r, c]);
                                    }
                                    else
                                    {
                                        blendedImage[b, r, c] = img[b, r, c];
                                    }
                                }
                            }
                        }
                        
                        blendedImage.DeleteMask();
                        
                        writeDebug(blendedImage, obs, "_blended");
                        writeDebug(blendedImage, obs, "_blended_winners", obsIndex);
                        
                        if (!options.NoSave)
                        {
                            var imgProd = new PngDataProduct(blendedImage);
                            pipeline.SaveDataProduct(project, imgProd);
                            obs.BlendedGuid = imgProd.Guid;
                            obs.Save(pipeline);
                        }
                    }
                    else
                    {
                        pipeline.LogWarn("cannot blend image for observation {0}, no valid backprojections", obs.Name);
                        if (!options.NoSave)
                        {
                            obs.BlendedGuid = Guid.Empty;
                            obs.Save(pipeline);
                        }
                    }

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });
        }

        private void BuildBackprojectIndexFromLeaves()
        {
            pipeline.LogInfo("building backproject index from leaves");

            BoundingBox? bounds = null;
            if (!string.IsNullOrEmpty(options.InputMesh))
            {
                pipeline.LogInfo("loading input mesh from {0}", options.InputMesh);
                bounds = Mesh.Load(pipeline.GetFileCached(options.InputMesh, "meshes")).Bounds();
            }
            else
            {
                bounds = sceneMesh.GetBounds();
                if (!bounds.HasValue)
                {
                    throw new Exception(string.Format("scene mesh {0} missing bounds", sceneMesh.Name));
                }
            }
            var boundsSize = bounds.Value.Size();
            pipeline.LogInfo("scene mesh XY plane bounds: {0:F3}x{1:F3}", boundsSize.X, boundsSize.Y);

            backprojectIndex = new Image(3, resolution, resolution);

            var opts = Rasterizer.BEVOptions.DirectToImage(backprojectIndex);

            double maxDim = Math.Max(boundsSize.X, boundsSize.Y);
            opts.MetersPerPixel = maxDim / resolution;
            opts.MeshOffset = new Vector2(boundsSize.X, boundsSize.Y) * 0.5;
            
            pipeline.LogInfo("rasterizing {0}x{0} backproject index from {1} leaves, {2:F3} meters/pixel",
                             resolution, tileList.LeafNames.Count, opts.MetersPerPixel);

            string leafFolder = DecorateOutDir(TilingCommand.OUT_DIR);
            CoreLimitedParallel.ForEach(tileList.LeafNames, leaf =>
            {
                string meshUrl = pipeline.GetStorageUrl(leafFolder, project.Name, leaf + tileList.MeshExt);
                var leafMesh = Mesh.Load(pipeline.GetFileCached(meshUrl, "meshes"));

                string indexName = leaf + TileList.INDEX_FILE_SUFFIX + TileList.INDEX_FILE_EXT;
                string indexUrl = pipeline.GetStorageUrl(leafFolder, project.Name, indexName);
                var leafIndex = pipeline.LoadImage(indexUrl);

                //only rasterize winning pixels from the leaf
                //any pixels in backprojectIndex that didn't get rasterized then remain masked
                //it seems common that there are some losing pixels right around the leaf boundary
                //(not sure why, mb that is a bug)
                //if we leave those masked then below we can inpaint them
                leafIndex.CreateMask();
                for (int r = 0; r < leafIndex.Height; r++)
                {
                    for (int c = 0; c < leafIndex.Width; c++)
                    {
                        if (leafIndex[0, r, c] < Observation.MIN_INDEX)
                        {
                            leafIndex.SetMaskValue(r, c, true);
                        }
                    }
                }

                Rasterizer.RenderBirdsEyeView(leafMesh, leafIndex, opts);
            });

            //fill small gaps along tile boundaries, should make LimberDMG happier
            backprojectIndex.Inpaint(2, useAnyNeighbor: true);

            if (tcopts.WriteDebug)
            {
                SaveBackprojectIndexDebug(backprojectIndex);
            }
        }

        private void BuildBlendedLeafTextures()
        {
            pipeline.LogInfo("blending leaf textures");
            string leafFolder = DecorateOutDir(TilingCommand.OUT_DIR);
            int curLeafNum = 0, leafCount = tileList.LeafNames.Count;
            CoreLimitedParallel.ForEach(tileList.LeafNames, leaf =>
            {
                Interlocked.Increment(ref curLeafNum);
                pipeline.LogInfo("blending leaf texture {0}/{1} ({2:F2}%): {3}", curLeafNum, leafCount,
                                 100 * curLeafNum / (float)leafCount, leaf);
                string indexName = leaf + TileList.INDEX_FILE_SUFFIX + TileList.INDEX_FILE_EXT;
                string indexUrl = pipeline.GetStorageUrl(leafFolder, project.Name, indexName);
                var index = pipeline.LoadImage(indexUrl);
                var results = Backproject.BuildResultsFromIndex(index, indexedImages);
                var texture = new Image(3, index.Width, index.Height);
                Backproject.FillOutputTexture(pipeline, results, texture, TextureVariant.Blended,
                                              fallbackToOriginal: true);
                TemporaryFile.GetAndDelete(tileList.ImageExt, tmpFile => {
                        texture.Save<byte>(tmpFile);
                        string textureUrl = pipeline.GetStorageUrl(leafFolder, project.Name, leaf + tileList.ImageExt);
                        pipeline.SaveFile(tmpFile, textureUrl);
                    });
            });
        }
    }
}
