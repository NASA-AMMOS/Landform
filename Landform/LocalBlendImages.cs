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
    [Verb("local-blend-images", HelpText = "blend observation images")]
    public class LocalBlendImagesOptions : TextureCommandOptions
    {
        [Option(HelpText = "Option disabled for this command - always uses blurred observation textures", Default = TextureVariant.Blurred)]
        public override TextureVariant TextureVariant { get; set; }

        [Option(HelpText = "Inpaint blended observation diff images by this many pixels, 0 to disable, negative for unlimited", Default = 20)]
        public int Inpaint { get; set; }

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

        [Option(HelpText = "Redo shrinkwrap texture", Default = false)]
        public bool RedoShrinkwrapTexture { get; set; }

        [Option(HelpText = "Redo blended shrinkwrap texture", Default = false)]
        public bool RedoShrinkwrapBlendedTexture { get; set; }

        [Option(HelpText = "Redo blended observation textures", Default = false)]
        public bool RedoBlendedObservationTextures { get; set; }
    }

    public class LocalBlendImages : TextureCommand
    {
        private const string OUT_DIR = "texturing/BlendProducts";

        private LocalBlendImagesOptions options;

        private Dictionary<int, Observation> indexedObservations;

        private Image shrinkwrapBlurredTexture;
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
            StartStopwatch();

            try
            {
                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }

                RunPhase("check/generate blurred observation images", BuildBlurredObservationImages);
                RunPhase("check/generate observation image masks", BuildObservationImageMasks);
                RunPhase("load or generate shrinkwrap mesh", LoadOrBuildShrinkwrapMesh);
                RunPhase("load or generate shrinkwrap blurred texture", LoadOrBuildShrinkwrapBlurredTexture);
                RunPhase("load or generate blended texture", LoadOrBuildBlendedTexture);
                RunPhase("generate blended observation images", BuildBlendedObservationImages);
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

            indexedObservations = new Dictionary<int, Observation>();
            foreach (var obs in imageObservations)
            {
                indexedObservations[obs.Index] = obs;
            }

            return true;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            throw new NotImplementedException();
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

            sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame, MeshVariant.Shrinkwrap, siteDrives);

            if (sceneMesh != null && sceneMesh.MeshGuid != Guid.Empty && !options.RedoShrinkwrapMesh)
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
                SceneMesh sm = SceneMesh.Find(pipeline, project.Name, meshFrame, MeshVariant.Default, siteDrives);
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

            pipeline.LogInfo("generating shrinkwrap mesh in frame {0} from input mesh with {1} faces" +
                             ": grid resolution {2}, projection axis {3}, mode {4}, miss behavior {5}",
                             meshFrame, Fmt.KMG(inputMesh.Faces.Count), options.GridResolution, options.ProjectionAxis,
                             options.ShrinkwrapMode, options.ShrinkwrapMiss);

            Mesh gridMesh = Shrinkwrap.BuildGrid(inputMesh, options.GridResolution, options.GridResolution,
                                                 options.ProjectionAxis);

            mesh = Shrinkwrap.Wrap(gridMesh, inputMesh, options.ShrinkwrapMode, options.ProjectionAxis, options.ShrinkwrapMiss);

            pipeline.LogInfo("built shrinkwrap mesh with {0} faces", Fmt.KMG(mesh.Faces.Count));

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("shrinkwrap mesh empty");
            }

            if (!mesh.HasUVs)
            {
                throw new Exception("shrinkwrap mesh needs UVs");
            }

            if (sceneMesh == null)
            {
                sceneMesh = SceneMesh.Create(pipeline, project, meshFrame, MeshVariant.Shrinkwrap, siteDrives,
                                             noSave: options.NoSave);
            }

            if (!options.NoSave)
            {
                pipeline.LogInfo("saving shrinkwrap mesh");
                var meshProd = new PlyGZDataProduct(mesh);
                pipeline.SaveDataProduct(project, meshProd);
                sceneMesh.MeshGuid = meshProd.Guid;
                sceneMesh.Save(pipeline);
            }

            writeDebug();
        }

        private void LoadOrBuildShrinkwrapBlurredTexture()
        {
            if (sceneMesh.BlurredTextureGuid != Guid.Empty && sceneMesh.BackprojectIndexGuid != Guid.Empty &&
                !options.RedoShrinkwrapTexture)
            {
                pipeline.LogInfo("loading shrinkwrap blurred texture from database");
                var texGuid = sceneMesh.BlurredTextureGuid;
                var indexGuid = sceneMesh.BackprojectIndexGuid;
                shrinkwrapBlurredTexture = pipeline.GetDataProduct<PngDataProduct>(project, texGuid).Image;
                shrinkwrapBackprojectIndex = pipeline.GetDataProduct<TiffDataProduct>(project, indexGuid).Image;
                if (shrinkwrapBlurredTexture.Width != resolution || shrinkwrapBlurredTexture.Height != resolution ||
                    shrinkwrapBackprojectIndex.Width != resolution || shrinkwrapBackprojectIndex.Height != resolution)
                {
                    throw new Exception(string.Format("existing backproject texture or index not {0}x{0}, " +
                                                      "re-run with --redoshrinkwraptexture", resolution, resolution));
                }
                if (options.WriteDebug)
                {
                    SaveBackprojectIndexDebug(shrinkwrapBackprojectIndex);
                    SaveBackprojectTextureDebug(shrinkwrapBlurredTexture, TextureVariant.Blurred);
                }
                return;
            }

            BuildSceneCaster();

            BackprojectObservations();

            shrinkwrapBackprojectIndex = BuildBackprojectIndex();

            shrinkwrapBlurredTexture = BuildBackprojectTexture(TextureVariant.Blurred);

            pipeline.LogInfo("created {0}x{0} shrinkwrap texture", resolution);
        }

        private void LoadOrBuildBlendedTexture()
        {
            void writeDebug()
            {
                if (options.WriteDebug)
                {
                    pipeline.LogInfo("saving shrinkwrap blended texture and textured mesh");
                    string name = sceneMesh.Name + "_backprojectTexture_" + TextureVariant.Blended;
                    SaveImage(shrinkwrapBlendedTexture, name);
                    SaveMesh(mesh, name, name + imageExt);
                }
            } 

            if (sceneMesh.BlendedTextureGuid != Guid.Empty && !options.RedoShrinkwrapBlendedTexture)
            {
                pipeline.LogInfo("loading shrinkwrap blended texture from database");
                var texGuid = sceneMesh.BlendedTextureGuid;
                shrinkwrapBlendedTexture = pipeline.GetDataProduct<PngDataProduct>(project, texGuid).Image;
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
                    int obsIndex = (int)shrinkwrapBackprojectIndex[0, r, c];

                    index[0, r, c] = obsIndex;

                    var obs = obsIndex >= Observation.MIN_INDEX ? indexedObservations[obsIndex] : null;

                    bool hasGray = true;
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
            shrinkwrapBlendedTexture = dmg.StitchImage(shrinkwrapBlurredTexture, index, flags);

            pipeline.LogInfo("created {0}x{0} shrinkwrap blended texture", resolution);

            if (!options.NoSave)
            {
                pipeline.LogInfo("saving shrinkwrap blended texture");
                var texProd = new PngDataProduct(shrinkwrapBlendedTexture);
                pipeline.SaveDataProduct(project, texProd);
                sceneMesh.BlendedTextureGuid = texProd.Guid;
                sceneMesh.Save(pipeline);
            }

            writeDebug();
        }

        private void BuildBlendedObservationImages()
        {
            pipeline.LogInfo("collecting backprojected pixels for each observation");

            //obs index => (obsPixelCol, obsPixelRow) => (sumBlendedR, sumBlendedG, sumBlendedB, num)
            var winners = new Dictionary<int, Dictionary<Vector2, Vector4>>();
            
            for (int r = 0; r < resolution; r++)
            {
                for (int c = 0; c < resolution; c++)
                {
                    int obsIndex = (int)shrinkwrapBackprojectIndex[0, r, c];

                    if (obsIndex < Observation.MIN_INDEX)
                    {
                        continue;
                    }

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
            
            pipeline.LogInfo("creating blended observation images");

            double maxLuminance = (new Rgb() { R = 255, G = 255, B = 255 }).To<Lab>().L;

            int no = indexedObservations.Count;
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(indexedObservations, entry => {

                    int obsIndex = entry.Key;
                    Observation obs = entry.Value;

                    if (!options.RedoBlendedObservationTextures && obs.BlendedGuid != Guid.Empty)
                    {
                        if (options.WriteDebug)
                        {
                            writeDebug(pipeline.LoadImage(obs.Url), obs, "");
                            var dbg = pipeline.GetDataProduct<PngDataProduct>(project, obs.BlendedGuid).Image;
                            //not generating _diff debug image here, run with --redoblendedobservationtextures for that
                            writeDebug(dbg, obs, "_blended");
                            writeDebug(dbg, obs, "_blended_winners", obsIndex);
                        }
                        Interlocked.Increment(ref nc);
                        return;
                    }

                    if (!winners.ContainsKey(obsIndex))
                    {
                        pipeline.LogWarn("cannot blend image for observation {0}, " +
                                         "no shrinkwrap mesh points backprojected to it", obs.Name);
                        if (!options.NoSave)
                        {
                            obs.BlendedGuid = Guid.Empty;
                            obs.Save(pipeline);
                        }
                        return;
                    }

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("blending image for observation {0}, processing {1} in parallel, " +
                                         "completed {2}/{3}", obs.Name, np, nc, no);
                    }
                    
                    if (obs.Bands != 3 && obs.Bands != 1)
                    {
                        pipeline.LogWarn("blending observation image {0} with {1} bands not supported",
                                         obs.Name, obs.Bands);
                        Interlocked.Increment(ref nc);
                        return;
                    }
                    
                    Image img = pipeline.LoadImage(obs.Url);
                    writeDebug(img, obs, "");
                    
                    Image blr = pipeline.GetDataProduct<PngDataProduct>(project, obs.BlurredGuid).Image;

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
                        
                        diffImage.SetBandValues(or, oc, diff);
                        diffImage.SetMaskValue(or, oc, false);
                    }
                    
                    if (winners[obsIndex].Count >= 3)
                    {
                        Rasterizer.BarycentricInterpolate(diffImage);
                    }
                    
                    diffImage.Inpaint(options.Inpaint);
                    
                    writeDebug(diffImage, obs, "_diff");

                    Image blendedImage = diffImage; //yes, alias
                    for (int b = 0; b < img.Bands; b++)
                    {
                        for (int r = 0; r < img.Height; r++)
                        {
                            for (int c = 0; c < img.Width; c++)
                            {
                                blendedImage[b, r, c] = MathE.Clamp01(diffImage[b, r, c] + img[b, r, c]);
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

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });
        }
    }
}
