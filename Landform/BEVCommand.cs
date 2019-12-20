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
        [Option(HelpText = "Auto wedge image decimation target resolution", Default = 512)]
        public override int TargetWedgeImageResolution { get; set; }

        [Option(HelpText = "Auto wedge mesh decimation target resolution", Default = 256)]
        public override int TargetWedgeMeshResolution { get; set; }

        [Option(HelpText = "Stereo eye to prefer", Default = "auto")]
        public string StereoEye { get; set; }

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

    public class BEVCommand : WedgeCommand
    {
        private BEVCommandOptions bcopts;

        //observations grouped by wedge, only loaded if necessary (or call CollectWedgeObservations())
        protected List<WedgeObservations> wedgeObservations;

        //sitedrive => (observation, mesh, image), (observation, mesh, image), ...
        protected ConcurrentDictionary<SiteDrive, ConcurrentBag<Tuple<string, Mesh, Image>>> wedgeMeshes =
            new ConcurrentDictionary<SiteDrive, ConcurrentBag<Tuple<string, Mesh, Image>>>();

        //sitedrive => BEV image
        protected ConcurrentDictionary<SiteDrive, Image> bevs = new ConcurrentDictionary<SiteDrive, Image>();

        //sitedrive => DEM image
        protected ConcurrentDictionary<SiteDrive, Image> dems = new ConcurrentDictionary<SiteDrive, Image>();

        //sitedrive => pixel in BEV image corresponding to world frame origin, based on priors
        protected ConcurrentDictionary<SiteDrive, Vector2> bevOrigins = new ConcurrentDictionary<SiteDrive, Vector2>();

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
        /// map a 3D point in meters from a given site drive to a 2D point in pixels in a given site drive
        /// </summary>
        protected Vector2 PointToPixel(Vector3 srcPoint, SiteDrive srcSiteDrive, SiteDrive dstSiteDrive)
        {
            var srcToRoot = SiteDrivePrior(srcSiteDrive);
            var ptInRoot = Vector3.Transform(srcPoint, srcToRoot);
            var pixelInRoot = ptInRoot * PixelsPerMeter;
            return bevOrigins[dstSiteDrive] + new Vector2(pixelInRoot.X, pixelInRoot.Y);
        }

        protected int BEVArea(SiteDrive siteDrive)
        {
            var bev = bevs[siteDrive];
            return bev.Width * bev.Height;
        }

        public BEVCommand(BEVAlignerOptions options) : base(options)
        {
            this.bcopts = options;
        }

        protected override bool ParseArgumentsAndLoadCaches(string outDir)
        {
            if (!base.ParseArgumentsAndLoadCaches(outDir))
            {
                return false; //help
            }

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

        protected override bool ObservationFilter(RoverObservation obs)
        {
            return obs.UseForAlignment;
        }

        protected override string DescribeObservationFilter()
        {
            return " alignment";
        }

        protected void CollectWedgeObservations()
        {
            var opts = new WedgeObservations.CollectOptions(bcopts.OnlyForSiteDrives, bcopts.OnlyForFrames,
                                                            bcopts.OnlyForCameras, mission)
            {
                RequirePoints = true,
                RequireNormals = bcopts.BEVColoring == BirdsEyeView.ColorMode.Tilt && bcopts.NoGenerateNormals,
                RequireTextures = bcopts.BEVColoring == BirdsEyeView.ColorMode.Texture,
                IncludeForAlignment = true,
                IncludeForMeshing = false,
                IncludeForTexturing = false,
                RequirePriorTransform = true,
                TargetFrame = "root"
            };
            wedgeObservations = WedgeObservations.Collect(frameCache, observationCache, opts);

            var stereoEye = RoverStereoPair.ParseEyeForGeometry(bcopts.StereoEye, mission);
            if (stereoEye != RoverStereoEye.Any)
            {
                wedgeObservations = WedgeObservations.FilterForEye(wedgeObservations, stereoEye).ToList(); 
            }
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

            var meshOpts = new WedgeObservations.MeshOptions()
                {
                    Frame = "root",
                    UsePriors = true,
                    MaxTriangleAspect = bcopts.MaxTriangleAspect,
                    GenerateNormals = !bcopts.NoGenerateNormals
                };

            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(wedgeObservations, obs => { 

                    Interlocked.Increment(ref np);

                    if (!bcopts.NoProgress)
                    {
                        pipeline.LogInfo("computing products for {0} observations in parallel, completed {1}/{2}",
                                         np, nc, no);
                    }

                    int mbs = WedgeObservations.AutoDecimate(obs.Points, bcopts.DecimateWedgeMeshes,
                                                             bcopts.TargetWedgeMeshResolution);
                    if (mbs > 1 && mbs != bcopts.DecimateWedgeMeshes)
                    {
                        pipeline.LogVerbose("auto decimating wedge mesh {0} with blocksize {1}", obs.Name, mbs);
                    }
                    var mo = meshOpts.Clone();
                    mo.Decimate = mbs;
                    Mesh mesh = obs.BuildOrganizedMesh(pipeline, frameCache, masker, mo);

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

                    var input = new Tuple<string, Mesh, Image>(obs.Points.Name, mesh, img);
                    wedgeMeshes.AddOrUpdate(obs.SiteDrive,
                                            _ => new ConcurrentBag<Tuple<string, Mesh, Image>>(new [] { input }),
                                            (_, bag) => { bag.Add(input); return bag; });

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            pipeline.LogInfo("created wedge meshes for {0} observations ({1:F3}s)", nc, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// populates bevs, dems, and bevOrigins from database or observations
        /// </summary>
        protected void LoadOrRenderBEVs()
        {
            if (bcopts.RedoBEVs || !LoadBEVs())
            {
                BuildWedgeMeshes();
                RenderBEVs();
                if (!bcopts.NoSave)
                {
                    SaveBEVs();
                }
            }

            PostProcessBEVs(out double min, out double max);

            if (bcopts.WriteDebug)
            {
                double startSec = UTCTime.Now();
                int np = 0, nc = 0;
                CoreLimitedParallel.ForEach(bevs, pair => {

                        Interlocked.Increment(ref np);

                        if (!bcopts.NoProgress)
                        {
                            pipeline.LogInfo("saving {0} birds eye view images in parallel, completed {1}/{2}",
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
            }
        }

        /// <summary>
        /// render any BEV and DEM images that were not loaded from database
        /// </summary>
        protected void RenderBEVs()
        {
            double startSec = UTCTime.Now();
            var bevsNeeded = siteDrives.Where(sd => !bevs.ContainsKey(sd) || !dems.ContainsKey(sd)).ToArray();
            pipeline.LogInfo("rendering {0} birds eye views...", bevsNeeded.Length);

            var bevOptions = new Rasterizer.BEVOptions
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
                }
            };

            var demOptions = bevOptions.Clone();
            demOptions.BlendMode = BlendMode.Average;

            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(bevsNeeded, siteDrive => {

                    Interlocked.Increment(ref np);

                    if (!bcopts.NoProgress)
                    {
                        pipeline.LogInfo("rendering {0} birds eye views in parallel, completed {1}/{2}",
                                         np, nc, bevsNeeded.Length);
                    }

                    Mesh mesh = null;
                    Image img = null;

                    //ensure inputs are in a canonical order particularly for BEVBlending = Over
                    var inputs = wedgeMeshes[siteDrive]
                    .OrderBy(inp => inp.Item1) //order by observation name
                    .Distinct() //ConcurrentBag is not necessarily a set
                    .Select(inp => new Tuple<Mesh, Image>(inp.Item2, inp.Item3))
                    .ToArray();
                    
                    if (bcopts.BEVColoring == BirdsEyeView.ColorMode.Texture)
                    {
                        var pair = Mesh.MergeMeshesAndTextures(inputs);
                        mesh = pair.Item1;
                        img = pair.Item2;
                    }
                    else
                    {
                        mesh = Mesh.Merge(inputs.Select(pr => pr.Item1).ToArray());
                    }
                    
                    switch (bcopts.BEVColoring)
                    {
                        case BirdsEyeView.ColorMode.Texture: break;
                        case BirdsEyeView.ColorMode.Tilt:
                        {
                            mesh.ColorByNormals(TiltMode.InvAcos);
                            break;
                        }
                        case BirdsEyeView.ColorMode.Elevation:
                        {
                            mesh.ColorByElevation(absolute: true);
                            break;
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
                    var sdOrigin = Vector3.Transform(Vector3.Zero, sdToWorld);
                    var sdOriginPixel = new Vector2(sdOrigin.X, sdOrigin.Y) / bcopts.BEVMetersPerPixel;

                    if (!bevs.ContainsKey(siteDrive))
                    {
                        pipeline.LogVerbose("rendering birds eye view for site drive {0}...", siteDrive);
                        Vector2 origin = sdOriginPixel;
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
                            throw new Exception(string.Format("cannot densify birds eye view for site drive {0}, " +
                                                              "try increasing BEV decimation (currently {1}): {2}",
                                                              siteDrive, bcopts.BEVDecimation, ex.Message));
                        }

                        bevs[siteDrive] = bev;
                        bevOrigins[siteDrive] = origin;
                    }

                    if (!dems.ContainsKey(siteDrive))
                    {
                        var bev = bevs[siteDrive];
                        var origin = bevOrigins[siteDrive];

                        if (bcopts.BEVColoring == BirdsEyeView.ColorMode.Elevation &&
                            bcopts.BEVBlending == BlendMode.Average)
                        {
                            dems[siteDrive] = new Image(bev); //deep copy - BEV may later be post-processed
                        }
                        else
                        {
                            mesh.ColorByElevation(absolute: true);
                            Vector2 demOrigin = sdOriginPixel;
                            var dem = Rasterizer.RenderBirdsEyeView(mesh, null, ref demOrigin, demOptions);
                            if (dem.Width != bev.Width || dem.Height != bev.Height)
                            {
                                throw new Exception(string.Format("DEM dimensions {0}x{1} don't match BEV {2}x{3}",
                                                                  dem.Width, dem.Height, bev.Width, bev.Height));
                            }

                            if (demOrigin != origin)
                            {
                                throw new Exception(string.Format("DEM origin {0} doesn't match BEV {1}",
                                                                  demOrigin, origin));
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
                                throw new Exception(string.Format("cannot densify DEM for site drive {0}, " +
                                                                  "try increasing BEV decimation (currently {1}): {2}",
                                                                  siteDrive, bcopts.BEVDecimation, ex.Message));
                            }

                            dems[siteDrive] = dem;
                        }
                    }
                        
                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            pipeline.LogInfo("generated {0} birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
        }

        /// <summary>
        /// populate bevs, dems, and bevOrigins from database
        /// returns true iff all were loaded successfully
        /// </summary>
        protected bool LoadBEVs()
        {
            double startSec = UTCTime.Now();
            CoreLimitedParallel.ForEach(siteDrives, siteDrive => {
                    var rec = BirdsEyeView.Find(pipeline, project.Name, siteDrive.ToString());
                    if (rec != null &&
                        rec.Coloring == bcopts.BEVColoring &&
                        rec.Blending == bcopts.BEVBlending &&
                        rec.MetersPerPixel == bcopts.BEVMetersPerPixel &&
                        rec.SparseBlockSize == bcopts.BEVSparseBlocksize &&
                        rec.MinValidBlockRatio == bcopts.BEVMinValidBlockRatio &&
                        rec.Inpaint == bcopts.BEVInpaint &&
                        rec.Smoothing == bcopts.BEVSmoothing &&
                        rec.Decimation == bcopts.BEVDecimation)
                    {
                        var bev = pipeline.GetDataProduct<TiffDataProduct>(project, rec.BEVGuid).Image;
                        var dem = pipeline.GetDataProduct<TiffDataProduct>(project, rec.DEMGuid).Image;
                        var mask = pipeline.GetDataProduct<PngDataProduct>(project, rec.MaskGuid).Image;
                        bev.UnionMask(mask, new float[] { 1 });
                        dem.UnionMask(mask, new float[] { 1 });
                        bevs[siteDrive] = bev;
                        dems[siteDrive] = dem;
                        bevOrigins[siteDrive] = new Vector2(rec.OriginX, rec.OriginY);
                    }
                });
            pipeline.LogInfo("loaded {0} birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
            return bevs.Count == siteDrives.Length;
        }

        /// <summary>
        /// save bevs, dems, and associated metadata to database
        /// </summary>
        protected void SaveBEVs()
        {
            double startSec = UTCTime.Now();
            pipeline.LogInfo("saving {0} birds eye views...", bevs.Count);
            CoreLimitedParallel.ForEach(bevs, pair => {
                    var siteDrive = pair.Key;
                    var bev = pair.Value;
                    var dem = dems[siteDrive];
                    var mask = bev.MaskToImage();
                    var origin = bevOrigins[siteDrive];
                    BirdsEyeView.Create(pipeline, project, siteDrive.ToString(), bev, dem, mask, origin,
                                        bcopts.BEVColoring, bcopts.BEVBlending, bcopts.BEVMetersPerPixel,
                                        bcopts.BEVSparseBlocksize, bcopts.BEVMinValidBlockRatio, bcopts.BEVInpaint,
                                        bcopts.BEVSmoothing, bcopts.BEVDecimation);
                });
            pipeline.LogInfo("saved {0} birds eye views ({1:F3}s)", bevs.Count, UTCTime.Now() - startSec);
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
