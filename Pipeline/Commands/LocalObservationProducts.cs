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

        [Option(HelpText = "Optimize contrast of images", Default = false)]
        public bool StretchImages { get; set; }

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

        [Option(HelpText = "Write all the things", Default = false)]
        public bool AllTheThings { get; set; }

        [Option(HelpText = "Write normals images", Default = false)]
        public bool NormalsImages { get; set; }

        [Option(HelpText = "Convert normals to scalar tilt relative to up (0, 0, -1)", Default = false)]
        public bool ConvertNormalsToTilts { get; set; }

        [Option(HelpText = "Normal to tilt conversion (Abs, Acos, Cos)", Default = Meshing.DEF_TILT_MODE)]
        public Meshing.TiltMode TiltMode { get; set; }

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
    }

    public class LocalObservationProducts
    {
        private LocalObservationProductsOptions options;
        private PipelineCore pipeline;

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
            options.FrustumHullMeshes &= !options.OnlyMergedSiteDriveMeshes;
            options.UncertaintyInflatedFrustumHullMeshes &= !options.OnlyMergedSiteDriveMeshes;
            options.MergedSiteDriveMeshes |= options.OnlyMergedSiteDriveMeshes;
            options.NoWedgeMeshes |= options.OnlyMergedSiteDriveMeshes;

            options.FrustumHullMeshes |= options.AllTheThings;
            options.UncertaintyInflatedFrustumHullMeshes |= options.AllTheThings;
            options.MergedSiteDriveMeshes |= options.AllTheThings;
            options.NormalsImages |= options.AllTheThings;

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

            string meshExt = null;
            if (!options.NoWedgeMeshes || options.FrustumHullMeshes ||
                options.UncertaintyInflatedFrustumHullMeshes || options.MergedSiteDriveMeshes)
            {
                meshExt = MeshSerializers.Instance.CheckFormat(options.MeshFormat, pipeline);
                if (meshExt == null)
                {
                    return 0;
                }
                if  (!options.NoWedgeMeshes)
                {
                    pipeline.LogInfo("writing {0} wedge meshes to {1}", meshExt, outputPath);
                }
                if (options.FrustumHullMeshes)
                {
                    pipeline.LogInfo("writing {0} hull meshes to {1}", meshExt, outputPath);
                } 
                if (options.UncertaintyInflatedFrustumHullMeshes)
                {
                    pipeline.LogInfo("writing {0} uncertainty inflated hull meshes to {1}", meshExt, outputPath);
                } 
                if (options.MergedSiteDriveMeshes)
                {
                    pipeline.LogInfo("writing {0} merged site drive meshes to {1}", meshExt, outputPath);
                }
            }

            string imageExt = null;
            if (!options.NoImages || options.NormalsImages || options.ElevationImages)
            {
                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return 0;
                }
                if (!options.NoImages)
                {
                    pipeline.LogInfo("writing {0} images to {1}", imageExt, outputPath);
                }
                if (options.NormalsImages)
                {
                    pipeline.LogInfo("writing {0} normals images to {1}", imageExt, outputPath);
                }
                if (options.ElevationImages)
                {
                    pipeline.LogInfo("writing {0} elevation images to {1}", imageExt, outputPath);
                }
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

            var observations = Meshing.CollectMeshObservations(frameCache, observationCache, options.AllowMastcam,
                                                               options.RequireNormals, options.RequireTextures);

            SiteDrive getSiteDrive(MeshObservations obs)
            {
                var ro = obs.Points as RoverObservation;
                return new SiteDrive(ro.Site, ro.Drive);
            }

            string getCamera(MeshObservations obs)
            {
                return (obs.Points as RoverObservation).Sensor;
            }

            SiteDrive[] siteDrives = (options.OnlyForSiteDrives ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => new SiteDrive(s.Trim()))
                .Cast<SiteDrive>()
                .ToArray();

            if (siteDrives.Length > 0)
            {
                observations = observations.Where(obs => siteDrives.Any(sd => sd == getSiteDrive(obs))).ToList();
            }

            string[] cameras = (options.OnlyForCameras ?? "")
                .Split(',')
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            if (cameras.Length > 0)
            {
                observations = observations.Where(obs => cameras.Any(cam => cam == getCamera(obs))).ToList();
            }
                                                  
            int no = observations.Count();
            pipeline.LogInfo("computing observation products for {0} observations{2} under {2}", no,
                             siteDrives.Length > 0 ?
                             (" for site drive(s) " +
                              String.Join(",", siteDrives.Select(sd => sd.ToString()).Cast<string>().ToArray())) : "",
                             outputPath);

            //sitedrive => (mesh, image)
            var mergeInputs = new ConcurrentDictionary<string, ConcurrentBag<Tuple<Mesh, Image>>>();

            double startSec = UTCTime.Now();
            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(observations, obs => { 

                    Interlocked.Increment(ref np);

                    if (!options.NoProgress)
                    {
                        pipeline.LogInfo("computing products for {0} observations in parallel, completed {1}/{2}",
                                         np, nc, no);
                    }

                    Mesh mesh = null;
                    bool buildMesh = !options.NoWedgeMeshes || options.MergedSiteDriveMeshes;
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
                                                                  !options.NoImages);
                                break;
                            }
                            case ReconstructionMethod.Poisson:
                            {
                                mesh = Meshing.BuildPoissonMesh(pipeline, obs, frameCache, outputFrame,
                                                                options.UsePriors, options.DecimateMeshes,
                                                                options.ScaleNormalsByConfidence, !options.NoImages);
                                break;
                            }
                            case ReconstructionMethod.FSSR:
                            {
                                mesh = Meshing.BuildFSSRMesh(pipeline, obs, frameCache, outputFrame,
                                                             options.UsePriors, options.DecimateMeshes,
                                                             !options.NoImages);
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

                    string siteDrive = getSiteDrive(obs).ToString();

                    //we're running for multiple site drives in parallel so don't mutate outputPath
                    string tmpPath = outputPath;
                    if (!options.SuppressSiteDriveDirectories)
                    {
                        tmpPath += siteDrive + "/";
                    }

                    string obsName = obs.Points.Name;
                    string imageFilename = null;
                    Image img = null;
                    if (!options.NoImages && obs.Texture != null)
                    {
                        pipeline.LogVerbose("loading image {0}", obs.Texture.Name);
                        imageFilename = obsName + imageExt;
                        img = pipeline.LoadImage(obs.Texture.Url);
                        if (options.DecimateImages > 1)
                        {
                            pipeline.LogVerbose("decimating {0}x{1} image {2} by {3}", img.Width, img.Height,
                                                obs.Texture.Name, options.DecimateImages);
                            img = img.Decimated(options.DecimateImages);
                        }
                        if (options.StretchImages)
                        {
                            img = img.ApplyStdDevStretch();
                        }
                        if (!options.OnlyMergedSiteDriveMeshes)
                        {
                            string file = tmpPath + imageFilename;
                            pipeline.LogVerbose("saving {0}x{1} image {2}", img.Width, img.Height, file);
                            PathHelper.EnsureExists(tmpPath);
                            img.Save<byte>(file);
                        }
                    }

                    if (options.NormalsImages && obs.Normals != null)
                    {
                        pipeline.LogVerbose("loading normals image {0}", obs.Normals.Name);
                        var normals = pipeline.LoadImage(obs.Normals.Url);
                        Image confidence = null;
                        if (options.ScaleNormalsByConfidence)
                        {
                            confidence = Meshing.GenerateConfidence(pipeline.LoadImage(obs.Points.Url));
                        }
                        normals = Meshing.ConvertNormals(normals, confidence);
                        if (options.DecimateImages > 1)
                        {
                            pipeline.LogVerbose("decimating {0}x{1} normals image {2} by {3}", normals.Width,
                                                normals.Height, obs.Normals.Name, options.DecimateImages);
                        }
                        var mask = RoverMask.LoadOrBuild(pipeline, obs.Mask, obs.Normals);
                        normals = Meshing.MaskAndDecimateNormals(normals, options.DecimateImages, mask);
                        string name = "Normals";
                        if (options.ConvertNormalsToTilts)
                        {
                            name = "Tilts";
                            normals = Meshing.NormalsToTilt(normals, options.TiltMode);
                        }
                        if (options.StretchImages)
                        {
                            normals = normals.ApplyStdDevStretch();
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
                            Meshing.AddOuterRegionsToMask(normals, mask);
                            normals.Inpaint(options.InpaintImages);
                            normals.UnionMask(mask, new float[] { 0 } );
                        }
                        if (options.ConvertNormalsToTilts && options.ThresholdImages > 0)
                        {
                            normals.ApplyInPlace(v => v > options.ThresholdImages ? 1 : 0);
                        }
                        string file = tmpPath + obsName + "_" + name + imageExt;
                        pipeline.LogVerbose("saving {0}x{1} normals image {2}", normals.Width, normals.Height, file);
                        PathHelper.EnsureExists(tmpPath);
                        normals.Save<byte>(file, ImageConverters.AbsNormalizedImageToValueRange);
                    }

                    if (options.ElevationImages && obs.Points != null)
                    {
                        pipeline.LogVerbose("loading points image to generate elevations {0}", obs.Points.Name);
                        var points = Meshing.ConvertPoints(pipeline.LoadImage(obs.Points.Url));
                        if (options.DecimateImages > 1)
                        {
                            pipeline.LogVerbose("decimating {0}x{1} points image {2} by {3}", points.Width,
                                                points.Height, obs.Points.Name, options.DecimateImages);
                        }
                        var mask = RoverMask.LoadOrBuild(pipeline, obs.Mask, obs.Points);
                        points = Meshing.MaskAndDecimatePoints(points, options.DecimateImages, mask);
                        string name = "Elevations";
                        var elevations = Meshing.PointsToElevation(points, normalize: !options.StretchImages);
                        if (options.StretchImages)
                        {
                            elevations = elevations.ApplyStdDevStretch();
                        }
                        if (options.InpaintImages > 0)
                        {
                            //see comment above
                            if (options.DecimateImages > 1)
                            {
                                mask = mask.Decimated(options.DecimateImages);
                            } 
                            Meshing.AddOuterRegionsToMask(elevations, mask);
                            elevations.Inpaint(options.InpaintImages);
                            elevations.UnionMask(mask, new float[] { 0 } );
                        }
                        if (options.ThresholdImages > 0)
                        {
                            elevations.ApplyInPlace(v => v > options.ThresholdImages ? 1 : 0);
                        }
                        string file = tmpPath + obsName + "_" + name + imageExt;
                        pipeline.LogVerbose("saving {0}x{1} elevations image {2}",
                                            elevations.Width, elevations.Height, file);
                        PathHelper.EnsureExists(tmpPath);
                        elevations.Save<byte>(file);
                    }

                    if (options.MergedSiteDriveMeshes && mesh != null)
                    {
                        var pair = new Tuple<Mesh, Image>(mesh, img);
                        mergeInputs.AddOrUpdate(siteDrive,
                                                _ => new ConcurrentBag<Tuple<Mesh, Image>>(new [] { pair }),
                                                (_, bag) => { bag.Add(pair); return bag; });
                    }

                    if (!options.NoWedgeMeshes && mesh != null)
                    {
                        string file = tmpPath + obsName + meshExt;
                        pipeline.LogVerbose("saving mesh {0}", file);
                        PathHelper.EnsureExists(tmpPath);
                        mesh.Save(file, imageFilename);
                    }
                      
                    if (options.FrustumHullMeshes)
                    {
                        var hull = Meshing.BuildFrustumHull(pipeline, obs, frameCache, outputFrame, options.UsePriors,
                                                            uncertaintyInflated: false);
                        string path = tmpPath + "Frusta/";
                        string file = tmpPath + obsName + meshExt;
                        pipeline.LogVerbose("saving hull mesh {0}", file);
                        PathHelper.EnsureExists(path);
                        hull.Mesh.Save(file);
                    }

                    if (options.UncertaintyInflatedFrustumHullMeshes)
                    {
                        var hull = Meshing.BuildFrustumHull(pipeline, obs, frameCache, outputFrame, options.UsePriors,
                                                            uncertaintyInflated: true);
                        string path = tmpPath + "InflatedFrusta/";
                        string file = path + obsName + meshExt;
                        pipeline.LogVerbose("saving uncertainty inflated hull mesh {0}", file);
                        PathHelper.EnsureExists(path);
                        hull.Mesh.Save(file);
                    }

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });
            double totalSec = UTCTime.Now() - startSec;

            if (options.MergedSiteDriveMeshes)
            {
                pipeline.LogInfo("generating merged meshes for {0} sitedrives", mergeInputs.Count);
                foreach (var siteDrive in mergeInputs.Keys.OrderBy(name => name))
                {
                    pipeline.LogInfo("generating merged mesh for site drive {0}", siteDrive);
                    var pair = Meshing.MergeMeshesAndTextures(mergeInputs[siteDrive].Distinct().ToArray());
                    var mesh = pair.Item1;
                    var img = pair.Item2;
                    string imageFilename = null;
                    if (img != null)
                    {
                        imageFilename = siteDrive + imageExt;
                        PathHelper.EnsureExists(outputPath);
                        img.Save<byte>(outputPath + imageFilename);
                    }
                    if (mesh != null && mesh.HasVertices && (options.PointCloud || mesh.HasFaces))
                    {
                        string file = outputPath + siteDrive + meshExt;
                        pipeline.LogVerbose("saving merged mesh {0}", file);
                        PathHelper.EnsureExists(outputPath);
                        mesh.Save(file, imageFilename);
                    }
                }
            }
            pipeline.LogInfo("generated products for {0} observations ({1:F3}s)", no, totalSec);

            return 0;
        }
    }
}
