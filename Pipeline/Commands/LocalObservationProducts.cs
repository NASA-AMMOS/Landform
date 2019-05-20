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

        [Option(HelpText = "Don't reate meshes for mastcam observations", Default = false)]
        public bool NoMastcam { get; set; }

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

        [Option(HelpText = "Allowed sources for adjusted transforms, comma separated, all if empty (Adjusted,Manual,Landform,LandformBEV,Agisoft)", Default = null)]
        public string AdjustedTransformSources { get; set; }

        [Option(HelpText = "Allowed sources for transform priors, comma separated, all if empty (Prior,PlacesDB,LocationsDB,PDS)", Default = null)]
        public string PriorTransformSources { get; set; }

        [Option(HelpText = "Use transform priors only", Default = false)]
        public bool UsePriors { get; set; }

        [Option(HelpText = "Use adjusted transforms only", Default = false)]
        public bool OnlyAligned { get; set; }

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
        public bool DeltaRangeImages { get; set;}
    } 

    public class LocalObservationProducts
    {
        private LocalObservationProductsOptions options;

        private PipelineCore pipeline;
        private MissionSpecific mission;
        private RoverMasker masker;

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
            options.FrustumHullMeshes &= !options.OnlyMergedSiteDriveMeshes;
            options.UncertaintyInflatedFrustumHullMeshes &= !options.OnlyMergedSiteDriveMeshes;
            options.MergedSiteDriveMeshes |= options.OnlyMergedSiteDriveMeshes;
            options.NoWedgeMeshes |= options.OnlyMergedSiteDriveMeshes;

            options.FrustumHullMeshes |= options.AllTheThings;
            options.UncertaintyInflatedFrustumHullMeshes |= options.AllTheThings;
            options.MergedSiteDriveMeshes |= options.AllTheThings;
            options.NormalsImages |= options.AllTheThings;
            options.CurvatureImages |= options.AllTheThings;
            options.ElevationImages |= options.AllTheThings;
            options.DeltaRangeImages |= options.AllTheThings;

            bool withUVs = !options.NoImages && options.ColorMeshesBy == Meshing.MeshColor.Texture;

            options.NoImages |= options.OnlyMergedSiteDriveMeshes && !withUVs;

            var project = Project.Find(pipeline, options.ProjectName);
            if (project == null)
            {
                pipeline.LogError("project \"{0}\" not found", options.ProjectName);
                return 1;
            }
            mission = MissionSpecific.GetInstance(project.Mission);
            masker = mission.GetMasker();

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
                if (options.OnlyAligned)
                {
                    pipeline.LogError("cannot specify both --usepriors and --onlyaligned");
                    return 1;
                }

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

            if (!options.NoImages || options.NormalsImages || options.CurvatureImages || options.ElevationImages)
            {
                imageExt = ImageSerializers.Instance.CheckFormat(options.ImageFormat, pipeline);
                if (imageExt == null)
                {
                    return 0;
                }
                pipeline.LogInfo("writing {0} images to {1}", imageExt, outputPath);
            }

            var frameCache = new FrameCache(pipeline, options.ProjectName);
            frameCache.PreloadFilteredTransforms(priorSources, adjustedSources, options.UsePriors);

            var observationCache = new ObservationCache(pipeline, options.ProjectName);
            observationCache.Preload();

            var opts = new Meshing.MeshObservationsOptions(options.OnlyForSiteDrives, options.OnlyForCameras)
                {
                    AllowMastcam = !options.NoMastcam,
                    RequirePoints = false,
                    RequireNormals = options.RequireNormals,
                    RequireTextures = options.RequireTextures,
                    RequirePriorTransform = options.UsePriors,
                    RequireAdjustedTransform = options.OnlyAligned,
                    TargetFrame = options.OutputFrame
                }; 
            var comparator = mission.GetRoverObservationComparator();
            var observations =
                Meshing.CollectMeshObservations(frameCache, observationCache, comparator, opts)
                .Where(obs => !obs.Empty)
                .OrderBy(obs => obs.FrameName)
                .OrderBy(obs => obs.Day)
                .OrderBy(obs => obs.StereoFrameName)
                .ToList();
            
            int no = observations.Count();
            var siteDrives = observations.Select(obs => obs.SiteDrive).Distinct().OrderBy(sd => sd).ToArray();
            pipeline.LogInfo("computing observation products for {0} observation frames{1} under {2}",
                             no,
                             siteDrives.Length > 0 ? (" for site drive(s) " +
                                                      String.Join(",", siteDrives.Select(sd => sd.ToString()).ToArray()))
                             : "",
                             outputPath);

            //sitedrive name => (observation, mesh, image), (observation, mesh, image), ...
            var mergeInputs = new ConcurrentDictionary<string, ConcurrentBag<Tuple<MeshObservations, Mesh, Image>>>();

            //frame name => num
            var validPoints = new ConcurrentDictionary<string, int>();
            var validNormals = new ConcurrentDictionary<string, int>();
            var validTriangles = new ConcurrentDictionary<string, int>();
            var generatedNormals = new ConcurrentDictionary<string, bool>();
            
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
                int numPoints = 0, numNormals = 0, numTriangles = 0;
                bool buildMesh = obs.Points != null && (!options.NoWedgeMeshes || options.MergedSiteDriveMeshes);
                if (buildMesh && options.PointCloud)
                {
                    pipeline.LogVerbose("building point cloud for {0}", obs.Points.Name);
                    mesh = Meshing.BuildPointCloud(pipeline, masker, obs, frameCache,
                                                   out numPoints, out numNormals,
                                                   outputFrame,
                                                   options.UsePriors, options.OnlyAligned,
                                                   options.DecimateMeshes, options.ScaleNormalsByConfidence);
                    if (mesh != null && !mesh.HasVertices)
                    {
                        mesh = null;
                    }
                }
                else if (buildMesh)
                {
                    pipeline.LogVerbose("building {0} triangle mesh for {1}",
                                        options.ReconstructionMethod, obs.Points.Name);
                    Exception ex = null;
                    try
                    {
                        switch (options.ReconstructionMethod)
                        {
                            case ReconstructionMethod.Organized:
                            {
                                bool generateNormals = true;
                                mesh = Meshing.BuildOrganizedMesh(pipeline, masker, obs, frameCache,
                                                                  out numPoints, out numNormals,
                                                                  outputFrame,
                                                                  options.UsePriors, options.OnlyAligned,
                                                                  options.DecimateMeshes,
                                                                  options.MaxTriangleAspect,
                                                                  withUVs, generateNormals,
                                                                  options.IsolatedPointSize);
                                if (numNormals == 0 && mesh != null && mesh.HasNormals)
                                {
                                    generatedNormals[obs.FrameName] = true;
                                }
                                break;
                            }
                            case ReconstructionMethod.Poisson:
                            {
                                mesh = Meshing.BuildPoissonMesh(pipeline, masker, obs, frameCache,
                                                                out numPoints, out numNormals,
                                                                outputFrame,
                                                                options.UsePriors, options.OnlyAligned,
                                                                options.DecimateMeshes,
                                                                options.ScaleNormalsByConfidence,
                                                                withUVs);
                                break;
                            }
                            case ReconstructionMethod.FSSR:
                            {
                                mesh = Meshing.BuildFSSRMesh(pipeline, masker, obs, frameCache,
                                                             out numPoints, out numNormals,
                                                             outputFrame,
                                                             options.UsePriors, options.OnlyAligned,
                                                             options.DecimateMeshes,
                                                             withUVs);
                                break;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        ex = e;
                    }
                    
                    if (mesh == null || !mesh.HasFaces)
                    {
                        pipeline.LogWarn("{0} reconstruction failed on observation {1} " +
                                         "({2} valid points, {3} valid normals): {4}",
                                         options.ReconstructionMethod, obs.Points.Name,
                                         numPoints, numNormals,
                                         ex != null ? ex.Message : "insufficient data or unknown error");
                        mesh = null;
                    }
                    else
                    {
                        numTriangles = mesh.Faces.Count;
                    }
                }

                validPoints[obs.FrameName] = numPoints;
                validNormals[obs.FrameName] = numNormals;
                validTriangles[obs.FrameName] = numTriangles;

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
                    try
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
                    catch (Exception ex)
                    {
                        img = null;
                        pipeline.LogWarn("error creating wedge image: " + ex.Message);
                    }
                }
                
                if (options.MergedSiteDriveMeshes && mesh != null)
                {
                    var input = new Tuple<MeshObservations, Mesh, Image>(obs, mesh, withUVs ? img : null);
                    mergeInputs
                    .AddOrUpdate(siteDrive,
                                 _ => new ConcurrentBag<Tuple<MeshObservations, Mesh, Image>>(new [] { input }),
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
                    //img can be null if there was an error loading it
                    mesh.Save(file, withUVs && img != null ? imageFilename : null);
                }
                
                if (options.NormalsImages && obs.Normals != null)
                {
                    try
                    {
                        var normals = pipeline.LoadImage(obs.Normals.Url);
                        Image confidence = null;
                        if (options.ScaleNormalsByConfidence)
                        {
                            confidence = Meshing.GenerateConfidence(pipeline.LoadImage(obs.Points.Url));
                        }
                        normals = Meshing.ConvertNormals(normals, confidence);
                        if (normals != null)
                        {
                            var mask = masker.LoadOrBuild(pipeline, obs.Mask, obs.Normals);
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
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error creating normals image: " + ex.Message);
                    }
                }
                
                if (options.CurvatureImages && obs.Points != null && obs.Normals != null)
                {
                    try
                    {
                        var points = Meshing.ConvertPoints(pipeline.LoadImage(obs.Points.Url));
                        if (points != null)
                        {
                            var normals = Meshing.ConvertNormals(pipeline.LoadImage(obs.Normals.Url));
                            var mask = masker.LoadOrBuild(pipeline, obs.Mask, obs.Points);
                            points = Meshing.MaskAndDecimatePoints(points, options.DecimateImages, mask);
                            normals = Meshing.MaskAndDecimateNormals(normals, options.DecimateImages, mask);
                            var curvatures = Meshing.ComputeCurvatures(points, normals, !options.StretchContrast,
                                                                       options.CurvatureNeighborhood);
                            FinishImage(curvatures, mask, tmpPath, obs.Name, "Curvature");
                        }
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error creating curvature image: " + ex.Message);
                    }
                }
                
                if (options.ElevationImages && obs.Points != null)
                {
                    try
                    {
                        var points = Meshing.ConvertPoints(pipeline.LoadImage(obs.Points.Url));
                        if (points != null)
                        {
                            var mask = masker.LoadOrBuild(pipeline, obs.Mask, obs.Points);
                            points = Meshing.MaskAndDecimatePoints(points, options.DecimateImages, mask);
                            var elevations = Meshing.PointsToElevation(points, normalize: !options.StretchContrast);
                            FinishImage(elevations, mask, tmpPath, obs.Name, "Elevation");
                        }
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error creating elevation image: " + ex.Message);
                    }
                }
                
                if (options.FrustumHullMeshes && (obs.Texture != null || obs.Points != null))
                {
                    try
                    {
                        var hull = Meshing.BuildFrustumHull(pipeline, obs, frameCache, outputFrame, options.UsePriors,
                                                            options.OnlyAligned, uncertaintyInflated: false);
                        string path = tmpPath + "Frusta/";
                        string file = tmpPath + obs.Name + meshExt;
                        pipeline.LogVerbose("saving hull mesh {0}", file);
                        PathHelper.EnsureExists(path);
                        hull.Mesh.Save(file);
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error creating hull mesh: " + ex.Message);
                    }
                }
                
                if (options.UncertaintyInflatedFrustumHullMeshes)
                {
                    try
                    {
                        var hull = Meshing.BuildFrustumHull(pipeline, obs, frameCache, outputFrame, options.UsePriors,
                                                            options.OnlyAligned, uncertaintyInflated: true);
                        string path = tmpPath + "InflatedFrusta/";
                        string file = path + obs.Name + meshExt;
                        pipeline.LogVerbose("saving uncertainty inflated hull mesh {0}", file);
                        PathHelper.EnsureExists(path);
                        hull.Mesh.Save(file);
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error creating uncertainty inflated hull mesh: " + ex.Message);
                    }
                }
                
                if (options.DeltaRangeImages && obs.Points != null)
                {
                    string path = tmpPath + "DeltaRange/";
                    PathHelper.EnsureExists(path);
                    
                    string pathPreview = tmpPath + "DeltaRange/Preview/";
                    PathHelper.EnsureExists(pathPreview);
                    
                    float[] previewDistanceBuckets = new float[] { 0.1f, 0.25f, 0.5f, 0.75f, 1.0f, 1.25f, 1.5f };
                    Vector3 [] colors = BrewerColors.GetColors("Blues", previewDistanceBuckets.Length + 1);
                    
                    foreach (var otherObs in observations)
                    {
                        if (obs == otherObs || otherObs.Points == null)
                        {
                            continue;
                        }
                        
                        var overlap = Overlap.Find(pipeline, project.Name, otherObs.Texture.Name, obs.Texture.Name);
                        if (overlap == null)
                        {
                            continue;
                        }
                        
                        Image deltaRangeImage = null;
                        try
                        {
                            deltaRangeImage = CreateDeltaRangeImage(otherObs, obs, frameCache,
                                                                    options.UsePriors, options.OnlyAligned);
                        }
                        catch (Exception ex)
                        {
                            pipeline.LogWarn("error creating delta range image: " + ex.Message);
                        }

                        if (deltaRangeImage != null)
                        {
                            string imageName = otherObs.Points.Name + "_in_" + obs.Points.Name;
                            deltaRangeImage.Save<float>(path + imageName + ".tif");
                            
                            Vector3 backgroundColor = new Vector3(0.9, 0.9, 0.9);
                            Image deltaRangePreview =
                                Image.ColorizeScalarImage(deltaRangeImage.Decimated(4), previewDistanceBuckets,
                                                          colors.Select(c => c.ToFloatArray()).ToArray(),
                                                          backgroundColor.ToFloatArray());
                            deltaRangePreview =
                                StampLegend(deltaRangePreview, previewDistanceBuckets, colors, backgroundColor);
                            deltaRangePreview.DeleteMask();
                            deltaRangePreview.Save<byte>(pathPreview + imageName + ".png");
                        }
                    }
                }
                
                Interlocked.Decrement(ref np);
                Interlocked.Increment(ref nc);
            });

            foreach (var obs in observations)
            {
                var fn = obs.FrameName;
                if (!options.NoWedgeMeshes || options.MergedSiteDriveMeshes)
                {
                    pipeline.LogInfo("{0}: {1} points, {2} normals{3}, {4} triangles{5}{6}{7}",
                                     fn, validPoints[fn], validNormals[fn],
                                     generatedNormals.ContainsKey(fn) && generatedNormals[fn] ? " (generated)" : "",
                                     validTriangles[fn],
                                     options.DecimateMeshes > 1 ?
                                     string.Format(" after {0}x decimation", options.DecimateMeshes)
                                     : "",
                                     Environment.NewLine, obs.ToString(pipeline));
                }
                else
                {
                    pipeline.LogInfo(obs.ToString(pipeline));
                }
            }

            if (options.MergedSiteDriveMeshes)
            {
                pipeline.LogInfo("generating merged meshes for {0} sitedrives", mergeInputs.Count);
                foreach (var siteDrive in mergeInputs.Keys.OrderBy(name => name))
                {
                    //ensure inputs are in a canonical order
                    var inputs = mergeInputs[siteDrive]
                        .OrderBy(inp => inp.Item1.FrameName) //order by observation frame
                        .Distinct() //ConcurrentBag is not necessarily a set
                        .ToArray();

                    int withNormals = 0, withTextures = 0;
                    var bands = new Dictionary<int, int>();
                    foreach (var input in inputs)
                    {
                        if (input.Item2.HasNormals)
                        {
                            withNormals++;
                        }
                        if (input.Item3 != null)
                        {
                            withTextures++;
                            int nb = input.Item3.Bands;
                            if (!bands.ContainsKey(nb))
                            {
                                bands[nb] = 1;
                            }
                            else
                            {
                                bands[nb] = bands[nb] + 1;
                            }
                        }
                    }

                    pipeline.LogInfo("generating merged mesh for site drive {0} from {1} wedge meshes, " +
                                     "{2} with normals, {3} with textures{4}",
                                     siteDrive, inputs.Length, withNormals, withTextures,
                                     withTextures > 0 ? 
                                     (": " + string.Join(", ", bands.Select(e => string.Format("{0} with {1} bands",
                                                                                               e.Value, e.Key))))
                                     : "");
                    
                    Mesh mesh = null;
                    Image img = null;
                    try
                    {
                        var pair = Meshing.MergeMeshesAndTextures(inputs
                                                                  .Select(t => new Tuple<Mesh, Image>(t.Item2, t.Item3))
                                                                  .ToArray());
                        mesh = pair.Item1;
                        img = pair.Item2;
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error creating merged mesh for site drive {0}: {1}", siteDrive, ex.Message);
                    }

                    if (mesh != null &&
                        options.ColorMeshesBy != Meshing.MeshColor.None &&
                        options.ColorMeshesBy != Meshing.MeshColor.Texture)
                    {
                        Meshing.ColorMesh(mesh, options.ColorMeshesBy,
                                          options.ConvertNormalsToTilts ? options.TiltMode : Meshing.TiltMode.None,
                                          allowAdjustColors: true,
                                          stretch: options.StretchContrast, nStddev: options.StretchStdDev);
                    }
                        
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
        private Image CreateDeltaRangeImage(MeshObservations srcObs, MeshObservations dstObs, FrameCache frameCache,
                                            bool usePriors, bool noPriors)
        {
            //load images
            var srcPointsRaw = pipeline.LoadImage(srcObs.Points.Url);
            var srcPoints = Meshing.ConvertPoints(srcPointsRaw);

            var dstPointsRaw = pipeline.LoadImage(dstObs.Points.Url);
            var dstPoints = Meshing.ConvertPoints(dstPointsRaw);

            if (srcPoints == null || dstPoints == null)
            {
                return null;
            }

            srcPoints.UnionMask(masker.LoadOrBuild(pipeline, srcObs.Mask, srcPointsRaw , srcObs.Name),
                                new float[] { 0 });

            dstPoints.UnionMask(masker.LoadOrBuild(pipeline, dstObs.Mask, dstPointsRaw , dstObs.Name),
                                new float[] { 0 });

            //get camera model
            Image dstImg = pipeline.LoadImage(dstObs.Texture.Url);
            PDSParser dstParser = new PDSParser((PDSMetadata)dstImg.Metadata);
            CameraModel dstCamera = dstParser.metadata.CameraModel;

            var srcObsToDstObs = Meshing.GetTransform(srcObs.Points.FrameName, dstObs.Points.FrameName, frameCache,
                                                      usePriors, noPriors).Mean;
            var dstHull = Meshing.BuildFrustumHull(pipeline, dstObs, frameCache, dstObs.Points.FrameName, usePriors,
                                                   uncertaintyInflated: false);

            //project points of src texture into dst
            Image deltaRangeImg = new Image(1, dstObs.Texture.Width, dstObs.Texture.Height);
            deltaRangeImg.CreateMask(true);

            bool anyValid = false;
            for (int idxSrcRow = 0; idxSrcRow < srcObs.Texture.Height; idxSrcRow++)
            {
                for (int idxSrcCol = 0; idxSrcCol < srcObs.Texture.Width; idxSrcCol++)
                {
                    if (srcPoints.IsInvalid(idxSrcRow, idxSrcCol))
                    {
                        continue;
                    }

                    Vector3 srcRoverPt = new Vector3(srcPoints[0, idxSrcRow, idxSrcCol],
                                                     srcPoints[1, idxSrcRow, idxSrcCol],
                                                     srcPoints[2, idxSrcRow, idxSrcCol]);
                    Vector3 srcPtInDst = Vector3.Transform(srcRoverPt, srcObsToDstObs);

                    //coarse test to ensure no errors at the far distant edges of camera models, or points behind the
                    //camera projecting to valid screen positions.
                    //NOTE: enforces the hull distance limit which may be too conservative
                    //also accuracy is poor for nonlinear camera models
                    if (!dstHull.Contains(srcPtInDst))
                    {
                        continue;
                    }

                    Vector2 dstPixel = dstCamera.Project(srcPtInDst, out double range);

                    int dstPixelX = (int)Math.Round(dstPixel.X);
                    int dstPixelY = (int)Math.Round(dstPixel.Y);

                    if (dstPixelX < 0 || dstPixelX >= dstObs.Texture.Width ||
                        dstPixelY < 0 || dstPixelY >= dstObs.Texture.Height)
                    {
                        continue;
                    }

                    //Issue #476: properly handle spreading data across fractional pixels (subpixel projection results) 
                    //and properly handle blending with existing data (coverage channel)

                    Vector3 dstRoverPt = new Vector3(dstPoints[0, dstPixelY, dstPixelX],
                                                     dstPoints[1, dstPixelY, dstPixelX],
                                                     dstPoints[2, dstPixelY, dstPixelX]);

                    Vector2 refDstPixel = dstCamera.Project(dstRoverPt, out double refRange);
                    int refDstPixelX = (int)Math.Round(refDstPixel.X);
                    int refDstPixelY = (int)Math.Round(refDstPixel.Y);

                    if (refDstPixelX < 0 || refDstPixelX >= dstObs.Texture.Width ||
                        refDstPixelY < 0 || refDstPixelY >= dstObs.Texture.Height)
                    {
                        continue;
                    }

                    if (dstPoints.IsInvalid((int)refDstPixelY, (int)refDstPixelX))
                    {
                        continue;
                    }

                    if ((int)refDstPixelX != (int)dstPixelX || (int)refDstPixelY != (int)dstPixelY)
                    {
                        throw new Exception("range product points should map back to the same pixel it was pulled from");
                    }

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
