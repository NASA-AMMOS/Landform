using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using log4net;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using Microsoft.Xna.Framework;
using OPS.Imaging.Emgu;
using Emgu.CV.Structure;

namespace OPS.Pipeline
{
    [Verb("local-observation-products", HelpText = "create observation mesh and image products locally")]
    public class LocalObservationProductsOptions : PipelineCoreOptions
    {
        [Value(0, Required = true, HelpText = "Project name", Default = null)]
        public string ProjectName { get; set; }

        [Option(HelpText = "Only generate products for specific site drives, comma separated", Default = null)]
        public string OnlyForSiteDrives { get; set; }

        [Option(HelpText = "Only generate products for specific cameras, comma separated (FrontHazcamLeft, FrontHazcamRight, RearHazcamLeft, RearHazcamRight, NavcamLeft, NavcamRight, MastcamLeft, MastcamRight, MAHLI)", Default = null)]
        public string OnlyForCameras { get; set; }

        [Option(HelpText = "Output directory, or omit to save to project storage", Default = null)]
        public string OutputFolder { get; set; }

        [Option(HelpText = "Output coordinate frame: rover, sitedrive, or root", Default = "rover")]
        public string OutputFrame { get; set; }

        [Option(HelpText = "Create meshes for mastcam observations", Default = false)]
        public bool AllowMastcam { get; set; }

        [Option(HelpText = "Only create meshes for observations with normals", Default = false)]
        public bool RequireNormals { get; set; }

        [Option(HelpText = "Only create meshes for observations with textures", Default = false)]
        public bool RequireTextures { get; set; }

        [Option(HelpText = "Don't write wedge meshes", Default = false)]
        public bool NoWedgeMeshes { get; set; }

        [Option(HelpText = "Don't write observation images (and don't texture wedge meshes)", Default = false)]
        public bool NoImages { get; set; }

        [Option(HelpText = "Mesh format, e.g. ply, obj, help for list", Default = "ply")]
        public string MeshFormat { get; set; }

        [Option(HelpText = "Image format, e.g. png, jpg, help for list", Default = "jpg")]
        public string ImageFormat { get; set; }

        [Option(HelpText = "Create point clouds instead of triangle meshes", Default = false)]
        public bool PointCloud { get; set; }

        [Option(HelpText = "Triangle mesh reconstruction method (Organized, Poisson, or FSSR)", Default = ReconstructionMethod.Organized)]
        public ReconstructionMethod ReconstructionMethod { get; set; }

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Mesh decimation blocksize", Default = 4)]
        public int DecimateMeshes { get; set; }

        [Option(HelpText = "Image decimation blocksize", Default = 2)]
        public int DecimateImages { get; set; }

        [Option(HelpText = "Optimize color contrast", Default = false)]
        public bool StretchContrast { get; set; }

        [Option(HelpText = "Optimize color contrast number of standard deviations", Default = 2)]
        public double StretchStdDev { get; set; }

        [Option(HelpText = "Max triangle aspect ratio for organized mesh reconstruction", Default = 20)]
        public double MaxTriangleAspect { get; set; }

        [Option(HelpText = "Isolated point size for organized mesh reconstruction, 0 to disable", Default = 0)]
        public double IsolatedPointSize { get; set; }

        [Option(HelpText = "Scale normals by confidence (for Poisson reconstruction)", Default = false)]
        public bool ScaleNormalsByConfidence { get; set; }

        [Option(HelpText = "Don't split output by site drive", Default = false)]
        public bool SuppressSiteDriveDirectories { get; set; }

        [Option(HelpText = "Write camera frustum hull meshes", Default = false)]
        public bool FrustumHullMeshes { get; set; }

        [Option(HelpText = "Write uncertainty inflated camera frustum hull meshes", Default = false)]
        public bool UncertaintyInflatedFrustumHullMeshes { get; set; }

        [Option(HelpText = "Write merged site drive meshes", Default = false)]
        public bool MergedSiteDriveMeshes { get; set; }

        [Option(HelpText = "Write only merged site drive meshes", Default = false)]
        public bool OnlyMergedSiteDriveMeshes { get; set; }

        [Option(HelpText = "Write site drive birds eye view images", Default = false)]
        public bool SiteDriveBirdsEyeViews { get; set; }

        [Option(HelpText = "Birds eye view blend mode (Over, Average, Max, Min)", Default = Meshing.BlendMode.Max)]
        public Meshing.BlendMode BEVBlending { get; set; }

