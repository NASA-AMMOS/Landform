using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CommandLine;
using Microsoft.Xna.Framework;
using ColorMine.ColorSpaces;
using JPLOPS.Util;
using JPLOPS.MathExtensions;
using JPLOPS.Imaging;
using JPLOPS.RayTrace;
using JPLOPS.Geometry;
using JPLOPS.Pipeline;
using JPLOPS.Pipeline.Texturing;
using JPLOPS.Pipeline.AlignmentServer;

namespace JPLOPS.Landform
{
    public class TextureCommandOptions : GeometryCommandOptions
    {
        [Option(HelpText = "Option disabled for this command ", Default = 0)]
        public override int DecimateWedgeMeshes { get; set; }

        [Option(HelpText = "Option disabled for this command", Default = 0)]
        public override int DecimateWedgeImages { get; set; }

        [Option(HelpText = "Wedge debug image decimation blocksize, 0 to disable, -1 for auto", Default = -1)]
        public virtual int DecimateDebugWedgeImages { get; set; }

        [Option(Default = null, HelpText = "Scene mesh, search project storage if omitted")]
        public string InputMesh { get; set; }

        [Option(HelpText = "Use level of detail meshes provided in input mesh", Default = false)]
        public bool LoadLODs { get; set; }

        [Option(HelpText = "Create or fix LOD meshes, comma separated list of min-max ranges, finest to coarsest, or \"null\" to disable", Default = TextureCommand.DEF_FIXUP_LODS)]
        public string FixupLODs { get; set; }

        [Option(HelpText = "Occlusion mesh in same frame as input mesh, defaults to input mesh", Default = null)]
        public string OcclusionMesh { get; set; }

        [Option(HelpText = "A tunable parameter for the Observation Selection Strategy used in backproject (range 0-1)", Default = TexturingDefaults.BACKPROJECT_QUALITY)]
        public virtual double BackprojectQuality { get; set; }

        [Option(HelpText = "Max backproject glancing angle relative to mesh normal, 90 to disable glance filter", Default = TexturingDefaults.BACKPROJECT_MAX_GLANCING_ANGLE_DEGREES)]
        public double MaxGlancingAngleDegrees { get; set; }

        [Option(HelpText = "The smallest distance (meters) for a raycast determined to be significant, prevents self intersections", Default = TexturingDefaults.RAYCAST_TOLERANCE)]
        public virtual double RaycastTolerance { get; set; }

        [Option(HelpText = "Write extended backproject debug info", Default = false)]
        public bool WriteBackprojectDebug { get; set; }

        [Option(HelpText = "Verbose backproject spew", Default = false)]
        public bool VerboseBackproject { get; set; }

        [Option(HelpText = "The strategy used to pick which of the many source image candidates for a given area is selected in backproject (Exhaustive, Spatial)", Default = TexturingDefaults.OBS_SEL_STRATEGY)]
        public virtual ObsSelectionStrategyName ObsSelectionStrategy { get; set; }
        
        [Option(Required = false, HelpText = "Observation image blur radius", Default = TexturingDefaults.OBSERVATION_BLUR_RADIUS)]
        public int ObservationBlurRadius { get; set; }

        [Option(HelpText = "Redo blurred observation textures", Default = false)]
        public bool RedoBlurredObservationTextures { get; set; }

        [Option(HelpText = "Redo blended observation textures", Default = false)]
        public bool RedoBlendedObservationTextures { get; set; }

        [Option(HelpText = "Number of inpaint missing pixels for backproject, 0 to disable inpaint, negative for unlimited", Default = TexturingDefaults.BACKPROJECT_INPAINT_MISSING)]
        public int BackprojectInpaintMissing { get; set; }

        [Option(HelpText = "Number of inpaint gutter pixels for backproject, 0 to disable inpaint, negative for unlimited", Default = TexturingDefaults.BACKPROJECT_INPAINT_GUTTER)]
        public int BackprojectInpaintGutter { get; set; }

        [Option(HelpText = "Prefer color images (Never, Always, EquivalentScores)", Default = TexturingDefaults.OBS_SEL_PREFER_COLOR)]
        public virtual PreferColorMode PreferColor { get; set; }

        [Option(HelpText = "Colorize mono images to median chrominance", Default = false)]
        public bool Colorize { get; set; }

        [Option(HelpText = "Disable generating UVs by texture projection", Default = false)]
        public bool NoTextureProjection { get; set; }

        [Option(HelpText = "Don't convert tileset images from linear RGB to sRGB", Default = false)]
        public bool NoConvertLinearRGBToSRGB { get; set; }

        [Option(HelpText = "Disable LRU image cache (longer runtime but lower memory footprint)", Default = false)]
        public bool DisableImageCache { get; set;}

        [Option(HelpText = "Barycentric interpolate backprojected pixels in diff images", Default = false)]
        public bool BarycentricInterpolateWinners { get; set; }

        [Option(HelpText = "Barycentric interpolate max triangle side length in pixels", Default = 100)]
        public double BarycentricInterpolateMaxTriangleSideLengthPixels { get; set; }

        [Option(HelpText = "Inpaint diff images by this many pixels (after Barycentric interpolation, if any), 0 to disable, negative for unlimited", Default = -1)]
        public int InpaintDiff { get; set; }

        [Option(HelpText = "Diff image blur radius, 0 to disable", Default = TexturingDefaults.DIFF_BLUR_RADIUS)]
        public int BlurDiff { get; set; }

        [Option(HelpText = "Don't fill unknown areas in blended images with average diff", Default = false)]
        public bool NoFillBlendWithAverageDiff { get; set; }

        [Option(HelpText = "Acceptable error in solving the linear system", Default = LimberDMG.DEF_RESIDUAL_EPSILON)]
        public double ResidualEpsilon { get; set; }

        [Option(HelpText = "Number of iterations of relaxation to perform between multigrid iterations", Default = LimberDMG.DEF_NUM_RELAXATION_STEPS)]
        public int NumRelaxationSteps { get; set; }

        [Option(HelpText = "Number of multigrid iterations to perform", Default = LimberDMG.DEF_NUM_MULTIGRID_ITERATIONS)]
        public int NumMultigridIterations { get; set; }

        [Option(HelpText = "Higher values will cause sharper transitions between images but better conform to the inputs", Default = LimberDMG.DEF_LAMBDA)]
        public double BlendLambda { get; set; }

        [Option(HelpText = "Don't blend leaves parallel", Default = false)]
        public bool NoBlendLeavesInParallel { get; set; }
    }

    public class TextureCommand : GeometryCommand
    {
        public const string DEF_FIXUP_LODS = "90000-300000,20000-100000,4000-30000,1000-5000,100-2000";

        protected TextureCommandOptions tcopts;

        protected SceneCaster sceneCaster;

        protected ObsSelectionStrategy backprojectStrategy;
        protected IDictionary<Pixel, Backproject.ObsPixel> backprojectResults;
        protected string backprojectDebugDir;
        protected Image backprojectIndex;

        protected TileList tileList;

        protected SceneMesh sceneMesh;
        protected Image sceneTexture;
        protected Matrix? meshToCamera; //non-null iff texture projection enabled

        protected Mesh mesh; //finest LOD
        protected List<Mesh> meshLOD; //meshLOD[0] = mesh, coarser LODs populated iff --loadlods
        protected MeshOperator meshOp; //finest LOD
        protected List<MeshOperator> meshOpForLOD; //meshOpForLOD[0] = meshOp, coarser LODs populated iff --loadlods
        protected Matrix? meshTransform;

        protected int numProjectAtlas;

