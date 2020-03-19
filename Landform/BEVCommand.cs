using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using CommandLine;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Alignment;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;

namespace OPS.Landform
{
    public class BEVCommandOptions : WedgeCommandOptions
    {
        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool OnlyAligned { get; set; }

        [Option(HelpText = "Auto wedge image decimation target resolution", Default = 512)]
        public override int TargetWedgeImageResolution { get; set; }

        [Option(HelpText = "Auto wedge mesh decimation target resolution", Default = 256)]
        public override int TargetWedgeMeshResolution { get; set; }

        [Option(HelpText = "Stereo eye to prefer", Default = "auto")]
        public string StereoEye { get; set; }

        //need to be able to default this differently for bev-align vs heighmap-align
        //unfortunately can't just make it virtual bool because can't default a bool flag to true
        //https://github.jpl.nasa.gov/OnSight/Landform/issues/1001
        [Option(HelpText = "Use mesh RDRs when available instead of reconstructing wedge meshes from observation pointclouds (\"true\", \"false\", or \"auto\")", Default = "auto")]
        public string UseMeshRDRs { get; set; }

        [Option(HelpText = "Wedge reconstruction method (Organized, Poisson, or FSSR)", Default = MeshReconstructionMethod.Organized)]
        public MeshReconstructionMethod ReconstructionMethod { get; set; }

        [Option(HelpText = "Max triangle aspect ratio for organized mesh reconstruction", Default = 10)]
        public double MaxTriangleAspect { get; set; }

        [Option(HelpText = "Disable generating organized mesh normals when normal image missing", Default = false)]
        public bool NoGenerateNormals { get; set; }

        [Option(HelpText = "Birds eye view meters per pixel", Default = 0.005)]
        public double BEVMetersPerPixel { get; set; }

        [Option(HelpText = "Birds eye view max radius in meters from site drive origin, 0 or negative for unlimited", Default = 20)]
        public double MaxBEVRadius { get; set; }

        [Option(HelpText = "Max dense BEV image dimension, 0 or negative to use max heap allocation size", Default = 0)]
        public int SparseImageThreshold { get; set; }

        [Option(HelpText = "Birds eye view blend mode (Over, Average, Max, Min)", Default = BlendMode.Max)]
        public BlendMode BEVBlending { get; set; }

        [Option(HelpText = "Birds eye view coloring (Texture, Tilt, Elevation}", Default = BirdsEyeView.ColorMode.Tilt)]
        public BirdsEyeView.ColorMode BEVColoring { get; set; }

        [Option(HelpText = "Birds eye view sparse invalidation blocksize, relative to largest image dimension if < 1, disabled if 0", Default = 0.005)]
        public double BEVSparseBlocksize { get; set; }

        [Option(HelpText = "Birds eye view sparse invalidation block threshold", Default = 0.8)]
        public double BEVMinValidBlockRatio { get; set; }

        [Option(HelpText = "Birds eye view smoothing box size (should be odd)", Default = 1)]
        public int BEVSmoothing { get; set; }

        [Option(HelpText = "Birds eye view decimation", Default = 2)]
        public int BEVDecimation { get; set; }

        [Option(HelpText = "Inpaint birds eye view images by this many pixels, 0 to disable, negative for unlimited", Default = 20)]
        public int BEVInpaint { get; set; }

        [Option(HelpText = "Threshold BEV images at this level", Default = 0)]
        public double BEVThreshold { get; set; }

        [Option(HelpText = "Recompute existing BEVs", Default = false)]
        public bool RedoBEVs { get; set; }

        [Option(HelpText = "Optimize contrast", Default = true)]
        public bool StretchContrast { get; set; }

        [Option(HelpText = "Optimize color contrast number of standard deviations", Default = 2)]
        public double StretchStdDevs { get; set; }
    }

    public abstract class BEVCommand : WedgeCommand
    {
        private BEVCommandOptions bcopts;

        private bool useMeshRDRs;

        protected WedgeObservations.CollectOptions wedgeCollectOpts;
        protected WedgeObservations.MeshOptions wedgeMeshOpts;
        protected BirdsEyeView.BEVOptions bevOptions;

        //observations grouped by wedge
        //populated by CollectWedgeObservations()
        protected List<WedgeObservations> wedgeObservations;