        [Option(HelpText = "Birds eye view meters per pixel", Default = 0.005)]
        public double BEVMetersPerPixel { get; set; }

        [Option(HelpText = "Birds eye view sparse invalidation blocksize, relative to largest image dimension if < 1, disabled if 0", Default = 0.005)]
        public double BEVSparseBlocksize { get; set; }

        [Option(HelpText = "Birds eye view sparse invalidation block threshold", Default = 0.8)]
        public double BEVMinValidBlockRatio { get; set; }

        [Option(HelpText = "Birds eye view smoothing box size (should be odd)", Default = 1)]
        public int BEVSmoothing { get; set; }

        [Option(HelpText = "Birds eye view decimation", Default = 2)]
        public int BEVDecimation { get; set; }

        [Option(HelpText = "Write all the things", Default = false)]
        public bool AllTheThings { get; set; }

        [Option(HelpText = "Write normals images", Default = false)]
        public bool NormalsImages { get; set; }

        [Option(HelpText = "Mesh coloring (None, Texture, Normals, Curvature, Elevation)",
                Default = Meshing.MeshColor.Texture)]
        public Meshing.MeshColor ColorMeshesBy { get; set; }

        [Option(HelpText = "Convert normals to scalar tilt relative to up (0, 0, -1)", Default = false)]
        public bool ConvertNormalsToTilts { get; set; }

        [Option(HelpText = "Normal to tilt conversion (Abs, Acos, Cos)", Default = Meshing.DEF_TILT_MODE)]
        public Meshing.TiltMode TiltMode { get; set; }

        [Option(HelpText = "Write curvature images", Default = false)]
        public bool CurvatureImages { get; set; }

        [Option(HelpText = "Curvature image neighborhood (Four, Eight)", Default = Meshing.Neighborhood.Four)]
        public Meshing.Neighborhood CurvatureNeighborhood { get; set; }

        [Option(HelpText = "Write elevation images", Default = false)]
        public bool ElevationImages { get; set; }

        [Option(HelpText = "Inpaint normal and elevation images by this many pixels", Default = 0)]
        public int InpaintImages { get; set; }

        [Option(HelpText = "Threshold tilt and elevation images at this level", Default = 0)]
        public double ThresholdImages { get; set; }

        [Option(HelpText = "Hide progress", Default = false)]
        public bool NoProgress { get; set; }

        [Option(HelpText = "Operate on cloud data", Default = false)]
        public bool Cloud { get; set; }

        [Option(HelpText = "Write delta range images: a visualization of the 3d distance between the points in one image projected into and compared to the points in another image", Default = false)]
        public bool WriteDeltaRangeImages { get; set;}
    } 

    public class LocalObservationProducts
    {
        private LocalObservationProductsOptions options;
        private PipelineCore pipeline;
        private string imageExt;
        private string meshExt;

        public LocalObservationProducts(LocalObservationProductsOptions options)
        {
            this.options = options;
            if (options.Cloud)
            {
                this.pipeline = new CloudPipeline(options, initQueues: false);
            }
            else
            {
                this.pipeline = new LocalPipeline(options);
            }
        }

        public int Run()
        {
            options.MergedSiteDriveMeshes |= options.SiteDriveBirdsEyeViews;

            options.FrustumHullMeshes &= !options.OnlyMergedSiteDriveMeshes;
            options.UncertaintyInflatedFrustumHullMeshes &= !options.OnlyMergedSiteDriveMeshes;
            options.MergedSiteDriveMeshes |= options.OnlyMergedSiteDriveMeshes;
            options.NoWedgeMeshes |= options.OnlyMergedSiteDriveMeshes;

            options.FrustumHullMeshes |= options.AllTheThings;
            options.UncertaintyInflatedFrustumHullMeshes |= options.AllTheThings;
            options.MergedSiteDriveMeshes |= options.AllTheThings;
            options.NormalsImages |= options.AllTheThings;

            bool withUVs = !options.NoImages && options.ColorMeshesBy == Meshing.MeshColor.Texture;

            options.NoImages |= options.OnlyMergedSiteDriveMeshes && !withUVs;

            var project = Project.Find(pipeline, options.ProjectName);

            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }

            var outputFrame = options.OutputFrame.ToLower().Trim();
            if (!(new [] {"rover", "sitedrive", "root"}).Any(f => outputFrame == f))
            {
                pipeline.LogError("unknown output frame: " + outputFrame);
                return 1;
            }