        protected TextureCommand(TextureCommandOptions tcopts) : base(tcopts)
        {
            this.tcopts = tcopts;
            if (tcopts.Redo)
            {
                tcopts.RedoBlurredObservationTextures = true;
                tcopts.RedoBlendedObservationTextures = true;
            }
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            if (tcopts.DisableImageCache)
            {
                pipeline.LogInfo("disabling LRU image cache");
                pipeline.SetImageCacheCapacity(0);
            }

            if (tcopts.DecimateWedgeImages < 0 || tcopts.DecimateWedgeImages > 1)
            {
                throw new Exception("--decimatewedgeimages is not implemented for this command");
            }

            if (tcopts.DecimateWedgeMeshes < 0 || tcopts.DecimateWedgeMeshes > 1)
            {
                throw new Exception("--decimatewedgemeshes is not implemented for this command");
            }

            if (!base.ParseArgumentsAndLoadCaches(outDir))
            {
                return false; //help
            }

            if (!tcopts.NoOrbital && !SiteDrive.IsSiteDriveString(meshFrame))
            {
                pipeline.LogInfo("mesh frame \"{0}\" is not a site drive, disabling orbital", meshFrame);
                tcopts.NoOrbital = true;
            }

            backprojectDebugDir = Path.Combine(localOutputPath, "Backproject");

            //some workflows do not load observations, for example tiling an M2020 tactical mesh
            if (observationCache != null)
            {
                if (!tcopts.NoOrbital)
                {
                    bool ok = LoadOrbitalTexture();
                    if (!ok && DisableOrbitalIfNoOrbitalTexture())
                    {
                        tcopts.NoOrbital = true;
                    }
                    if (tcopts.NoOrbital && tcopts.NoSurface)
                    {
                        throw new Exception("--nosurface but failed to load orbital");
                    }
                }
            }

            return true;
        }

        protected override bool ObservationFilter(RoverObservation obs)
        {
            return obs.UseForTexturing && (obs.ObservationType == RoverProductType.Image ||
                                           obs.ObservationType == RoverProductType.RoverMask);
        }

        protected override string DescribeObservationFilter()
        {
            return " texturing images and masks";
        }

        protected virtual bool DisableOrbitalIfNoOrbitalTexture()
        {
            return true;
        }

        protected void BuildBlurredObservationImages()
        {
            int no = roverImages.Count;
            int np = 0, nc = 0, nl = 0, nf = 0;
            double lastSpew = UTCTime.Now();
            CoreLimitedParallel.ForEachNoPartition(GetRoverImagesInNextIterationOrder(), obs =>
            {
                if (!tcopts.RedoBlurredObservationTextures && obs.BlurredGuid != Guid.Empty)
                {
#if DBG_BLURRED
                    if (tcopts.WriteDebug)
                    {
                        SaveDebugWedgeImage
                            (pipeline.GetDataProduct<PngDataProduct>(project, obs.BlurredGuid, noCache: true).Image,
                             obs, "_blurred");
                    }

#endif
                    Interlocked.Increment(ref nc);
                    Interlocked.Increment(ref nl);
                    return;
                }
                
                Interlocked.Increment(ref np);

                double now = UTCTime.Now();
                if (!tcopts.NoProgress && (pipeline.Verbose || ((now - lastSpew) > SPEW_INTERVAL_SEC)))
                {
                    pipeline.LogInfo("computing blurred image for observation {0}, processing {1} in parallel, " +
                                     "completed {2}/{3}, {4} cached, {5} failed", obs.Name, np, nc, no, nl, nf);
                    lastSpew = now;
                }

                try
                {
                    Image img = null;
                    if (tcopts.TextureVariant == TextureVariant.Stretched && obs.StretchedGuid != Guid.Empty)
                    {
                        img = pipeline.GetDataProduct<PngDataProduct>(project, obs.StretchedGuid, noCache: true).Image;
                    }
                    else
                    {
                        img = pipeline.LoadImage(obs.Url);
                        if (obs.MaskGuid != Guid.Empty)
                        {
                            var mask =
                                pipeline.GetDataProduct<PngDataProduct>(project, obs.MaskGuid, noCache: true).Image;
                            img = new Image(img); //don't mutate cached image
                            img.UnionMask(mask, new float[] { 0 }); //0 means bad, 1 means good
                        }
                    }

                    //notes from TerrainTools PDSImageRoutines.cs
                    //"Used to do a guass blur 4 with photoshop"
                    //the current code is: img.SmoothBlur(13, 13)
                    Image blurredImage = img.GaussianBoxBlur(tcopts.ObservationBlurRadius);
                    
#if DBG_BLURRED
                    if (tcopts.WriteDebug)
                    {
                        SaveDebugWedgeImage(blurredImage, obs, "_blurred");
                    }
#endif
                    
                    if (!tcopts.NoSave)
                    {
                        var imgProd = new PngDataProduct(blurredImage);
                        pipeline.SaveDataProduct(project, imgProd, noCache: true);
                        obs.BlurredGuid = imgProd.Guid;
                        obs.Save(pipeline);
                    }
                    
                    Interlocked.Increment(ref nc);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref nf);
                    pipeline.LogException(ex, $"error creating blurred image for observation {obs.Name}");
                }

                Interlocked.Decrement(ref np);
            });
            pipeline.LogInfo("built blurred images for {0} observations, {1} cached, {2} failed", nc - nl, nl, nf);
        }