        //sitedrive => (observation, mesh, image), (observation, mesh, image), ...
        //populated by BuildWedgeMeshes()
        //wedge meshes are always generated in root frame using transform priors
        protected ConcurrentDictionary<SiteDrive, ConcurrentBag<Tuple<string, Mesh, Image>>> wedgeMeshes =
            new ConcurrentDictionary<SiteDrive, ConcurrentBag<Tuple<string, Mesh, Image>>>();

        //sitedrive => BEV image
        //populated by LoadOrRenderBEVs()
        protected ConcurrentDictionary<SiteDrive, Image> bevs = new ConcurrentDictionary<SiteDrive, Image>();

        //sitedrive => DEM image
        //populated by LoadOrRenderBEVs()
        protected ConcurrentDictionary<SiteDrive, Image> dems = new ConcurrentDictionary<SiteDrive, Image>();

        //sitedrive => pixel in BEV & DEM image corresponding to project root frame origin, based on priors
        protected ConcurrentDictionary<SiteDrive, Vector2> rootOriginPixel =
            new ConcurrentDictionary<SiteDrive, Vector2>();

        //sitedrive => pixel in BEV & DEM image corresponding to site drive frame origin, based on priors
        protected ConcurrentDictionary<SiteDrive, Vector2> sdOriginPixel =
            new ConcurrentDictionary<SiteDrive, Vector2>();

        protected double MetersPerPixel { get { return bcopts.BEVMetersPerPixel * bcopts.BEVDecimation; } }
        protected double PixelsPerMeter { get { return 1 / MetersPerPixel; } }

        /// <summary>
        /// convenicence method to get prior transform from siteDrive to project root frame
        /// </summary>
        protected Matrix SiteDrivePrior(SiteDrive siteDrive)
        {
            return frameCache.GetBestPrior(siteDrive.ToString()).Transform.Mean;
        }

        /// <summary>
        /// convenicence method to best available transform from siteDrive to project root frame
        /// if there is an adjusted transform from one of the allowed sources (typically any source but this aligner)
        /// then that is used
        /// otherwise returns the best prior
        /// </summary>
        protected Matrix SiteDriveBest(SiteDrive siteDrive)
        {
            return frameCache.GetBestTransform(siteDrive.ToString()).Transform.Mean;
        }

        /// <summary>
        /// map a 3D point in meters from a given site drive to a 2D point in pixels in a given site drive
        /// </summary>
        protected Vector2 PointToPixel(Vector3 srcPoint, SiteDrive srcSiteDrive, SiteDrive dstSiteDrive)
        {
            var srcToRoot = SiteDrivePrior(srcSiteDrive);
            var ptInRoot = Vector3.Transform(srcPoint, srcToRoot);
            var pixelInRoot = ptInRoot * PixelsPerMeter;
            return rootOriginPixel[dstSiteDrive] + new Vector2(pixelInRoot.X, pixelInRoot.Y);
        }

        public BEVCommand(BEVCommandOptions options) : base(options)
        {
            this.bcopts = options;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            if (bcopts.OnlyAligned)
            {
                //wedge meshes are always generated in root frame using transform priors
                throw new Exception("--onlyaligned not supported for this command");
            } 

            if (!bcopts.UsePriors && string.IsNullOrEmpty(bcopts.AdjustedTransformSources))
            {
                var excluded = GetDefaultExcludedAdjustedTransformSources();
                var allowed = Enum.GetValues(typeof(TransformSource)).Cast<TransformSource>()
                    .Where(s => s < TransformSource.Prior && !excluded.Contains(s))
                    .Select(s => s.ToString());
                bcopts.AdjustedTransformSources = String.Join(",", allowed);
                pipeline.LogInfo("allowing adjusted transform sources: {0}", bcopts.AdjustedTransformSources);
            } 

            if (!base.ParseArgumentsAndLoadCaches(outDir))
            {
                return false; //help
            }

            var useMeshRDRsStr = bcopts.UseMeshRDRs.ToLower().Trim();
            useMeshRDRs = useMeshRDRsStr == "auto" ? AutoUseMeshRDRs() : useMeshRDRsStr == "true";

            MakeCollectOpts();
            MakeMeshOpts();
            MakeBEVOpts();
                             
            //if user did not specify --onlyforsitedrves then find all site drives in project
            if (siteDrives.Length == 0)
            {
                CollectWedgeObservations();
                siteDrives = wedgeObservations.Select(obs => obs.SiteDrive).Distinct().ToArray();
            }

            //lexicographically sort siteDrives so that older ones come before newer just to give a canonical order
            siteDrives = siteDrives.Distinct().OrderBy(sd => sd).ToArray();

            return true;
        }