            if (options.MergedSiteDriveMeshes && outputFrame == "rover")
            {
                pipeline.LogError("cannot write merged sitedrive meshes in rover frame");
                return 1;
            }

            TransformSource[] parseSources(string sources)
            {
                return (sources ?? "")
                    .Split(',')
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Select(s => Enum.Parse(typeof(TransformSource), s.Trim(), ignoreCase: true))
                    .Cast<TransformSource>()
                    .ToArray();
            }
            var adjustedSources = parseSources(options.AdjustedTransformSources);
            var priorSources = parseSources(options.PriorTransformSources);

            string dir = outputFrame + "Frame";
            if (options.UsePriors)
            {
                dir += "/prior";
                if (priorSources.Length > 0)
                {
                    dir += "_" + String.Join("_", priorSources);
                }
            }
            else
            {
                dir += "/best";
                if (priorSources.Length > 0)
                {
                    dir += "_" + String.Join("_", priorSources);
                }
                if (adjustedSources.Length > 0)
                {
                    dir += "_" + String.Join("_", adjustedSources);
                }
            }
            string outputPath = pipeline.GetLocalDebugFolder(options.OutputFolder,
                                                             "alignment/ObservationProducts/" + dir,
                                                             project.Name);

            if (!options.NoWedgeMeshes || options.FrustumHullMeshes ||
                options.UncertaintyInflatedFrustumHullMeshes || options.MergedSiteDriveMeshes)
            {
                meshExt = MeshSerializers.Instance.CheckFormat(options.MeshFormat, pipeline);
                if (meshExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} meshes to {1}", meshExt, outputPath);
            }

            if (!options.NoImages || options.NormalsImages || options.CurvatureImages || options.ElevationImages ||
                options.SiteDriveBirdsEyeViews)
            {
                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} images to {1}", imageExt, outputPath);
            }

            var frameCache = new FrameCache(pipeline, options.ProjectName);
            Func<FrameTransform, bool> filterPrior =
                transform => priorSources.Length == 0 || priorSources.Any(s => s == transform.Source);
            Func<FrameTransform, bool> filterAdjusted =
                transform => adjustedSources.Length == 0 || adjustedSources.Any(s => s == transform.Source);
            frameCache.Preload(loadTransforms: true, transformFilter: ft => 
                               (!options.UsePriors || ft.IsPrior()) && //iff --usepriors only allow priors
                               ((ft.IsPrior() && filterPrior(ft)) || //iff --priorsources only allow specific priors
                                (!ft.IsPrior() && filterAdjusted(ft)))); //iff --adjustedsources only allow specific adj

            var observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.Preload();

            bool requirePoints = false;
            var observations =
                Meshing.CollectMeshObservations(frameCache, observationCache, options.AllowMastcam,
                                                requirePoints, options.RequireNormals, options.RequireTextures,
                                                options.OnlyForSiteDrives, options.OnlyForCameras);
            
            int no = observations.Count();
            var siteDrives = observations.Select(obs => obs.SiteDrive).Distinct().OrderBy(sd => sd).ToArray();
            pipeline.LogInfo("computing observation products for {0} observations{1} under {2}", no,
                             siteDrives.Length > 0 ?
                             (" for site drive(s) " +
                              String.Join(",", siteDrives.Select(sd => sd.ToString()).ToArray())) : "",
                             outputPath);

            //sitedrive name => (observation, mesh, image), (observation, mesh, image), ...
            var mergeInputs = new ConcurrentDictionary<string, ConcurrentBag<Tuple<string, Mesh, Image>>>();
            