        protected void BuildBlendedObservationImages(Image blendedTexture, Image backprojectIndex = null,
                                                     TextureVariant textureVariant = TextureVariant.Blended,
                                                     bool forceRedo = false, double preadjustLuminance = 0)
        {
            backprojectIndex = backprojectIndex ?? this.backprojectIndex;

            pipeline.LogInfo("building blended observation images");

            //TODO: when a source observation has higher effective resolution than the scene texture
            //really there is a whole neighborhood of source pixels that should contribute to each texel
            //(minification).  We don't handle that properly yet.

            pipeline.LogInfo("collecting blended backproject results");

            //obs index => (obsCol, obsRow) => (sumBlendedR, sumBlendedG, sumBlendedB, num)
            var winners = new Dictionary<int, Dictionary<Vector2, Vector4>>();

            if (blendedTexture.Height != backprojectIndex.Height || blendedTexture.Width != backprojectIndex.Width)
            {
                throw new ArgumentException("backproject index and blended texture must be same size");
            }

            long nw = 0;
            for (int r = 0; r < blendedTexture.Height; r++)
            {
                for (int c = 0; c < blendedTexture.Width; c++)
                {
                    int obsIndex = (int)backprojectIndex[0, r, c];

                    if (obsIndex < Observation.MIN_INDEX)
                    {
                        continue;
                    }

                    nw++;

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

            pipeline.LogInfo("{0} blended backproject pixels in {1} observations", Fmt.KMG(nw), winners.Count);

            CheckGarbage(immediate: true);

            void writeDebug(Image img, Observation obs, string suffix, int markWinnersForObs = -1)
            {
                if (tcopts.WriteDebug)
                {
                    if (markWinnersForObs >= Observation.MIN_INDEX)
                    {
                        img = new Image(img);
                        if (img.Bands < 3)
                        {
                            float[] intensity = img.GetBandData(0);
                            while (img.Bands < 3 )
                            {
                                Array.Copy(intensity, img.GetBandData(img.AddBand()), intensity.Length);
                            }
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

            double luminanceRange = Colorspace.GetLuminanceRange(); //typically defined as 100

            double colorizeHue = tcopts.Colorize ? medianHue : -1;

            double lumaMed = -1, lumaMAD = -1;
            if (preadjustLuminance > 0)
            {
                Backproject.GetImageStats(pipeline, project,
                                          winners.Keys.Select(idx => observationCache.GetObservation(idx)),
                                          out lumaMed, out lumaMAD, out double hueMed);
            }

            int no = indexedImages.Count;
            pipeline.LogInfo("creating {0} images for {1} observations{2}{3}",
                             textureVariant, no, colorizeHue >= 0 ? ", colorizing monochrome images" : "",
                             preadjustLuminance > 0 ? (", preadjust luminance " + preadjustLuminance) : "");
            pipeline.LogInfo("barycentric interp: {0}, inpaint diff: {1}, blur diff: {2}, fill avg: {3}",
                             tcopts.BarycentricInterpolateWinners, tcopts.InpaintDiff, tcopts.BlurDiff,
                             !tcopts.NoFillBlendWithAverageDiff);
            if (tcopts.BarycentricInterpolateWinners &&
                tcopts.BarycentricInterpolateMaxTriangleSideLengthPixels > 0)
            {
                pipeline.LogInfo("barycentric interpolate max triangle side {0}px",
                                 tcopts.BarycentricInterpolateMaxTriangleSideLengthPixels);
            }

            //TODO blend orbital

            int np = 0, nc = 0, nf = 0;
            double lastSpew = UTCTime.Now();
            CoreLimitedParallel.ForEach(GetRoverImagesInNextIterationOrder(), obs =>
            {
                if (!(tcopts.RedoBlendedObservationTextures || forceRedo) &&
                    obs.GetTextureVariantGuid(textureVariant) != Guid.Empty)
                {
                    if (tcopts.WriteDebug)
                    {
                        writeDebug(pipeline.LoadImage(obs.Url, noCache: true), obs, "");
                        writeDebug(pipeline.GetDataProduct<PngDataProduct>
                                   (project, obs.GetTextureVariantGuid(textureVariant), noCache: true).Image,
                                   obs, "_blended");
                    }
                    Interlocked.Increment(ref nc);
                    return;
                }

                if (!winners.ContainsKey(obs.Index))
                {
                    pipeline.LogVerbose("cannot blend image for observation {0}, no points backprojected to it",
                                        obs.Name);
                    Interlocked.Increment(ref nf);
                    if (!tcopts.NoSave)
                    {
                        obs.SetTextureVariantGuid(textureVariant, Guid.Empty);
                        obs.Save(pipeline);
                    }
                    Interlocked.Increment(ref nc);
                    return;
                }

                if (obs.Bands != 3 && obs.Bands != 1)
                {
                    pipeline.LogWarn("blending observation image {0} with {1} bands not supported",
                                     obs.Name, obs.Bands);
                    if (!tcopts.NoSave)
                    {
                        obs.SetTextureVariantGuid(textureVariant, Guid.Empty);
                        obs.Save(pipeline);
                    }
                    Interlocked.Increment(ref nc);
                    return;
                }

                Interlocked.Increment(ref np);

                double now = UTCTime.Now();
                if (!tcopts.NoProgress && (pipeline.Verbose || ((now - lastSpew) > SPEW_INTERVAL_SEC)))
                {
                    pipeline.LogInfo("blending image for observation {0}, processing {1} in parallel, " +
                                     "completed {2}/{3}", obs.Name, np, nc, no);
                    lastSpew = now;
                }

                CheckGarbage(); //this is a memory pinch point

                Image img = pipeline.LoadImage(obs.Url, noCache: true);
                writeDebug(img, obs, "");

                Image blr = pipeline.GetDataProduct<PngDataProduct>(project, obs.BlurredGuid, noCache: true).Image;

                if (preadjustLuminance > 0 && lumaMed >= 0 && obs.StatsGuid != Guid.Empty)
                {
                    var st = pipeline.GetDataProduct<ImageStats>(project, obs.StatsGuid, noCache: true);
                    img = new Image(img); //don't mutate cached image
                    img.AdjustLuminanceDistribution(st.LuminanceMedian, st.LuminanceMedianAbsoluteDeviation,
                                                    lumaMed, lumaMAD, preadjustLuminance);
                    blr = new Image(blr); //don't mutate cached image
                    blr.AdjustLuminanceDistribution(st.LuminanceMedian, st.LuminanceMedianAbsoluteDeviation,
                                                    lumaMed, lumaMAD, preadjustLuminance);
                }

                if (colorizeHue >= 0 && img.Bands == 1)
                {
                    img = img.ColorizeScalarImage(colorizeHue);
                    blr = blr.ColorizeScalarImage(colorizeHue);
//                    writeDebug(img, obs, "_colorize");
//                    writeDebug(blr, obs, "_colorize_blur");
                }

                var diffImage = new Image(img.Bands, img.Width, img.Height);
                diffImage.CreateMask(true); //all pixels initially masked

                var avgDiff = new float[img.Bands];
                int numWinners = 0;
                foreach (var winner in winners[obs.Index])
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
                    if (img.Bands == 3)
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
                        luminance /= luminanceRange; //[0,100] => [0,1]
                        diff = new float[] { (float)luminance - blr[0, or, oc] };
                    }

                    for (int i = 0; i < img.Bands; i++)
                    {
                        avgDiff[i] += diff[i];
                    }

                    diffImage.SetBandValues(or, oc, diff);
                    diffImage.SetMaskValue(or, oc, false);

                    numWinners++;
                }

                if (numWinners > 0)
                {
                    for (int i = 0; i < img.Bands; i++)
                    {
                        avgDiff[i] /= numWinners;
                    }

                    if (tcopts.BarycentricInterpolateWinners && numWinners >= 3)
                    {
                        Func<Mesh, Face, bool> filter = null;
                        double ms = tcopts.BarycentricInterpolateMaxTriangleSideLengthPixels;
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

                    if (tcopts.InpaintDiff != 0)
                    {
                        diffImage.Inpaint(tcopts.InpaintDiff);
                    }

                    if (tcopts.BlurDiff > 0)
                    {
                        diffImage.GaussianBoxBlur(tcopts.BlurDiff, blendMasked: true);
                    }

#if DBG_DIFF
                    writeDebug(diffImage, obs, "_diff");
#endif

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
                                else if (!tcopts.NoFillBlendWithAverageDiff)
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
                    writeDebug(blendedImage, obs, "_blended_winners", obs.Index);

                    if (!tcopts.NoSave)
                    {
                        var imgProd = new PngDataProduct(blendedImage);
                        pipeline.SaveDataProduct(project, imgProd, noCache: true);
                        obs.SetTextureVariantGuid(textureVariant, imgProd.Guid);
                        obs.Save(pipeline);
                    }
                }
                else
                {
                    pipeline.LogWarn("cannot blend image for observation {0}, no valid backprojections", obs.Name);
                    Interlocked.Increment(ref nf);
                    if (!tcopts.NoSave)
                    {
                        obs.SetTextureVariantGuid(textureVariant, Guid.Empty);
                        obs.Save(pipeline);
                    }
                }

                Interlocked.Decrement(ref np);
                Interlocked.Increment(ref nc);
            });

            pipeline.LogInfo("created blended images for {0}/{1} observations, skipped {2} with no backprojections",
                             nc, no, nf);
        }

        protected void BuildBlendedLeafTextures(string leafFolder,
                                                TextureVariant textureVariant = TextureVariant.Blended)
        {
            double colorizeHue = tcopts.Colorize ? medianHue : -1;

            pipeline.LogInfo("replacing leaf textures in {0} with {1} variant",
                             pipeline.GetStorageUrl(leafFolder, project.Name), textureVariant);

            int curLeafNum = 0, leafCount = tileList.LeafNames.Count;
            int numSurfacePixels = 0, numOrbitalPixels = 0, numMissingPixels = 0, numFallbacks = 0;
            double lastSpew = UTCTime.Now();

            void blendLeaf(string leaf)
            {
                Interlocked.Increment(ref curLeafNum);
                double now = UTCTime.Now();
                if (!tcopts.NoProgress && (pipeline.Verbose || ((now - lastSpew) > SPEW_INTERVAL_SEC)))
                {
                    pipeline.LogInfo("building {0} leaf texture {1}/{2} ({3:F2}%): {4}",
                                     textureVariant, curLeafNum, leafCount, 100 * curLeafNum / (float)leafCount, leaf);
                    lastSpew = now;
                }
                CheckGarbage(); //memory pinch point
                string indexName = leaf + TilingDefaults.INDEX_FILE_SUFFIX + TilingDefaults.INDEX_FILE_EXT;
                string indexUrl = pipeline.GetStorageUrl(leafFolder, project.Name, indexName);
                var index = pipeline.LoadImage(indexUrl, noCache: true);
                var results = Backproject.BuildResultsFromIndex(index, indexedImages, msg => pipeline.LogWarn(msg));
                var texture = new Image(3, index.Width, index.Height);
                var stats = Backproject.FillOutputTexture(pipeline, project, results, texture, textureVariant,
                                                          tcopts.BackprojectInpaintMissing,
                                                          tcopts.BackprojectInpaintGutter, fallbackToOriginal: true,
                                                          orbitalTexture: orbitalTexture, colorizeHue: colorizeHue);
                Interlocked.Add(ref numSurfacePixels, stats.BackprojectedSurfacePixels);
                Interlocked.Add(ref numOrbitalPixels, stats.BackprojectedOrbitalPixels);
                Interlocked.Add(ref numMissingPixels, stats.BackprojectMissingPixels);
                Interlocked.Add(ref numFallbacks, stats.NumFallbacks);
                TemporaryFile.GetAndDelete(tileList.ImageExt, tmpFile =>
                {
                    texture.Save<byte>(tmpFile);
                    string textureUrl = pipeline.GetStorageUrl(leafFolder, project.Name, leaf + tileList.ImageExt);
                    if (pipeline.FileExists(textureUrl))
                    {
                        pipeline.LogInfo("overwriting {0} with {1} variant", textureUrl, textureVariant);
                        string unblendedUrl = pipeline.GetStorageUrl(leafFolder, project.Name,
                                                                     leaf + "_unblended" + tileList.ImageExt);
                        if (tcopts.WriteDebug && !File.Exists(unblendedUrl))
                        {
                            pipeline.LogInfo("saving backup of {0} to {1}", textureUrl, unblendedUrl);
                            pipeline.GetFile(textureUrl, tmp => pipeline.SaveFile(tmp, unblendedUrl));
                        }
                    }
                    pipeline.SaveFile(tmpFile, textureUrl);
                });
            }

            //order access to improve cache coherence
            //(either forward or reverse ordering would group leaves spatially
            //but reverse order builds deepest leaves first, which might be a little better)
            var leaves = tileList.LeafNames.OrderByDescending(name => name).ToList();

            if (!tcopts.NoBlendLeavesInParallel)
            {
                CoreLimitedParallel.ForEach(leaves, blendLeaf);
            }
            else
            {
                pipeline.LogInfo("blending leaves serially");
                Serial.ForEach(leaves, blendLeaf);
            }

            pipeline.LogInfo("built {0} leaf textures using {1} surface pixels, {2} orbital pixels, " +
                             "{3} missing pixels, {4} fallbacks to original observation textures",
                             textureVariant, Fmt.KMG(numSurfacePixels), Fmt.KMG(numOrbitalPixels),
                             Fmt.KMG(numMissingPixels), numFallbacks);
        }

        public Image BlendImage(Image blurred, Image backprojectIndex = null,
                                LimberDMG.EdgeBehavior edgeMode = LimberDMG.DEF_EDGE_BEHAVIOR)
        {
            backprojectIndex = backprojectIndex ?? this.backprojectIndex;

            int width = backprojectIndex.Width, height = backprojectIndex.Height;

            if (blurred.Height != height || blurred.Width != width)
            {
                throw new ArgumentException("backproject index and blurred texture must be same size");
            }

            pipeline.LogInfo("stitching {0}x{1} image with LimberDMG, residual epsilon {2}, {3} relaxation steps, " +
                             "{4} multigrid iterations, lambda {5}",
                             width, height, tcopts.ResidualEpsilon, tcopts.NumRelaxationSteps,
                             tcopts.NumMultigridIterations, tcopts.BlendLambda);

            Image index = new Image(1, width, height);
            Image flags = new Image(3, width, height);
            for (int r = 0; r < height; r++)
            {
                for (int c = 0; c < width; c++)
                {
                    int obsIndex = (int)backprojectIndex[0, r, c];

                    index[0, r, c] = obsIndex;

                    var obs = indexedImages.ContainsKey(obsIndex) ? indexedImages[obsIndex] : null;

                    if (obs != null && obs.IsOrbitalDEM)
                    {
                        obs = null; //no, this shouldn't happen...
                    }

                    bool hasGray = obs != null;
                    bool hasColor = obs != null && (tcopts.Colorize || obs.Bands == 3);
                    bool orbital = obs != null && obs.IsOrbitalImage;

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

            var dmg = new LimberDMG(tcopts.ResidualEpsilon, tcopts.NumRelaxationSteps, tcopts.NumMultigridIterations,
                                    tcopts.BlendLambda, edgeMode, LimberDMG.ColorConversion.RGBToLAB,
                                    msg => pipeline.LogVerbose(msg));

            if (pipeline.Verbose)
            {
                blurred.DumpStats(msg => pipeline.LogInfo("initial image: " + msg));
            }

            var blended = dmg.StitchImage(blurred, index, flags);

            if (pipeline.Verbose)
            {
                blended.DumpStats(msg => pipeline.LogInfo("blended image: " + msg));
            }

            return blended;
        }

        protected virtual void LoadInputMesh(bool requireUVs = false, bool requireNormals = false)
        {
            if (sceneMesh == null && project != null) //might have already been loaded in GetProject()
            {
                sceneMesh = SceneMesh.Find(pipeline, project.Name, MeshVariant.Default);
            }

            if (!string.IsNullOrEmpty(tcopts.InputMesh))
            {
                pipeline.LogInfo("loading input mesh from {0}{1}", tcopts.InputMesh,
                                 sceneMesh != null ? ", overriding scene mesh " : "");
                string meshFile = pipeline.GetFileCached(tcopts.InputMesh, "meshes");
                if (tcopts.LoadLODs)
                {
                    meshLOD = Mesh.LoadAllLODs(meshFile);
                }
                else
                {
                    mesh = Mesh.Load(meshFile);
                }
            }
            else if (sceneMesh != null)
            {
                if (sceneMesh.MeshGuid != Guid.Empty)
                {
                    pipeline.LogInfo("loading scene mesh in frame {0} from database", meshFrame);
                    mesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, sceneMesh.MeshGuid, noCache: true).Mesh;
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

            if (meshLOD == null)
            {
                meshLOD = new List<Mesh>() { mesh };
            }

            foreach (var lodMesh in meshLOD)
            {
                lodMesh.Clean(verbose: msg => pipeline.LogVerbose(msg), warn: msg => pipeline.LogWarn(msg));
            }

            var keepers = new List<Mesh>();
            for (int i = 0; i < meshLOD.Count; i++)
            {
                if (meshLOD[i] == null || meshLOD[i].Faces.Count == 0)
                {
                    pipeline.LogWarn("ignoring empty input mesh at LOD {0}", i);
                }
                else
                {
                    keepers.Add(meshLOD[i]);
                }
            }
            meshLOD = keepers.OrderByDescending(m => m.Faces.Count).ToList();

            if (meshLOD.Count == 0)
            {
                pipeline.LogWarn("input mesh contains 0 non-empty levels of detail");
                meshLOD = new List<Mesh>() { new Mesh(hasNormals: requireNormals, hasUVs: requireUVs) };
            }
            else
            {
                pipeline.LogInfo("input mesh contains {0} non-empty level(s) of detail", meshLOD.Count);
            }

            if (meshTransform.HasValue && meshTransform.Value != Matrix.Identity)
            {
                pipeline.LogInfo("applying non-identity transform to input mesh");
                for (int i = 0; i < meshLOD.Count; i++)
                {
                    meshLOD[i].Transform(meshTransform.Value);
                }
            }

            mesh = meshLOD.First();

            for (int lod = 0; lod < meshLOD.Count; lod++)
            {
                pipeline.LogInfo("LOD {0}: {1} vertices, {2} faces, bounds {3}",
                                 lod, Fmt.KMG(meshLOD[lod].Vertices.Count), Fmt.KMG(meshLOD[lod].Faces.Count),
                                 meshLOD[lod].Bounds().FmtExtent());
            }

            bool canGenUVs = CanAtlasSceneMesh();

            if (tcopts.LoadLODs && !string.IsNullOrEmpty(tcopts.FixupLODs) && (tcopts.FixupLODs.ToLower() != "null") &&
                (!requireUVs || canGenUVs))
            {
                int[][] ranges = null;
                try
                {
                    ranges = tcopts.FixupLODs.Split(',')
                        .Select(r => r.Split('-').Select(c => int.Parse(c)).ToArray())
                        .ToArray();
                    if (ranges.Length < 1)
                    {
                        throw new Exception("no triangle ranges");
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception("error parsing --fixuplods \"" + tcopts.FixupLODs + "\"", ex);
                }

                FixupLODs(ranges);
            }

            //if texture projection is enabled vertices outside the camera frustum and backfaces can be discarded
            //even if we're not actually requiring UVs
            if (canGenUVs && tcopts.AtlasMode == AtlasMode.Project)
            {
                int lodCountWas = meshLOD.Count;
                for (int i = 0; i < meshLOD.Count; i++)
                {
                    bool applyUVs = requireUVs && !meshLOD[i].HasUVs;
                    meshLOD[i].ProjectTexture(sceneTexture, meshToCamera.Value, applyUVs: applyUVs,
                                              verbose: msg => pipeline.LogVerbose($"LOD {i}:  {msg}"));
                    if (applyUVs)
                    {
                        numProjectAtlas++;
                    }
                }

                meshLOD = meshLOD
                    .Where(m => m != null && m.Faces.Count > 0)
                    .OrderByDescending(m => m.Faces.Count)
                    .ToList();
                
                if (meshLOD.Count == 0)
                {
                    pipeline.LogWarn("0 non-empty levels of detail after texture projection");
                    meshLOD = new List<Mesh>() { new Mesh(hasNormals: requireNormals, hasUVs: requireUVs) };
                }
                else if (meshLOD.Count < lodCountWas)
                {
                    pipeline.LogWarn("discarded {0} empty LODs after texture projection",
                                     (lodCountWas - meshLOD.Count));
                }

                mesh = meshLOD.First();
            }

            if (requireUVs)
            {
                int lodCountWas = meshLOD.Count;
                for (int i = 0; i < meshLOD.Count; i++)
                {
                    if (!meshLOD[i].HasUVs)
                    {
                        if (canGenUVs)
                        {
                            AtlasMesh(meshLOD[i], sceneTextureResolution, "LOD " + i);
                        }
                        else
                        {
                            throw new Exception("atlassing disabled and mesh missing UVs" +
                                                (i > 0 ? $" at LOD {i}" : ""));
                        }
                    }
                }

                meshLOD = meshLOD
                    .Where(m => m != null && m.Faces.Count > 0)
                    .OrderByDescending(m => m.Faces.Count)
                    .ToList();
                
                if (meshLOD.Count == 0)
                {
                    pipeline.LogWarn("0 non-empty levels of detail after atlassing");
                    meshLOD = new List<Mesh>() { new Mesh(hasNormals: requireNormals, hasUVs: requireUVs) };
                }
                else if (meshLOD.Count < lodCountWas)
                {
                    pipeline.LogWarn("discarded {0} empty levels of detail after atlassing",
                                     (lodCountWas - meshLOD.Count));
                }

                mesh = meshLOD.First();
            }

            if (requireNormals)
            {
                for (int i = 0; i < meshLOD.Count; i++)
                {
                    if (!meshLOD[i].HasNormals)
                    {
                        meshLOD[i].GenerateVertexNormals();
                    }
                }
            }

            pipeline.LogInfo("loaded {0} LODs:", meshLOD.Count);
            for (int lod = 0; lod < meshLOD.Count; lod++)
            {
                pipeline.LogInfo("LOD {0}: {1} vertices, {2} faces",
                                 lod, Fmt.KMG(meshLOD[lod].Vertices.Count), Fmt.KMG(meshLOD[lod].Faces.Count));
            }
        }

        protected void FixupLODs(int[][] ranges)
        {
            var newLODs = new Mesh[ranges.Length];
            var used = new bool[meshLOD.Count];
            for (int i = 0; i < ranges.Length; i++)
            {
                int s = -1;
                for (int j = 0; j < meshLOD.Count; j++)
                {
                    int fc = meshLOD[j].Faces.Count; 
                    if (!used[j] && (ranges[i][0] <= fc && fc <= ranges[i][1]))
                    {
                        s = j;
                        break;
                    }
                }
                if (s >= 0)
                {
                    used[s] = true;
                    Mesh src = meshLOD[s];
                    pipeline.LogInfo("using source LOD {0} with {1} tris for fixed up LOD {2} ({3}-{4})",
                                     s, Fmt.KMG(src.Faces.Count), i, Fmt.KMG(ranges[i][0]), Fmt.KMG(ranges[i][1]));
                    newLODs[i] = src;
                }
                else
                {
                    int target = (int)Math.Round(0.5 * (ranges[i][0] + ranges[i][1]));
                    s = meshLOD.FindLastIndex(m => m.Faces.Count > ranges[i][1]);
                    string st = "source";
                    Mesh src = s >= 0 ? meshLOD[s] : null;
                    if (s < 0 || meshLOD[s].Faces.Count > 2 * target)
                    {
                        int fs = newLODs.ToList().FindLastIndex(m => (m != null && m.Faces.Count >= target));
                        if (fs >= 0)
                        {
                            s = fs;
                            st = "fixed up";
                            src = newLODs[s];
                        }
                    }
                    if (src != null)
                    {
                        if (src.Faces.Count - target > 10000)
                        {
                            pipeline.LogInfo("decimating {0} LOD {1} from {2} to {3} triangles for fixed up lod {4}",
                                             st, s, Fmt.KMG(src.Faces.Count), Fmt.KMG(target), i);
                        }
                        newLODs[i] = src.Decimated(target, tcopts.MeshDecimator, logger: pipeline);
                        pipeline.LogInfo("decimated {0} tri {1} LOD {2} for fixed up LOD {3} ({4}-{5}) " +
                                         "to {6} (target {7}) tris with {8}", Fmt.KMG(src.Faces.Count), st, s, i,
                                         Fmt.KMG(ranges[i][0]), Fmt.KMG(ranges[i][1]),
                                         Fmt.KMG(newLODs[i].Faces.Count), Fmt.KMG(target), tcopts.MeshDecimator);
                        if (newLODs[i].Faces.Count < ranges[i][0] || newLODs[i].Faces.Count > ranges[i][1])
                        {
                            pipeline.LogWarn("not using fixed up LOD {0}, face count {1} out of range {2}-{3}",
                                             i, Fmt.KMG(newLODs[i].Faces.Count), Fmt.KMG(ranges[i][0]),
                                             Fmt.KMG(ranges[i][1]));
                            newLODs[i] = null;
                                             
                        }
                    }
                    else
                    {
                        pipeline.LogInfo("no mesh available for making fixed up LOD {0} with {1}-{2} tris",
                                         i, Fmt.KMG(ranges[i][0]), Fmt.KMG(ranges[i][1]));
                    }
                }
            }

            newLODs = newLODs
                .Where(m => m != null && m.Faces.Count > 0)
                .OrderByDescending(m => m.Faces.Count)
                .ToArray();

            if (newLODs.Length > 0)
            {
                meshLOD = newLODs.ToList();
                mesh = meshLOD.First();
                pipeline.LogInfo("{0} LODs after fixup:", meshLOD.Count);
                for (int lod = 0; lod < meshLOD.Count; lod++)
                {
                    pipeline.LogInfo("LOD {0}: {1} vertices, {2} faces",
                                     lod, Fmt.KMG(meshLOD[lod].Vertices.Count), Fmt.KMG(meshLOD[lod].Faces.Count));
                }
            }
            else
            {
                pipeline.LogWarn("LOD fixup failed, using original {0} LODs", meshLOD.Count);
            }
        }

        //BuildTilingInput.SetupTextureProjection() is the only place that meshToCamera can get set
        //to actually enable texture projection
        protected bool TextureProjectionEnabled()
        {
            return !tcopts.NoTextureProjection &&
                sceneTexture != null && sceneTexture.CameraModel != null && meshToCamera.HasValue;
        }

        protected void ProjectTexture(Mesh mesh, string name = null)
        {
            if (sceneTexture == null)
            {
                throw new Exception("cannot project texture coordinates, no scene texture");
            }
            if (sceneTexture.CameraModel == null)
            {
                throw new Exception("cannot project texture coordinates, scene texture has no camera model");
            }
            if (!meshToCamera.HasValue)
            {
                throw new Exception("cannot project texture coordinates, no mesh-to-image transform");
            }
            int vertsWas = mesh.Vertices.Count;
            mesh.ProjectTexture(sceneTexture, meshToCamera.Value, verbose: msg => pipeline.LogVerbose(msg));
            if (mesh.Vertices.Count == 0)
            {
                pipeline.LogWarn("all {0} verts of {1}mesh outside camera frustum and removed by texture projection",
                                 Fmt.KMG(vertsWas), !string.IsNullOrEmpty(name) ? (name + " ") : "");
            }
        }

        protected override void AtlasMesh(Mesh mesh, int resolution, string name = null)
        {
            if (mesh.Vertices.Count == 0)
            {
                pipeline.LogInfo("cannot atlas {0}mesh, no vertices", !string.IsNullOrEmpty(name) ? (name + " ") : "");
                return;
            }
            if (tcopts.AtlasMode == AtlasMode.Project && TextureProjectionEnabled())
            {
                string msg = string.Format("atlassing {0}mesh ({1} triangles) with texture projection",
                                           !string.IsNullOrEmpty(name) ? (name + " ") : "", Fmt.KMG(mesh.Faces.Count));
                if (mesh.Faces.Count > ATLAS_LOG_THRESHOLD)
                {
                    pipeline.LogInfo(msg);
                }
                else
                {
                    pipeline.LogVerbose(msg);
                }
                ProjectTexture(mesh, name);
                numProjectAtlas++;
            }
            else if (tcopts.AtlasMode != AtlasMode.Project)
            {
                base.AtlasMesh(mesh, resolution, name);
            }
            else
            {
                throw new Exception($"cannot atlas {name}mesh, texture projection not available");
            }
        }

        protected override void DumpAtlasStats()
        {
            if (numProjectAtlas > 0)
            {
                pipeline.LogInfo("projection atlassed {0} meshes", numProjectAtlas);
            }
            base.DumpAtlasStats();
        }

        protected virtual bool CanAtlasSceneMesh()
        {
            if (tcopts.AtlasMode == AtlasMode.None)
            {
                return false;
            }
            if (tcopts.AtlasMode == AtlasMode.Project && !TextureProjectionEnabled())
            {
                return false;
            }
            return true;
        }

        protected virtual void LoadTileList()
        {
            if (sceneMesh == null)
            {
                throw new Exception("no scene mesh");
            }

            if (sceneMesh.TileListGuid == Guid.Empty)
            {
                throw new Exception(string.Format("scene mesh has no tile list"));
            }

            tileList = pipeline.GetDataProduct<TileList>(project, sceneMesh.TileListGuid, noCache: true);

            if (tileList.LeafNames == null || tileList.LeafNames.Count == 0)
            {
                pipeline.LogWarn("leaf list empty");
            }
        }

        protected void BuildSceneCaster()
        {
            Mesh occlusionMesh = null;
            if (!string.IsNullOrEmpty(tcopts.OcclusionMesh))
            {
                pipeline.LogInfo("loading occlusion mesh {0}", tcopts.OcclusionMesh);
                occlusionMesh = Mesh.Load(pipeline.GetFileCached(tcopts.OcclusionMesh, "meshes"));
            }
            else
            {
                occlusionMesh = mesh;
            }

            if (occlusionMesh == null || occlusionMesh.Faces.Count == 0)
            {
                pipeline.LogWarn("cannot create scene caster, occlusion mesh empty");
                return;
            }

            sceneCaster = new SceneCaster(occlusionMesh); //NOTE: can't change mesh after this
        }

        protected void BuildMeshOperator()
        {
            var meshOps = new MeshOperator[meshLOD.Count];
            CoreLimitedParallel.For(0, meshLOD.Count, lod =>
            {
                meshOps[lod] = new MeshOperator(meshLOD[lod], buildFaceTree: true,
                                                buildVertexTree: !meshLOD[lod].HasFaces, buildUVFaceTree: false);
            });
            meshOpForLOD = meshOps.ToList();
            meshOp = meshOpForLOD.First();
        }

        protected virtual void InitBackprojectStrategy()
        {
            InitBackprojectStrategy(mesh, meshOp, sceneCaster, sceneCaster);
        }

        protected void InitBackprojectStrategy(Mesh mesh, MeshOperator meshOp, SceneCaster meshCaster,
                                               SceneCaster occlusionScene, bool useSurfaceBounds = true)
        {
            if (meshOp == null)
            {
                pipeline.LogWarn("cannot create backproject strategy, no mesh operator");
                return;
            }
            if (meshCaster == null)
            {
                pipeline.LogWarn("cannot create backproject strategy, no mesh caster");
                return;
            }

            backprojectStrategy = ObsSelectionStrategy.Create(tcopts.ObsSelectionStrategy);

            backprojectStrategy.Quality = tcopts.BackprojectQuality;
            backprojectStrategy.PreferColor = tcopts.PreferColor;
            backprojectStrategy.RaycastTolerance = tcopts.RaycastTolerance;
            backprojectStrategy.PreferNonlinear = !mission.PreferLinearRasterProducts();
            backprojectStrategy.DebugOutputPath = tcopts.WriteBackprojectDebug ? backprojectDebugDir : null;
            backprojectStrategy.Logger = pipeline;

            int numOrbital = 0;
            if (!tcopts.NoOrbital && observationCache.ContainsObservation(Observation.ORBITAL_IMAGE_INDEX))
            {
                var texObs = observationCache.GetObservation(Observation.ORBITAL_IMAGE_INDEX);
                backprojectStrategy.OrbitalMetersPerPixel =
                    (texObs.CameraModel as ConformalCameraModel).AvgMetersPerPixel;
                backprojectStrategy.OrbitalIsColor = texObs.Bands > 1;
                if (useSurfaceBounds && sceneMesh != null)
                {
                    backprojectStrategy.SurfaceExtent = sceneMesh.SurfaceExtent;
                }
                numOrbital = 1;
            }

            pipeline.LogInfo("initializing observation selection strategy {0} for {1} rover observations, {2} orbital",
                             tcopts.ObsSelectionStrategy, roverImages.Count, numOrbital);

            var contexts = Backproject.BuildContexts(obsToHull, roverImages, //doesn't load images, don't toggle order
                                                     mission, frameCache, observationCache, meshFrame, tcopts.UsePriors,
                                                     tcopts.OnlyAligned, msg => pipeline.LogWarn(msg));

            backprojectStrategy.Initialize(mesh, meshOp, meshCaster, occlusionScene, contexts);
        }

        protected void BackprojectObservations()
        {
            backprojectResults = BackprojectObservations(mesh, sceneTextureResolution, sceneCaster, sceneCaster,
                                                         out Backproject.Stats stats);
        }

        protected IDictionary<Pixel, Backproject.ObsPixel>
            BackprojectObservations(Mesh mesh, int resolution, SceneCaster meshCaster, SceneCaster occlusionScene,
                                    out Backproject.Stats stats, ObsSelectionStrategy strategy = null,
                                    string meshName = "", bool quiet = false)
        {
            string forMesh = !string.IsNullOrEmpty(meshName) ? $" for mesh {meshName}" : "";

            if (mesh.Vertices.Count < 3 || mesh.Faces.Count < 1)
            {
                throw new Exception($"cannot backproject: no triangles{forMesh}");
            }

            strategy = strategy ?? backprojectStrategy;
            if (strategy == null)
            {
                throw new Exception($"must initialize backproject strategy before backprojecting{forMesh}");
            }

            var opts = new Backproject.Options()
            {
                pipeline = pipeline,

                project = project,
                mission = mission,

                frameCache = frameCache,
                observationCache = observationCache,

                obsToHull = obsToHull,

                mesh = mesh,
                meshOp = new MeshOperator(mesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true),
                meshFrame = meshFrame,

                meshCaster = meshCaster,
                occlusionScene = occlusionScene,

                usePriors = tcopts.UsePriors,
                onlyAligned = tcopts.OnlyAligned,

                writeDebug = tcopts.WriteBackprojectDebug,
                localDebugOutputPath = Path.Combine(backprojectDebugDir, meshName), //ignores empty strings

                outputResolution = resolution,

                quality = tcopts.BackprojectQuality,
                obsSelectionStrategy = strategy,

                raycastTolerance = tcopts.RaycastTolerance,
                maxGlancingAngleDegrees = tcopts.MaxGlancingAngleDegrees,

                meshName = meshName,
                quiet = quiet,
                verbose = tcopts.VerboseBackproject
            };

            try
            {
                opts.meshHull = ConvexHull.Create(mesh);
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    pipeline.LogWarn("failed to make convex hull{0}: {1}", forMesh, ex.Message);
                }
            }

            if (!tcopts.NoOrbital)
            {
                var meshToRoot = frameCache.GetBestTransform(meshFrame).Transform.Mean;
                opts.meshToOrbital = meshToRoot * Matrix.Invert(orbitalTextureToRoot);
                mission.GetLocalLevelBasis(out Vector3 north, out Vector3 east, out Vector3 nadir);
                opts.skyDirInMesh = -nadir;
            }

            opts = CustomizeBackprojectOptions(opts);

            if (!quiet)
            {
                pipeline.LogInfo("backprojecting {0} observations{1}, resolution {2}, quality {3}, prefer color {4}, " +
                                 "texture far clip {5:f3}",
                                 imageObservations.Count, forMesh, resolution, tcopts.BackprojectQuality,
                                 tcopts.PreferColor, tcopts.TextureFarClip);
            }

            var results = Backproject.BackprojectObservations(opts, imageObservations, out stats);

            if (!quiet)
            {
                pipeline.LogInfo("backprojected {0} pixels from surface{1}, {2} from orbital, {3} failed, " +
                                 "tried up to {4} observations per pixel",
                                 Fmt.KMG(stats.BackprojectedSurfacePixels), forMesh,
                                 Fmt.KMG(stats.BackprojectedOrbitalPixels), Fmt.KMG(stats.BackprojectMissingPixels),
                                 stats.NumFallbacks + 1);
            }

            return results;
        }

        protected virtual Backproject.Options CustomizeBackprojectOptions(Backproject.Options opts)
        {
            return opts;
        }

        protected void BuildBackprojectIndex()
        {
            pipeline.LogInfo("creating backproject index");
            backprojectIndex = new Image(3, sceneTextureResolution, sceneTextureResolution);
            Backproject.FillIndexImage(backprojectResults, backprojectIndex);

            if (!tcopts.NoSave)
            {
                pipeline.LogInfo("saving backproject index");
                var indexProd = new TiffDataProduct(backprojectIndex);
                pipeline.SaveDataProduct(project, indexProd, noCache: true);
                sceneMesh.BackprojectIndexGuid = indexProd.Guid;
                sceneMesh.Save(pipeline);
            }
            
            if (tcopts.WriteDebug)
            {
                SaveBackprojectIndexDebug(backprojectIndex);
            }
        }

        protected Image MaskBackprojectIndex(Image index)
        {
            index.CreateMask();
            for (int r = 0; r < index.Height; r++)
            {
                for (int c = 0; c < index.Width; c++)
                {
                    if (index[0, r, c] < Observation.MIN_INDEX)
                    {
                        index.SetMaskValue(r, c, true);
                    }
                }
            }
            return index;
        }

        protected void MaskBackprojectIndex()
        {
            MaskBackprojectIndex(backprojectIndex);
        }

        protected void BuildBackprojectResultsFromIndex()
        {
            pipeline.LogInfo("building backproject results from index");
            if (backprojectIndex == null)
            {
                var indexGuid = sceneMesh.BackprojectIndexGuid;
                backprojectIndex = pipeline.GetDataProduct<TiffDataProduct>(project, indexGuid, noCache: true).Image;
            }
            backprojectResults =
                Backproject.BuildResultsFromIndex(backprojectIndex, indexedImages, msg => pipeline.LogWarn(msg));
        }

        protected Image BuildBackprojectTexture(TextureVariant srcTextureVariant,
                                                TextureVariant? dstTextureVariant = null,
                                                double preadjustLuminance = 0)
        {
            //careful here, if we already have a full-scene backprojectIndex
            //then the full-scene texture we're going to generate should be the same resolution
            //in most cases the resolution should match tcopts.TextureResolution
            //but in some workflows, such as blend-after-texture with a lower res blend, it may not
            int width = sceneTextureResolution, height = sceneTextureResolution;
            if (backprojectIndex != null)
            {
                width = backprojectIndex.Width;
                height = backprojectIndex.Height;
            }
            
            pipeline.LogInfo("creating {0}x{1} {2} backproject texture from {3} backproject results, inpaint {4}",
                             width, height, srcTextureVariant, Fmt.KMG(backprojectResults.Count),
                             tcopts.BackprojectInpaintMissing);
            pipeline.LogInfo("preadjust luminance: {0:f3}, colorize: {1}", preadjustLuminance, tcopts.Colorize);

            Image texture = new Image(3, width, height);

            var stats = Backproject.FillOutputTexture(pipeline, project, backprojectResults, texture, srcTextureVariant,
                                                      tcopts.BackprojectInpaintMissing, tcopts.BackprojectInpaintGutter,
                                                      orbitalTexture: orbitalTexture,
                                                      preadjustLuminance: preadjustLuminance,
                                                      colorizeHue: tcopts.Colorize ? medianHue : -1);

            pipeline.LogInfo("filled {0} pixels from {1} surface observations, {2} from orbital, {3} failed, " +
                             "{4} fallbacks to original texture",
                             Fmt.KMG(stats.BackprojectedSurfacePixels), srcTextureVariant,
                             Fmt.KMG(stats.BackprojectedOrbitalPixels), Fmt.KMG(stats.BackprojectMissingPixels),
                             stats.NumFallbacks);

            texture.DumpStats(msg => pipeline.LogInfo(msg));

            if (stats.NumFallbacks > 0)
            {
                pipeline.LogWarn("falling back to {0} texture on {1} observations missing {2} texture",
                                 TextureVariant.Original, stats.NumFallbacks, srcTextureVariant);
            }

            if (!dstTextureVariant.HasValue)
            {
                dstTextureVariant = srcTextureVariant;
            }

            if (!tcopts.NoSave)
            {
                pipeline.LogInfo("saving {0} backproject texture", dstTextureVariant.Value);
                var texProd = new PngDataProduct(texture);
                pipeline.SaveDataProduct(project, texProd, noCache: true);
                switch (dstTextureVariant.Value)
                {
                    case TextureVariant.Original: sceneMesh.TextureGuid = texProd.Guid; break;
                    case TextureVariant.Stretched: sceneMesh.StretchedTextureGuid = texProd.Guid; break;
                    case TextureVariant.Blurred: sceneMesh.BlurredTextureGuid = texProd.Guid; break;
                    case TextureVariant.Blended: sceneMesh.BlendedTextureGuid = texProd.Guid; break;
                    default: throw new Exception("unknown texture variant " + dstTextureVariant.Value);
                }
                sceneMesh.Save(pipeline);
            }
            
            if (tcopts.WriteDebug)
            {
                SaveBackprojectTextureDebug(texture, dstTextureVariant.Value);
            }

            return texture;
        }

        protected void SaveBackprojectIndexDebug(Image index, bool withMesh = true, string suffix = "")
        {
            string name = meshFrame + "_backprojectIndex" + suffix;
            SaveFloatTIFF(index, name);
            Image previewImg = Backproject.GenerateIndexPreviewImage(index);
            name = meshFrame + "_backprojectIndexFalseColor" + suffix;
            pipeline.LogInfo("saving backproject index false color debug image");
            SaveImage(previewImg, name);
            if (withMesh && mesh != null)
            {
                pipeline.LogInfo("saving backproject index false color textured debug mesh");
                SaveMesh(mesh, name, name + imageExt);
            }
        }

        protected void SaveBackprojectTextureDebug(Image texture,
                                                   TextureVariant textureVariant = TextureVariant.Original,
                                                   bool withMesh = true, string suffix = "")
        {
            string name = meshFrame + "_backprojectTexture";
            if (textureVariant != TextureVariant.Original)
            {
                name += "_" + textureVariant.ToString();
            }
            name += suffix;
            pipeline.LogInfo("saving backproject {0} texture debug image", textureVariant);
            SaveImage(texture, name);
            if (withMesh && mesh != null)
            {
                pipeline.LogInfo("saving backproject {0} textured debug mesh", textureVariant);
                SaveMesh(mesh, name, name + imageExt);
            }
        }

        protected void SaveDebugWedgeImage(Image img, Observation obs, string suffix)
        {
            int bs = WedgeObservations.AutoDecimate(obs, tcopts.DecimateDebugWedgeImages,
                                                    tcopts.TargetWedgeImageResolution);
            if (bs > 1)
            {
                img = img.Decimated(bs);
            }
            
            SaveImage(img, obs.Name + suffix);
        }

        protected void SaveSceneMesh(string outputMesh, bool withIndex = false)
        {
            SaveSceneMesh(outputMesh, tcopts.TextureVariant, withIndex);
        }

        protected void SaveSceneMesh(string outputMesh, TextureVariant textureVariant, bool withIndex = false)
        {
            var meshURL = CheckOutputURL(outputMesh, project.Name, outputFolder, MeshSerializers.Instance);
            var imgURL = StringHelper.ChangeUrlExtension(meshURL, imageExt);

            if (withIndex)
            {
                var index = backprojectIndex;
                if (index == null && sceneMesh.BackprojectIndexGuid != Guid.Empty)
                {
                    index = pipeline
                        .GetDataProduct<TiffDataProduct>(project, sceneMesh.BackprojectIndexGuid, noCache: true)
                        .Image;
                }
                if (index != null)
                {
                    var ext = ".tif";
                    var indexURL = StringHelper.ChangeUrlExtension(meshURL, ext);
                    pipeline.LogInfo("saving {0}x{1} float tiff backproject index image {2}",
                                     index.Width, index.Height, indexURL);
                    TemporaryFile.GetAndDelete(ext, tmpFile =>
                    {
                        var opts = new GDALTIFFWriteOptions(GDALTIFFWriteOptions.CompressionType.DEFLATE);
                        var serializer = new GDALSerializer(opts);
                        serializer.Write<float>(tmpFile, index);
                        pipeline.SaveFile(tmpFile, indexURL, constrainToStorage: false);
                    });
                }
            }

            var texture = sceneTexture;
            if (texture == null)
            {
                Guid texGuid = Guid.Empty;
                switch (textureVariant)
                {
                    case TextureVariant.Original: texGuid = sceneMesh.TextureGuid; break;
                    case TextureVariant.Stretched: texGuid = sceneMesh.StretchedTextureGuid; break;
                    case TextureVariant.Blurred: texGuid = sceneMesh.BlurredTextureGuid; break;
                    case TextureVariant.Blended: texGuid = sceneMesh.BlendedTextureGuid; break;
                    default: throw new Exception("unknown texture variant " + tcopts.TextureVariant);
                }
                if (texGuid != Guid.Empty)
                {
                    texture = pipeline.GetDataProduct<PngDataProduct>(project, texGuid, noCache: true).Image;
                }
            }

            if (texture != null)
            {
                if (!tcopts.NoConvertLinearRGBToSRGB)
                {
                    pipeline.LogInfo("converting scene texture {0} from linear RGB to sRGB", imgURL);
                    texture = texture.LinearRGBToSRGB();
                }
                else
                {
                    pipeline.LogWarn("not converting scene texture {0} from linear RGB to sRGB ",
                                     "(many image formats assume sRGB)", imgURL);
                }
                pipeline.LogInfo("saving {0}x{1} scene texture {2}", texture.Width, texture.Height, imgURL);
                TemporaryFile.GetAndDelete(imageExt, tmpFile =>
                {
                    texture.Save<byte>(tmpFile);
                    pipeline.SaveFile(tmpFile, imgURL, constrainToStorage: false);
                });
            }

            var mesh = this.mesh;
            if (mesh == null && sceneMesh.MeshGuid != Guid.Empty)
            {
                mesh = pipeline.GetDataProduct<PlyGZDataProduct>(project, sceneMesh.MeshGuid, noCache: true).Mesh;
            }

            if (mesh != null)
            {
                pipeline.LogInfo("saving {0}scene mesh", texture != null ? "textured " : "");
                TemporaryFile.GetAndDelete(StringHelper.GetUrlExtension(meshURL), tmpFile =>
                {
                    string texFile = texture != null ? StringHelper.GetLastUrlPathSegment(imgURL) : null;
                    mesh.Save(tmpFile, texFile);
                    pipeline.SaveFile(tmpFile, meshURL, constrainToStorage: false);
                });
            }
            else
            {
                pipeline.LogWarn("no scene mesh to save");
            }
        }
    }
}