        protected abstract HashSet<TransformSource> GetDefaultExcludedAdjustedTransformSources();

        protected override bool ObservationFilter(RoverObservation obs)
        {
            return obs.UseForAlignment;
        }

        protected override string DescribeObservationFilter()
        {
            return " alignment";
        }

        protected virtual bool AutoUseMeshRDRs()
        {
            return false;
        }

        protected void MakeCollectOpts(bool requireNormals, bool requireTextures)
        {
            wedgeCollectOpts = new WedgeObservations.CollectOptions(bcopts.OnlyForSiteDrives, bcopts.OnlyForFrames,
                                                                    bcopts.OnlyForCameras, mission)
            {
                RequireMeshable = true,
                RequireReconstructable = !useMeshRDRs,
                RequireNormals = requireNormals,
                RequireTextures = requireTextures,
                IncludeForAlignment = true,
                IncludeForMeshing = false,
                IncludeForTexturing = false,
                RequirePriorTransform = true,
                TargetFrame = "root",
                FilterMeshableWedgesForEye = RoverStereoPair.ParseEyeForGeometry(bcopts.StereoEye, mission)
            };
        }

        protected virtual void MakeCollectOpts()
        {
            MakeCollectOpts(requireNormals: (bcopts.BEVColoring == BirdsEyeView.ColorMode.Tilt &&
                                             !useMeshRDRs && bcopts.NoGenerateNormals),
                            requireTextures: bcopts.BEVColoring == BirdsEyeView.ColorMode.Texture);
        }

        protected void MakeMeshOpts(bool applyTexture)
        {
            wedgeMeshOpts = new WedgeObservations.MeshOptions()
            {
                Frame = "root",
                LoadedFrame = mission.GetTacticalMeshFrame(),
                UsePriors = true,
                ApplyTexture = applyTexture,
                MaxTriangleAspect = bcopts.MaxTriangleAspect,
                GenerateNormals = !bcopts.NoGenerateNormals,
                MeshDecimator = bcopts.MeshDecimator,
                AlwaysReconstruct = !useMeshRDRs,
                ReconstructionMethod = bcopts.ReconstructionMethod
            };
        }

        protected virtual void MakeMeshOpts()
        {
            MakeMeshOpts(applyTexture: bcopts.BEVColoring == BirdsEyeView.ColorMode.Texture);
        }

        protected void MakeBEVOpts()
        {
            bevOptions = new BirdsEyeView.BEVOptions
            {
                BlendMode = bcopts.BEVBlending,
                MetersPerPixel = bcopts.BEVMetersPerPixel,
                SparseBlockSize = bcopts.BEVSparseBlocksize,
                MinSparseBlockValidRatio = bcopts.BEVMinValidBlockRatio,
                Inpaint = bcopts.BEVInpaint,
                Blur = bcopts.BEVSmoothing,
                Decimate = bcopts.BEVDecimation,
                MaxRadiusMeters = bcopts.MaxBEVRadius,
                RadiusRelativeToOrigin = true,

                ImageFactory = (bands, width, height) =>
                {
                    string err = null;
                    if (bcopts.SparseImageThreshold > 0 && width > bcopts.SparseImageThreshold)
                    {
                        err = string.Format("width {0} > {1}", width, bcopts.SparseImageThreshold);
                    }
                    if (bcopts.SparseImageThreshold > 0 && err == null && height > bcopts.SparseImageThreshold)
                    {
                        err = string.Format("height {0} > {1}", height, bcopts.SparseImageThreshold);
                    }
                    if (err == null)
                    {
                        err = Image.CheckSize(bands, width, height);
                    }
                    if (string.IsNullOrEmpty(err))
                    {
                        return new Image(bands, width, height);
                    }
                    else
                    {
                        pipeline.LogVerbose("using sparse image to render {0}x{1} {2} band birds eye view: {3}",
                                            width, height, bands, err);
                        return new SparseImage(bands, width, height);
                    }
                },

                WedgeCollectOptions = wedgeCollectOpts,
                WedgeMeshOptions = wedgeMeshOpts,

                DecimateWedgeMeshes = bcopts.DecimateWedgeMeshes,
                TargetWedgeMeshResolution = bcopts.TargetWedgeMeshResolution,
                DecimateWedgeImages = bcopts.DecimateWedgeImages,
                TargetWedgeImageResolution = bcopts.TargetWedgeImageResolution,

                Coloring = bcopts.BEVColoring,
                StretchContrast = bcopts.StretchContrast
            };
        }