            double startSec = UTCTime.Now();
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(observations, obs => { 

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("computing products for {0} observations in parallel, completed {1}/{2}",
                                         np, nc, no);
                    }

                    string siteDrive = obs.SiteDrive.ToString();

                    Mesh mesh = null;
                    bool buildMesh = obs.Points != null && (!options.NoWedgeMeshes || options.MergedSiteDriveMeshes);
                    if (buildMesh && options.PointCloud)
                    {
                        pipeline.LogVerbose("building point cloud for {0}", obs.Points.Name);
                        mesh = Meshing.BuildPointCloud(pipeline, obs, frameCache, outputFrame, options.UsePriors,
                                                       options.DecimateMeshes, options.ScaleNormalsByConfidence);
                        if (!mesh.HasVertices)
                        {
                            mesh = null;
                        }
                    }
                    else if (buildMesh)
                    {
                        pipeline.LogVerbose("building {0} triangle mesh for {1}",
                                            options.ReconstructionMethod, obs.Points.Name);
                        switch (options.ReconstructionMethod)
                        {
                            case ReconstructionMethod.Organized:
                            {
                                mesh = Meshing.BuildOrganizedMesh(pipeline, obs, frameCache, outputFrame,
                                                                  options.UsePriors, options.DecimateMeshes,
                                                                  options.ScaleNormalsByConfidence,
                                                                  options.MaxTriangleAspect, options.IsolatedPointSize,
                                                                  withUVs);
                                break;
                            }
                            case ReconstructionMethod.Poisson:
                            {
                                mesh = Meshing.BuildPoissonMesh(pipeline, obs, frameCache, outputFrame,
                                                                options.UsePriors, options.DecimateMeshes,
                                                                options.ScaleNormalsByConfidence, withUVs);
                                break;
                            }
                            case ReconstructionMethod.FSSR:
                            {
                                mesh = Meshing.BuildFSSRMesh(pipeline, obs, frameCache, outputFrame,
                                                             options.UsePriors, options.DecimateMeshes, withUVs);
                                break;
                            }
                        }
                        if (!mesh.HasFaces)
                        {
                            pipeline.LogWarn("{0} reconstruction failed on observation {1}",
                                             options.ReconstructionMethod, obs.Points.Name);
                            mesh = null;
                        }
                    }

                    //we're running for multiple site drives in parallel so don't mutate outputPath
                    string tmpPath = outputPath;
                    if (!options.SuppressSiteDriveDirectories)
                    {
                        tmpPath += siteDrive + "/";
                    }

                    string imageFilename = null;
                    Image img = null;
                    if (!options.NoImages && obs.Texture != null)
                    {
                        imageFilename = obs.Name + imageExt;
                        img = pipeline.LoadImage(obs.Texture.Url);
                        if (options.DecimateImages > 1)
                        {
                            img = img.Decimated(options.DecimateImages);
                        }
                        var wedgeImg = img;
                        if (options.StretchContrast)
                        {
                            if (options.SiteDriveBirdsEyeViews)
                            {
                                wedgeImg = (Image)img.Clone();
                            }
                            wedgeImg.ApplyStdDevStretch();
                        }
                        if (!options.OnlyMergedSiteDriveMeshes)
                        {
                            string file = tmpPath + imageFilename;
                            pipeline.LogVerbose("saving {0}x{1} wedge image {2}", wedgeImg.Width, wedgeImg.Height, file);
                            PathHelper.EnsureExists(tmpPath);
                            wedgeImg.Save<byte>(file);
                        }
                    }

                    if (options.NormalsImages && obs.Normals != null)
                    {
                        var normals = pipeline.LoadImage(obs.Normals.Url);
                        Image confidence = null;
                        if (options.ScaleNormalsByConfidence)
                        {
                            confidence = Meshing.GenerateConfidence(pipeline.LoadImage(obs.Points.Url));
                        }
                        normals = Meshing.ConvertNormals(normals, confidence);
                        var mask = RoverMask.LoadOrBuild(pipeline, obs.Mask, obs.Normals);
                        normals = Meshing.MaskAndDecimateNormals(normals, options.DecimateImages, mask);
                        string name = "Normals";
                        if (options.ConvertNormalsToTilts)
                        {
                            name = "Tilts";
                            normals = Meshing.NormalsToTilt(normals, options.TiltMode);
                        }
                        else
                        {
                            normals.ApplyInPlace(v => Math.Abs(v));
                        }
                        FinishImage(normals, mask, tmpPath, obs.Name, name);

                    }

                    if (options.CurvatureImages && obs.Points != null && obs.Normals != null)
                    {
                        var points = Meshing.ConvertPoints(pipeline.LoadImage(obs.Points.Url));
                        var normals = Meshing.ConvertNormals(pipeline.LoadImage(obs.Normals.Url));
                        var mask = RoverMask.LoadOrBuild(pipeline, obs.Mask, obs.Points);
                        points = Meshing.MaskAndDecimatePoints(points, options.DecimateImages, mask);
                        normals = Meshing.MaskAndDecimateNormals(normals, options.DecimateImages, mask);
                        var curvatures = Meshing.ComputeCurvatures(points, normals, !options.StretchContrast,
                                                                   options.CurvatureNeighborhood);
                        FinishImage(curvatures, mask, tmpPath, obs.Name, "Curvature");
                    }

                    if (options.ElevationImages && obs.Points != null)
                    {
                        var points = Meshing.ConvertPoints(pipeline.LoadImage(obs.Points.Url));
                        var mask = RoverMask.LoadOrBuild(pipeline, obs.Mask, obs.Points);
                        points = Meshing.MaskAndDecimatePoints(points, options.DecimateImages, mask);
                        var elevations = Meshing.PointsToElevation(points, normalize: !options.StretchContrast);
                        FinishImage(elevations, mask, tmpPath, obs.Name, "Elevation");
                    }

                    if (options.MergedSiteDriveMeshes && mesh != null)
                    {
                        var input = new Tuple<string, Mesh, Image>(obs.Points.Name, mesh, withUVs ? img : null);
                        mergeInputs.AddOrUpdate(siteDrive,
                                                _ => new ConcurrentBag<Tuple<string, Mesh, Image>>(new [] { input }),
                                                (_, bag) => { bag.Add(input); return bag; });
                    }

                    if (!options.NoWedgeMeshes && mesh != null)
                    {
                        if (options.ColorMeshesBy != Meshing.MeshColor.None &&
                            options.ColorMeshesBy != Meshing.MeshColor.Texture)
                        {
                            if (options.MergedSiteDriveMeshes)
                            {
                                mesh = new Mesh(mesh);
                            }
                            Meshing.ColorMesh(mesh, options.ColorMeshesBy,
                                              options.ConvertNormalsToTilts ? options.TiltMode : Meshing.TiltMode.None,
                                              stretch: options.StretchContrast, nStddev: options.StretchStdDev);
                        }
                        string file = tmpPath + obs.Name + meshExt;
                        pipeline.LogVerbose("saving mesh {0}", file);
                        PathHelper.EnsureExists(tmpPath);
                        mesh.Save(file, withUVs ? imageFilename : null);
                    }
                      
                    if (options.FrustumHullMeshes && (obs.Texture != null || obs.Points != null))
                    {
                        var hull = Meshing.BuildFrustumHull(pipeline, obs, frameCache, outputFrame, options.UsePriors,
                                                            uncertaintyInflated: false);
                        string path = tmpPath + "Frusta/";
                        string file = tmpPath + obs.Name + meshExt;
                        pipeline.LogVerbose("saving hull mesh {0}", file);
                        PathHelper.EnsureExists(path);
                        hull.Mesh.Save(file);
                    }

                    if (options.UncertaintyInflatedFrustumHullMeshes)
                    {
                        var hull = Meshing.BuildFrustumHull(pipeline, obs, frameCache, outputFrame, options.UsePriors,
                                                            uncertaintyInflated: true);
                        string path = tmpPath + "InflatedFrusta/";
                        string file = path + obs.Name + meshExt;
                        pipeline.LogVerbose("saving uncertainty inflated hull mesh {0}", file);
                        PathHelper.EnsureExists(path);
                        hull.Mesh.Save(file);
                    }

                    if (options.WriteDeltaRangeImages)
                    {
                        string path = tmpPath + "DeltaRange/";
                        PathHelper.EnsureExists(path);

                        string pathPreview = tmpPath + "DeltaRange/Preview/";
                        PathHelper.EnsureExists(pathPreview);

                        float[] previewDistanceBuckets = new float[] { 0.1f, 0.25f, 0.5f, 0.75f, 1.0f, 1.25f, 1.5f };
                        Vector3 [] colors = BrewerColors.GetColors("Blues", previewDistanceBuckets.Length + 1);
                       
                        foreach (var otherObs in observations)
                        {
                            if (obs == otherObs)
                                continue;

                            var overlap = Overlap.Find(pipeline, project.Name, otherObs.Texture.Name, obs.Texture.Name);
                            if (overlap == null)
                                continue;

                            Image deltaRangeImage = CreateDeltaRangeImage(otherObs, obs, frameCache, options.UsePriors);
                            if (deltaRangeImage != null)
                            {
                                string imageName = otherObs.Points.Name + "_in_" + obs.Points.Name;
                                deltaRangeImage.Save<float>(path + imageName + ".tif");

                                Vector3 backgroundColor = new Vector3(0.9, 0.9, 0.9);
                                Image deltaRangePreview = Image.ColorizeScalarImage(deltaRangeImage.Decimated(4), previewDistanceBuckets, colors.Select(c => c.ToFloatArray()).ToArray(), backgroundColor.ToFloatArray());
                                deltaRangePreview = StampLegend(deltaRangePreview, previewDistanceBuckets, colors, backgroundColor);
                                deltaRangePreview.DeleteMask();
                                deltaRangePreview.Save<byte>(pathPreview + imageName + ".png");
                            }
                        }
                    }

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            if (options.MergedSiteDriveMeshes)
            {
                pipeline.LogInfo("generating merged meshes for {0} sitedrives", mergeInputs.Count);
                foreach (var siteDrive in mergeInputs.Keys.OrderBy(name => name))
                {
                    pipeline.LogInfo("generating merged mesh for site drive {0}", siteDrive);

                    //ensure inpurs are in a canonical order particularly for BEVBlending = Over
                    var inputs = mergeInputs[siteDrive]
                        .OrderBy(inp => inp.Item1) //order by observation name
                        .Distinct() //ConcurrentBag is not necessarily a set
                        .Select(inp => new Tuple<Mesh, Image>(inp.Item2, inp.Item3))
                        .ToArray();

                    var pair = Meshing.MergeMeshesAndTextures(inputs);

                    var mesh = pair.Item1;
                    var img = pair.Item2;

                    Meshing.ColorMesh(mesh, options.ColorMeshesBy,
                                      options.ConvertNormalsToTilts ? options.TiltMode : Meshing.TiltMode.None,
                                      allowAdjustColors: !options.SiteDriveBirdsEyeViews,
                                      stretch: options.StretchContrast, nStddev: options.StretchStdDev);

                    string imageFilename = null;
                    if (img != null)
                    {
                        imageFilename = siteDrive + imageExt;
                        string file = outputPath + imageFilename;
                        pipeline.LogVerbose("saving merged sitedrive texure {0}", file);
                        PathHelper.EnsureExists(outputPath);
                        img.Save<byte>(file);
                    }

                    if (mesh != null && mesh.HasVertices && (options.PointCloud || mesh.HasFaces))
                    {
                        string file = outputPath + siteDrive + meshExt;
                        pipeline.LogVerbose("saving merged sitedrive mesh {0}", file);
                        PathHelper.EnsureExists(outputPath);
                        mesh.Save(file, withUVs ? imageFilename : null);
                    }

                    if (options.SiteDriveBirdsEyeViews)
                    {
                        pipeline.LogInfo("generating birds eye view for site drive {0}", siteDrive);

                        var bevOptions = new Meshing.BEVOptions
                        {
                            BlendMode = options.BEVBlending,
                            MetersPerPixel = options.BEVMetersPerPixel,
                            Greyscale = !withUVs &&
                            (options.ColorMeshesBy != Meshing.MeshColor.Normals || options.ConvertNormalsToTilts),
                            SparseBlockSize = options.BEVSparseBlocksize,
                            MinSparseBlockValidRatio = options.BEVMinValidBlockRatio,
                            Inpaint = options.InpaintImages,
                            Blur = options.BEVSmoothing,
                            Decimate = options.BEVDecimation
                        };
                        var bev = Meshing.RenderBirdsEyeView(mesh, img, bevOptions);

                        if (options.StretchContrast)
                        {
                            bev.ApplyStdDevStretch();
                        }
                        else if (bev.Bands == 1)
                        {
                            bev.Normalize();
                        }
                        if (bev.Bands == 1 && options.ThresholdImages > 0)
                        {
                            bev.ApplyInPlace(v => v > options.ThresholdImages ? 1 : 0);
                        }
                        string file = outputPath + siteDrive + "_BirdsEyeView" + imageExt;
                        pipeline.LogVerbose("saving {0}x{1} merged sitedrive birds eye view {2}",
                                            bev.Width, bev.Height, file);
                        PathHelper.EnsureExists(outputPath);
                        bev.Save<byte>(file);
                    }
                }
            }

            double totalSec = UTCTime.Now() - startSec;
            pipeline.LogInfo("generated products for {0} observations ({1:F3}s)", no, totalSec);

            return 0;
        }

        private Image StampLegend(Image img, float[] previewDistanceBuckets, Vector3[] colorsLowToHigh, Vector3 backgroundColor)
        {
            //formatting parameters
            // if we need a more general layout api these can be exposed
            int largeSpacingPixels = 16;
            int smallSpacingPixels = 7;
            int colorChipWidthPixels = 10;
            int frameWidthPixels = 70;

            int legendDimColor = 3;
            Rgb textColor = new Rgb(40, 40, 40);
            Rgb bgColor = OPS.Imaging.Emgu.Extensions.ToEmguColor(backgroundColor.ToFloatArray());
            Rgb legendColor = new Rgb(Math.Max(0,bgColor.Red - legendDimColor), Math.Max(0, bgColor.Green - legendDimColor), Math.Max(0, bgColor.Blue - legendDimColor));

            //allocate expanded image and clear to background color
            System.Drawing.Size expandedImageSize = new System.Drawing.Size(frameWidthPixels + img.Width, img.Height);
            Emgu.CV.Image<Rgb, byte> emguImg = new Emgu.CV.Image<Rgb, byte>(expandedImageSize);
            emguImg.Draw(new System.Drawing.Rectangle(new System.Drawing.Point(0, 0), new System.Drawing.Size(frameWidthPixels, img.Height)), legendColor, -1);

            //draw legend

            System.Drawing.Point pt = new System.Drawing.Point(largeSpacingPixels, largeSpacingPixels);
            
            //catchall
            emguImg.Draw(new System.Drawing.Rectangle(new System.Drawing.Point(pt.X, pt.Y - (int)colorChipWidthPixels / 2), new System.Drawing.Size(colorChipWidthPixels, colorChipWidthPixels)), OPS.Imaging.Emgu.Extensions.ToEmguColor(colorsLowToHigh.Last().ToFloatArray()), -1);
            emguImg.Draw("> " + previewDistanceBuckets[previewDistanceBuckets.Length - 1].ToString("F2") + "m", new System.Drawing.Point(pt.X + colorChipWidthPixels + smallSpacingPixels, pt.Y), Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.2, textColor, 1);
            pt.Y += largeSpacingPixels;

            for (int idx = previewDistanceBuckets.Length-1; idx >= 0; idx--)
            {
                Rgb color = OPS.Imaging.Emgu.Extensions.ToEmguColor(colorsLowToHigh[idx].ToFloatArray());
                emguImg.Draw(new System.Drawing.Rectangle(new System.Drawing.Point(pt.X,pt.Y - (int)colorChipWidthPixels/2), new System.Drawing.Size(colorChipWidthPixels, colorChipWidthPixels)), color, -1);
                emguImg.Draw("< " + previewDistanceBuckets[idx].ToString("F2") + "m", new System.Drawing.Point(pt.X + colorChipWidthPixels + smallSpacingPixels, pt.Y), Emgu.CV.CvEnum.FontFace.HersheySimplex, 0.2, textColor, 1);
                pt.Y += largeSpacingPixels;
            }
            
            Image result = emguImg.ToOPSImage();
            emguImg.Dispose();

            result.Blit(img,frameWidthPixels,0);

            return result;
        }