        protected void CollectWedgeObservations()
        {
            wedgeObservations = WedgeObservations.Collect(frameCache, observationCache, wedgeCollectOpts);
        }

        /// <summary>
        /// populates wedgeMeshes with individual wedge meshes and textures from observations
        /// </summary>
        protected void BuildWedgeMeshes()
        {
            double startSec = UTCTime.Now();
            if (wedgeObservations == null)
            {
                CollectWedgeObservations();
            }
            int no = wedgeObservations.Count;
            pipeline.LogInfo("creating wedge meshes for {0} observations...", no);

            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(wedgeObservations, obs => { 

                    Interlocked.Increment(ref np);

                    if (!bcopts.NoProgress)
                    {
                        pipeline.LogInfo("computing products for {0} observations in parallel, completed {1}/{2}",
                                         np, nc, no);
                    }

                    var mbsObs = (obs.HasMesh && useMeshRDRs) ? obs.MeshObservation : obs.Points ?? obs.Range;
                    int mbs = WedgeObservations.AutoDecimate(mbsObs, //null ok
                                                             bcopts.DecimateWedgeMeshes,
                                                             bcopts.TargetWedgeMeshResolution);
                    if (mbs > 1 && mbs != bcopts.DecimateWedgeMeshes)
                    {
                        pipeline.LogVerbose("auto decimating wedge mesh {0} with blocksize {1}", obs.Name, mbs);
                    }
                    var mo = wedgeMeshOpts.Clone();
                    mo.Decimate = mbs;
                    Mesh mesh = obs.BuildMesh(pipeline, frameCache, masker, mo);

                    Image img = null;
                    if (bcopts.BEVColoring == BirdsEyeView.ColorMode.Texture && obs.Texture != null)
                    {
                        img = pipeline.LoadImage(obs.Texture.Url);
                        int ibs = WedgeObservations.AutoDecimate(obs.Texture, bcopts.DecimateWedgeImages,
                                                                 bcopts.TargetWedgeImageResolution);
                        if (ibs > 1)
                        {
                            if (ibs != bcopts.DecimateWedgeImages)
                            {
                                pipeline.LogVerbose("auto decimating wedge image {0}, blocksize {1}", obs.Name, ibs);
                            }
                            img = img.Decimated(ibs);
                        }
                    }

                    var input = new Tuple<string, Mesh, Image>(obs.Name, mesh, img);
                    wedgeMeshes.AddOrUpdate(obs.SiteDrive,
                                            _ => new ConcurrentBag<Tuple<string, Mesh, Image>>(new [] { input }),
                                            (_, bag) => { bag.Add(input); return bag; });

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            pipeline.LogInfo("created wedge meshes for {0} observations ({1:F3}s)", nc, UTCTime.Now() - startSec);
        }

        protected virtual void LoadOrRenderBEVs()
        {
            LoadOrRenderBEVs(includeBEVs: true, includeDEMs: true);
        }

        /// <summary>
        /// populates bevs, dems, rootOriginPixel, and sdOriginPixel from database or observations
        /// </summary>
        protected void LoadOrRenderBEVs(bool includeBEVs, bool includeDEMs)
        {
            if (bcopts.RedoBEVs || !LoadBEVs(includeBEVs, includeDEMs))
            {
                BuildWedgeMeshes();
                RenderBEVs(includeBEVs, includeDEMs);
                if (!bcopts.NoSave)
                {
                    SaveBEVs();
                }
            }

            double min = 0, max = 0;
            if (includeBEVs)
            {
                PostProcessBEVs(out min, out max);
            }

            if (bcopts.WriteDebug)
            {
                double startSec = UTCTime.Now();
                int np = 0, nc = 0;
                CoreLimitedParallel.ForEach(bevs, pair => {

                        Interlocked.Increment(ref np);

                        if (!bcopts.NoProgress)
                        {
                            pipeline.LogInfo("saving {0} BEV debug images in parallel, completed {1}/{2}",
                                             np, nc, bevs.Count);
                        }

                        var siteDrive = pair.Key;
                        var bev = pair.Value;
                        if (!bcopts.StretchContrast && bcopts.BEVColoring == BirdsEyeView.ColorMode.Elevation)
                        {
                            bev = new Image(bev);
                            bev.ScaleValues((float)min, (float)max, 0, 1);
                        }
                        SaveImage(bev, siteDrive + "_BEV");

                        Interlocked.Decrement(ref np);
                        Interlocked.Increment(ref nc);
                    });
                pipeline.LogInfo("saved {0} birds eye view images ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);

                startSec = UTCTime.Now();
                np = 0; nc = 0;
                CoreLimitedParallel.ForEach(dems, pair => {

                        Interlocked.Increment(ref np);

                        if (!bcopts.NoProgress)
                        {
                            pipeline.LogInfo("saving {0} DEM debug images in parallel, completed {1}/{2}",
                                             np, nc, dems.Count);
                        }

                        var siteDrive = pair.Key;
                        var dem = new Image(pair.Value);

                        var stats = new ImageStatistics(dem);
                        dem.ScaleValues((float)stats.Average(0).Min, (float)stats.Average(0).Max, 0, 1);

                        SaveImage(dem, siteDrive + "_DEM");

                        Interlocked.Decrement(ref np);
                        Interlocked.Increment(ref nc);
                    });
                pipeline.LogInfo("saved {0} DEM images ({1:F3}s)", dems.Count, UTCTime.Now() - startSec);
            }
        }

        /// <summary>
        /// render any BEV and DEM images that were not loaded from database
        /// if any BEV or DEM fails to render that site drive is removed from siteDrives
        /// </summary>
        protected void RenderBEVs(bool includeBEVs, bool includeDEMs)
        {
            double startSec = UTCTime.Now();
            var bevsNeeded = siteDrives
                .Where(sd => (includeBEVs && !bevs.ContainsKey(sd)) || (includeDEMs && !dems.ContainsKey(sd)))
                .ToArray();
            pipeline.LogInfo("rendering {0} birds eye views...", bevsNeeded.Length);

            var demOptions = bevOptions.Clone();
            demOptions.BlendMode = BlendMode.Average;

            int np = 0, nc = 0, nf = 0;
            CoreLimitedParallel.ForEach(bevsNeeded, siteDrive =>
            {
                try
                {
                    Interlocked.Increment(ref np);

                    if (!bcopts.NoProgress)
                    {
                        pipeline.LogInfo("rendering {0} birds eye views in parallel, completed {1}/{2}",
                                         np, nc, bevsNeeded.Length);
                    }

                    bool renderBEV = includeBEVs && !bevs.ContainsKey(siteDrive);
                    bool renderDEM = includeDEMs && !dems.ContainsKey(siteDrive);

                    Mesh mesh = null;
                    Image img = null;

                    //ensure inputs are in a canonical order particularly for BEVBlending = Over
                    var inputs = wedgeMeshes[siteDrive]
                    .OrderBy(inp => inp.Item1) //order by observation name
                    .Distinct() //ConcurrentBag is not necessarily a set
                    .ToList();

                    if (renderBEV && bcopts.BEVColoring == BirdsEyeView.ColorMode.Texture)
                    {
                        var bad = inputs.Where(inp => !inp.Item2.HasUVs || inp.Item3 == null).ToList();
                        if (bad.Count > 0)
                        {
                            pipeline.LogWarn("{0} wedges missing UVs or texture image, " +
                                             "excluding from BEV for site drive {1}: {2}",
                                             bad.Count, siteDrive, String.Join(", ", bad.Select(inp => inp.Item1)));
                            inputs = inputs.Where(inp => inp.Item2.HasUVs && inp.Item3 != null).ToList();
                        }
                    }

                    var pairs = inputs.Select(inp => new Tuple<Mesh, Image>(inp.Item2, inp.Item3)).ToArray();

                    if (pairs.Length == 0)
                    {
                        throw new Exception("no wedges to render BEV");
                    }
                    
                    if (renderBEV && bcopts.BEVColoring == BirdsEyeView.ColorMode.Texture)
                    {
                        var pair = Mesh.MergeMeshesAndTextures(pairs);
                        mesh = pair.Item1;
                        img = pair.Item2;
                    }
                    else
                    {
                        mesh = Mesh.MergeWithCommonAttributes(pairs.Select(pr => pr.Item1).ToArray());
                    }

                    if (renderBEV)
                    {
                        switch (bcopts.BEVColoring)
                        {
                            case BirdsEyeView.ColorMode.Texture: break;
                            case BirdsEyeView.ColorMode.Tilt:
                            {
                                if (!mesh.HasNormals && !bcopts.NoGenerateNormals)
                                {
                                    mesh.GenerateVertexNormals();
                                }
                                mesh.ColorByNormals(TiltMode.InvAcos);
                                break;
                            }
                            case BirdsEyeView.ColorMode.Elevation:
                            {
                                mesh.ColorByElevation(absolute: true);
                                break;
                            }
                        }
                    }
                    
                    if (bcopts.WriteDebug)
                    {
                        string name = siteDrive + "_BEV_Mesh";
                        if (img != null)
                        {
                            SaveImage(img, name);
                        }
                        SaveMesh(mesh, name, img != null ? (name + imageExt) : null);
                    }

                    var sdToWorld = SiteDrivePrior(siteDrive);
                    var sdCenter = Vector3.Transform(Vector3.Zero, sdToWorld);
                    var sdCenterPixel = new Vector2(sdCenter.X, sdCenter.Y) / bcopts.BEVMetersPerPixel;

                    if (renderBEV)
                    {
                        pipeline.LogVerbose("rendering BEV image for site drive {0}...", siteDrive);

                        //"origin" param to Rasterizer.RenderBirdsEyeView() has different semantics on input vs output
                        //
                        //on input it defines the pixel about which bevOptions.MaxRadiusMeters applies
                        //(since bevOptions.RadiusRelativeToOrigin = true)
                        //we set it as the pixel to which the origin of sitedrive frame would project
                        //this is in naieve BEV image coordinates
                        //without decimation, restriction to mesh bounds, or sparse block trimming
                        //
                        //on output it is the pixel to which the origin of mesh frame would project
                        //and mesh frame is project root frame (WedgeObservations.MeshOptions.Frame = "root")
                        //this is in the actual output BEV image coordinates
                        //including decimation, restriction to mesh bounds, and sparse block trimming

                        Vector2 origin = sdCenterPixel;
                        var bev = Rasterizer.RenderBirdsEyeView(mesh, img, ref origin, bevOptions);
                        
                        pipeline.LogVerbose("birds eye view for site drive {0}: {1}x{2}, origin ({3}, {4}), " +
                                            "{5} meters/pixel ({6} with decimation), sparse block size {7}, " +
                                            "valid block ratio {8}, inpaint {9}, smoothing {10}, decimation {11}, " +
                                            "max radius {12}m",
                                            siteDrive, bev.Width, bev.Height, (int)origin.X, (int)origin.Y,
                                            bcopts.BEVMetersPerPixel, MetersPerPixel, bcopts.BEVSparseBlocksize,
                                            bcopts.BEVMinValidBlockRatio, bcopts.BEVInpaint, bcopts.BEVSmoothing,
                                            bcopts.BEVDecimation, bcopts.MaxBEVRadius);
                        try
                        {
                            if (bev is SparseImage)
                            {
                                bev = (bev as SparseImage).Densify();
                                pipeline.LogVerbose("densified {0}x{1} birds eye view for site drive {2}",
                                                    bev.Width, bev.Height, siteDrive);
                            }
                        }
                        catch (Exception ex)
                        {
                            throw new Exception(string.Format("cannot densify birds eye view, " +
                                                              "try increasing BEV decimation (currently {0}): {1}",
                                                              bcopts.BEVDecimation, ex.Message));
                        }

                        bevs[siteDrive] = bev;

                        rootOriginPixel[siteDrive] = origin;

                        //careful, it would not be right to say sdOriginPixel[siteDrive] = origin + sdCenterPixel
                        //because sdCenterPixel doesn't account for
                        //BEV image decimation, restriction to mesh bounds, or sparse block trimming
                        sdOriginPixel[siteDrive] = PointToPixel(Vector3.Zero, siteDrive, siteDrive);
                    }

                    if (renderDEM)
                    {
                        var bev = bevs.ContainsKey(siteDrive) ? bevs[siteDrive] : null;

                        if (bev != null && bcopts.BEVColoring == BirdsEyeView.ColorMode.Elevation &&
                            bcopts.BEVBlending == BlendMode.Average)
                        {
                            dems[siteDrive] = new Image(bev); //deep copy - BEV may later be post-processed
                        }
                        else
                        {
                            pipeline.LogVerbose("rendering DEM image for site drive {0}...", siteDrive);

                            mesh.ColorByElevation(absolute: true);

                            Vector2 origin = sdCenterPixel;
                            var dem = Rasterizer.RenderBirdsEyeView(mesh, null, ref origin, demOptions);

                            if (bev != null && (dem.Width != bev.Width || dem.Height != bev.Height))
                            {
                                throw new Exception(string.Format("DEM dimensions {0}x{1} don't match BEV {2}x{3}",
                                                                  dem.Width, dem.Height, bev.Width, bev.Height));
                            }

                            if (!rootOriginPixel.ContainsKey(siteDrive))
                            {
                                rootOriginPixel[siteDrive] = origin;
                                sdOriginPixel[siteDrive] = PointToPixel(Vector3.Zero, siteDrive, siteDrive);
                            }
                            else if (origin != rootOriginPixel[siteDrive])
                            {
                                throw new Exception(string.Format("DEM origin {0} doesn't match BEV {1}",
                                                                  origin, rootOriginPixel[siteDrive]));
                            }

                            try
                            {
                                if (dem is SparseImage)
                                {
                                    dem = (dem as SparseImage).Densify();
                                    pipeline.LogVerbose("densified {0}x{1} DEM for site drive {2}",
                                                        dem.Width, dem.Height, siteDrive);
                                }
                            }
                            catch (Exception ex)
                            {
                                throw new Exception(string.Format("cannot densify DEM, " +
                                                                  "try increasing BEV decimation (currently {0}): {1}",
                                                                  bcopts.BEVDecimation, ex.Message));
                            }

                            dems[siteDrive] = dem;
                        }
                    }
                        
                    Interlocked.Increment(ref nc);
                }
                catch (Exception ex)
                {
                    pipeline.LogException(ex, "error rendering BEV for site drive " + siteDrive);
                    Interlocked.Increment(ref nf);
                }
                finally
                {
                    Interlocked.Decrement(ref np);
                }
            });

            siteDrives = siteDrives
                .Where(sd => (!includeBEVs || bevs.ContainsKey(sd)) && (!includeDEMs || dems.ContainsKey(sd)))
                .ToArray();

            pipeline.LogInfo("generated {0} birds eye views, {1} failures ({2:F3}s)", nc, nf, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// populate bevs, dems, rootOriginPixel, and sdOriginPixel from database
        /// returns true iff all were loaded successfully
        /// </summary>
        protected bool LoadBEVs(bool includeBEVs, bool includeDEMs)
        {
            double startSec = UTCTime.Now();
            CoreLimitedParallel.ForEach(siteDrives, siteDrive => {
                    var rec = BirdsEyeView.Find(pipeline, project.Name, siteDrive, bevOptions);
                    if (rec == null)
                    {
                        pipeline.LogVerbose("no cached BEV for {0}", siteDrive);
                    }
                    else if (rec.CreationOptions != bevOptions.Serialize())
                    {
                        pipeline.LogVerbose("options mismatch for cached BEV {0}", siteDrive);
                        pipeline.LogVerbose("cached options: {0}", rec.CreationOptions);
                        pipeline.LogVerbose("required options: {0}", bevOptions.Serialize());
                    }
                    else if ((includeBEVs && rec.BEVGuid == null) || (includeDEMs && rec.DEMGuid == null))
                    {
                        pipeline.LogVerbose("missing BEV or DEM image for cached BEV {0}", siteDrive);
                    }
                    else
                    {
                        var mask = pipeline.GetDataProduct<PngDataProduct>(project, rec.MaskGuid).Image;

                        if (includeBEVs)
                        {
                            var bev = pipeline.GetDataProduct<TiffDataProduct>(project, rec.BEVGuid).Image;
                            bev.UnionMask(mask, new float[] { 1 });
                            bevs[siteDrive] = bev;
                        }

                        if (includeDEMs)
                        {
                            var dem = pipeline.GetDataProduct<TiffDataProduct>(project, rec.DEMGuid).Image;
                            dem.UnionMask(mask, new float[] { 1 });
                            dems[siteDrive] = dem;
                        }

                        rootOriginPixel[siteDrive] = rec.RootOriginPixel;
                        sdOriginPixel[siteDrive] = rec.SiteDriveOriginPixel;
                    }
                });
            int numLoaded = includeBEVs ? bevs.Count : includeDEMs ? dems.Count : 0;
            pipeline.LogInfo("loaded {0} birds eye views ({1:F3}s)", numLoaded, UTCTime.Now() - startSec);
            return numLoaded == siteDrives.Length;
        }

        /// <summary>
        /// save bevs, dems, rootOriginPixel, and sdOriginPixel to database
        /// </summary>
        protected void SaveBEVs()
        {
            double startSec = UTCTime.Now();
            var sds = new HashSet<SiteDrive>();
            sds.UnionWith(bevs.Keys);
            sds.UnionWith(dems.Keys);
            pipeline.LogInfo("saving {0} birds eye views...", sds.Count);
            CoreLimitedParallel.ForEach(sds, siteDrive => {
                    var bev = bevs.ContainsKey(siteDrive) ? bevs[siteDrive] : null;
                    var dem = dems.ContainsKey(siteDrive) ? dems[siteDrive] : null;
                    var mask = (bev ?? dem).MaskToImage();
                    var rootOrigin = rootOriginPixel[siteDrive];
                    var sdOrigin = sdOriginPixel[siteDrive];
                    BirdsEyeView.Create(pipeline, project, siteDrive, bevOptions, bev, dem, mask, rootOrigin, sdOrigin);
                });
            pipeline.LogInfo("saved {0} birds eye views ({1:F3}s)", sds.Count, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// apply optional image processing (e.g. contrast stretching, thresholding) to BEVs
        /// </summary>
        protected void PostProcessBEVs(out double min, out double max)
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("post processing {0} birds eye views...", bevs.Count);

            int n = 0;
            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            double mean = 0;
            double stddev = 0;
            if (bcopts.StretchContrast || bcopts.BEVColoring == BirdsEyeView.ColorMode.Elevation)
            {
                CollectBEVStats(out n, out min, out max, out mean, out stddev);
            }

            if (bcopts.StretchContrast)
            {
                double lower = Math.Max(mean - stddev * bcopts.StretchStdDevs, min);
                double upper = Math.Min(mean + stddev * bcopts.StretchStdDevs, max);
                pipeline.LogInfo("stretching [{0}, {1}] -> [0, 1] ({2} stddev)", lower, upper, bcopts.StretchStdDevs);
                foreach (var bev in bevs.Values)
                {
                    bev.ScaleValues((float)lower, (float)upper, 0, 1);
                }
            }

            if (bcopts.BEVThreshold > 0)
            {
                pipeline.LogInfo("thresholding to {0}", bcopts.BEVThreshold);
                foreach (var bev in bevs.Values)
                {
                    bev.ApplyInPlace(v => v > bcopts.BEVThreshold ? 1 : 0);
                }
            }

            pipeline.LogInfo("post processed {0} birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// collect combined stats across all BEVs  
        /// n - total number of valid pixels
        /// min, max, mean, stddev - stats for valid pixel values
        /// </summary>
        protected void CollectBEVStats(out int n, out double min, out double max, out double mean, out double stddev)
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("collecting combined stats for {0} birds eye views...", bevs.Count);

            n = 0;
            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            mean = 0;
            foreach (var bev in bevs.Values)
            {
                foreach (ImageCoordinate ic in bev.Coordinates(includeInvalidValues: false))
                {
                    var v = bev[0, ic.Row, ic.Col];
                    min = Math.Min(min, v);
                    max = Math.Max(max, v);
                    mean += v;
                    n++;
                }
            }
            mean /= n;
            
            double variance = 0;
            foreach (var bev in bevs.Values)
            {
                foreach (ImageCoordinate ic in bev.Coordinates(includeInvalidValues: false))
                {
                    var d = bev[0, ic.Row, ic.Col] - mean;
                    variance += d * d;
                }
            }
            variance /= n;
            stddev = Math.Sqrt(variance);

            pipeline.LogInfo("{0} valid pixels, min {1:F3}, max {2:F3}, mean {3:F3}, stddev {4:F3}",
                             n, min, max, mean, stddev);
            pipeline.LogInfo("collected stats for {0} birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
        }
    }
}