        // fills a texture with the difference in the per-pixel range of a src point cloud and dst point cloud 
        // designed to give an coarse visual estimate of how well cameras are aligned
        private Image CreateDeltaRangeImage(MeshObservations srcObs, MeshObservations dstObs, FrameCache frameCache, bool usePriors)
        {
            //load images
            Meshing.LoadOrGenerateMeshImages(this.pipeline, srcObs, 1, false, out Image srcPoints, out Image srcNormals, out Image srcMask);
            srcPoints.UnionMask(srcMask, new float[] { 0 });

            Meshing.LoadOrGenerateMeshImages(this.pipeline, dstObs, 1, false, out Image dstPoints, out Image dstNormals, out Image dstMask);
            dstPoints.UnionMask(dstMask, new float[] { 0 });

            //get camera model
            Image dstImg = pipeline.LoadImage(dstObs.Texture.Url);
            PDSParser dstParser = new PDSParser((PDSMetadata)dstImg.Metadata);
            CameraModel dstCamera = dstParser.metadata.CameraModel;

            var srcObsToDstObs = Meshing.GetTransform(srcObs.Points.FrameName, dstObs.Points.FrameName, frameCache, usePriors).Mean;
            var dstHull = Meshing.BuildFrustumHull(pipeline, dstObs, frameCache, dstObs.Points.FrameName, usePriors, uncertaintyInflated: false);

            //project points of src texture into dst
            Image deltaRangeImg = new Image(1, dstObs.Texture.Width, dstObs.Texture.Height);
            deltaRangeImg.CreateMask(true);

            bool anyValid = false;
            for (int idxSrcRow = 0; idxSrcRow < srcObs.Texture.Height; idxSrcRow++)
            {
                for (int idxSrcCol = 0; idxSrcCol < srcObs.Texture.Width; idxSrcCol++)
                {
                    if (srcPoints.IsInvalid(idxSrcRow, idxSrcCol))
                        continue;

                    Vector3 srcRoverPt = new Vector3(srcPoints[0, idxSrcRow, idxSrcCol], srcPoints[1, idxSrcRow, idxSrcCol], srcPoints[2, idxSrcRow, idxSrcCol]);
                    Vector3 srcPtInDst = Vector3.Transform(srcRoverPt, srcObsToDstObs);

                    //coarse test to ensure no errors at the far distant edges of camera models, or points behind the camera 
                    // projecting to valid screen positions. NOTE: enforces the hull distance limit which may be too conservative
                    // also accuracy is poor for nonlinear camera models
                    if (!dstHull.Contains(srcPtInDst))
                        continue;

                    Vector2 dstPixel = dstCamera.Project(srcPtInDst, out double range);

                    int dstPixelX = (int)Math.Round(dstPixel.X);
                    int dstPixelY = (int)Math.Round(dstPixel.Y);

                    if (dstPixelX < 0 || dstPixelX >= dstObs.Texture.Width ||
                        dstPixelY < 0 || dstPixelY >= dstObs.Texture.Height)
                        continue;

                    //Issue #476: properly handle spreading data across fractional pixels (subpixel projection results) 
                    // and properly handle blending with existing data (coverage channel)

                    Vector3 dstRoverPt = new Vector3(dstPoints[0, dstPixelY, dstPixelX], dstPoints[1, dstPixelY, dstPixelX], dstPoints[2, dstPixelY, dstPixelX]);

                    Vector2 refDstPixel = dstCamera.Project(dstRoverPt, out double refRange);
                    int refDstPixelX = (int)Math.Round(refDstPixel.X);
                    int refDstPixelY = (int)Math.Round(refDstPixel.Y);

                    if (refDstPixelX < 0 || refDstPixelX >= dstObs.Texture.Width ||
                        refDstPixelY < 0 || refDstPixelY >= dstObs.Texture.Height)
                        continue;

                    if (dstPoints.IsInvalid((int)refDstPixelY, (int)refDstPixelX))
                        continue;

                    if ((int)refDstPixelX != (int)dstPixelX || (int)refDstPixelY != (int)dstPixelY)
                        throw new Exception("range product points should map back to the same pixel it was pulled from");

                    deltaRangeImg[0, (int)dstPixel.Y, (int)dstPixel.X] = (float)Vector3.Distance(dstRoverPt, srcPtInDst);
                    deltaRangeImg.SetMaskValue((int)dstPixel.Y, (int)dstPixel.X, false);
                    anyValid = true;
                }
            }

            return anyValid ? deltaRangeImg : null;
        }
        private void FinishImage(Image img, Image mask, string dir, string basename, string name)
        {
            if (options.StretchContrast)
            {
                img = img.ApplyStdDevStretch(options.StretchStdDev);
            }
            if (options.InpaintImages > 0)
            {
                //we're going to call Inpaint() to try to fill in small holes
                //but by its nature it will also inpaint into the rover mask
                //we combat this by re-applying the mask after the inpainting
                //but there is a third category of bad pixels besides rover mask and small holes:
                //outer regions where stereo corelation failed
                //so let's try to add them to the mask
                if (options.DecimateImages > 1)
                {
                    mask = mask.Decimated(options.DecimateImages);
                } 
                img.AddOuterRegionsToMask(mask, invalid: 0);
                img.Inpaint(options.InpaintImages);
                img.UnionMask(mask, new float[] { 0 } );
            }
            if (img.Bands == 1 && options.ThresholdImages > 0)
            {
                img.ApplyInPlace(v => v > options.ThresholdImages ? 1 : 0);
            }
            string file = dir + basename + "_" + name + imageExt;
            pipeline.LogVerbose("saving {0}x{1} {2} image {3}", img.Width, img.Height, name, file);
            PathHelper.EnsureExists(dir);
            img.Save<byte>(file);
        }
    }
}
