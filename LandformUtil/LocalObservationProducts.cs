using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using CommandLine;
using log4net;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline;
using OPS.Landform;

namespace OPS.LandformUtil
{
    [Verb("local-observation-products", HelpText = "create observation mesh and image products")]
    public class LocalObservationProductsOptions : GeometryCommandOptions
    {
        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSave { get; set; }

        [Option(HelpText = "Auto wedge image decimation target resolution", Default = 512)]
        public override int TargetWedgeImageResolution { get; set; }

        [Option(HelpText = "Auto wedge mesh decimation target resolution", Default = 256)]
        public override int TargetWedgeMeshResolution { get; set; }

        [Option(HelpText = "Only create products for observations with normals", Default = false)]
        public bool RequireNormals { get; set; }

        [Option(HelpText = "Only create products for observations with textures", Default = false)]
        public bool RequireTextures { get; set; }

        [Option(HelpText = "Don't write wedge meshes", Default = false)]
        public bool NoWedgeMeshes { get; set; }

        [Option(HelpText = "Don't write observation images (and don't texture wedge meshes)", Default = false)]
        public bool NoWedgeImages { get; set; }

        [Option(HelpText = "Use blended observation textures if available", Default = false)]
        public bool UseBlendedTextures { get; set; }

        [Option(HelpText = "Optimize color contrast", Default = false)]
        public bool StretchContrast { get; set; }

        [Option(HelpText = "Optimize color contrast number of standard deviations", Default = 2)]
        public double StretchStdDev { get; set; }

        [Option(HelpText = "Create point clouds instead of triangle meshes", Default = false)]
        public bool PointCloud { get; set; }

        [Option(HelpText = "Wedge reconstruction method (Organized, Poisson, or FSSR)", Default = MeshReconstructionMethod.Organized)]
        public MeshReconstructionMethod ReconstructionMethod { get; set; }

        [Option(HelpText = "Max triangle aspect ratio for organized mesh reconstruction", Default = 10)]
        public double MaxTriangleAspect { get; set; }

        [Option(HelpText = "Isolated point size for organized mesh reconstruction, 0 to disable", Default = 0)]
        public double IsolatedPointSize { get; set; }

        [Option(HelpText = "Disable generating organized mesh normals when normal image missing", Default = false)]
        public bool NoGenerateNormals { get; set; }

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

        [Option(HelpText = "Stereo eye to prefer for wedges with geometry", Default = "auto")]
        public string StereoEye { get; set; }

        [Option(HelpText = "Write all the things", Default = false)]
        public bool AllTheThings { get; set; }

        [Option(HelpText = "Only generate statistics", Default = false)]
        public bool StatsOnly { get; set; }

        [Option(HelpText = "Write mask images", Default = false)]
        public bool MaskImages { get; set; }

        [Option(HelpText = "Write normals images", Default = false)]
        public bool NormalsImages { get; set; }

        [Option(HelpText = "Mesh coloring (None, Texture, Normals, Curvature, Elevation)", Default = MeshColor.Texture)]
        public MeshColor ColorMeshesBy { get; set; }

        [Option(HelpText = "Convert normals to scalar tilt relative to up (0, 0, -1)", Default = false)]
        public bool ConvertNormalsToTilts { get; set; }

        [Option(HelpText = "Normal to tilt conversion (Abs, Acos, Cos)", Default = OrganizedPointCloud.DEF_TILT_MODE)]
        public TiltMode TiltMode { get; set; }

        [Option(HelpText = "Write curvature images", Default = false)]
        public bool CurvatureImages { get; set; }

        [Option(HelpText = "Curvature image neighborhood (Four, Eight)", Default = Neighborhood.Four)]
        public Neighborhood CurvatureNeighborhood { get; set; }

        [Option(HelpText = "Write elevation images", Default = false)]
        public bool ElevationImages { get; set; }

        [Option(HelpText = "Inpaint normal and elevation images by this many pixels", Default = 0)]
        public int InpaintImages { get; set; }

        [Option(HelpText = "Threshold tilt and elevation images at this level", Default = 0)]
        public double ThresholdImages { get; set; }

        [Option(HelpText = "Write delta range images: a visualization of the 3d distance between the points in one image projected into and compared to the points in another image", Default = false)]
        public bool DeltaRangeImages { get; set;}
    } 

    public class LocalObservationProducts : GeometryCommand
    {
        private LocalObservationProductsOptions options;

        private bool withTextures;
        private bool buildWedgeMeshes;
        private bool buildWedgeImages;

        private List<WedgeObservations> wedgeObservations;

        //sitedrive name => (observation, mesh, image), (observation, mesh, image), ...
        private ConcurrentDictionary<string, ConcurrentBag<Tuple<WedgeObservations, Mesh, Image>>> mergeInputs =
            new ConcurrentDictionary<string, ConcurrentBag<Tuple<WedgeObservations, Mesh, Image>>>();
        
        public LocalObservationProducts(LocalObservationProductsOptions options) : base(options)
        {
            this.options = options;

            if (options.OnlyMergedSiteDriveMeshes)
            {
                options.MergedSiteDriveMeshes = true;
                options.NoWedgeMeshes = true;
                options.NoWedgeImages = true;
                options.FrustumHullMeshes = false;
                options.UncertaintyInflatedFrustumHullMeshes = false;
                options.MaskImages = false;
                options.NormalsImages = false;
                options.CurvatureImages = false;
                options.ElevationImages = false;
                options.DeltaRangeImages = false;
            }

            if (options.AllTheThings)
            {
                options.MergedSiteDriveMeshes = true;
                options.FrustumHullMeshes = true;
                options.UncertaintyInflatedFrustumHullMeshes = true;
                options.MaskImages = true;
                options.NormalsImages = true;
                options.CurvatureImages = true;
                options.ElevationImages = true;
                options.DeltaRangeImages = true;
            }

            if (options.StatsOnly)
            {
                options.MergedSiteDriveMeshes = false;
                options.NoWedgeMeshes = true;
                options.NoWedgeImages = true;
                options.FrustumHullMeshes = false;
                options.UncertaintyInflatedFrustumHullMeshes = false;
                options.MaskImages = false;
                options.NormalsImages = false;
                options.CurvatureImages = false;
                options.ElevationImages = false;
                options.DeltaRangeImages = false;
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

                RunPhase("generate observation products",GenerateObservationProducts);

                if (options.MergedSiteDriveMeshes)
                {
                    RunPhase("generate merged site drive meshes", GenerateMergedSiteDriveMeshes);
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
            if (options.NoSave)
            {
                throw new Exception("--nosave not implemented for this command");
            }

            buildWedgeMeshes = !options.NoWedgeMeshes || options.MergedSiteDriveMeshes || options.StatsOnly;
            withTextures = buildWedgeMeshes && options.ColorMeshesBy == MeshColor.Texture;
            buildWedgeImages = withTextures || !options.NoWedgeImages;
            
            if (options.MergedSiteDriveMeshes && options.MeshFrame == "rover")
            {
                pipeline.LogWarn("cannot write merged sitedrive meshes in rover frame, using newest sitedrive");
                options.MeshFrame = "newest";
            }

            if (!ParseArgumentsAndLoadCaches("alignment/ObservationProducts"))
            {
                return false; // help
            }
            
            var opts = new WedgeObservations.CollectOptions(options.OnlyForSiteDrives, options.OnlyForFrames,
                                                           options.OnlyForCameras, mission)
                {
                    RequirePoints = false,
                    RequireNormals = options.RequireNormals,
                    RequireTextures = options.RequireTextures,
                    IncludeForAlignment = true,
                    IncludeForMeshing = true,
                    IncludeForTexturing = true,
                    RequirePriorTransform = options.UsePriors,
                    RequireAdjustedTransform = options.OnlyAligned,
                    TargetFrame = meshFrame
                }; 

            wedgeObservations = CollectWedgeObservations(opts, options.StereoEye);

            return true;
        }

        private void GenerateObservationProducts()
        {
            int no = wedgeObservations.Count;
            var sds = wedgeObservations
                .Select(obs => obs.SiteDrive)
                .Distinct()
                .OrderBy(sd => sd)
                .Select(sd => sd.ToString())
                .ToArray();
            pipeline.LogInfo("computing observation products for {0} observation frames{1} under {2}", no,
                             sds.Length > 0 ? (" for site drive(s) " + String.Join(",", sds)) : "", localOutputPath);

            //indexed by frame name
            var validPoints = new ConcurrentDictionary<string, int>();
            var validNormals = new ConcurrentDictionary<string, int>();
            var validTriangles = new ConcurrentDictionary<string, int>();
            var faceStats = new ConcurrentDictionary<string, Mesh.FaceStats>();
            var generatedNormals = new ConcurrentDictionary<string, bool>();
            var wedgeDecimation = new ConcurrentDictionary<string, int>();

            var meshOpts = new WedgeObservations.MeshOptions()
                {
                    Frame = meshFrame,
                    UsePriors = options.UsePriors,
                    OnlyAligned = options.OnlyAligned,
                    Decimate = options.DecimateWedgeMeshes,
                    ScaleNormalsByConfidence = options.ScaleNormalsByConfidence,
                    ApplyTexture = withTextures,
                    MaxTriangleAspect = options.MaxTriangleAspect,
                    IsolatedPointSize = options.IsolatedPointSize,
                    GenerateNormals = !options.NoGenerateNormals
                };

            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(wedgeObservations, obs => { 

                Interlocked.Increment(ref np);

                if (!options.NoProgress)
                {
                    pipeline.LogInfo("computing products for {0} observations in parallel, completed {1}/{2}",
                                     np, nc, no);
                }
                
                string siteDrive = obs.SiteDrive.ToString();
                string sdPrefix = !options.SuppressSiteDriveDirectories ? siteDrive + "/" : "";

                //mesh decimation blocksize
                int mbs = WedgeObservations.AutoDecimate(obs.Points != null ? obs.Points : obs.Normals, //null ok
                                                         options.DecimateWedgeMeshes,
                                                         options.TargetWedgeMeshResolution);

                if (mbs > 1 && mbs != options.DecimateWedgeMeshes)
                {
                    pipeline.LogVerbose("auto decimating wedge mesh {0} with blocksize {1}", obs.Name, mbs);
                }
                wedgeDecimation[obs.FrameName] = mbs;

                var mo = meshOpts.Clone();
                mo.Decimate = mbs;

                int numPoints = 0, numNormals = 0, numTriangles = 0;
                Mesh mesh = null;
                if (buildWedgeMeshes && obs.Points != null)
                {
                    mesh = BuildWedgeMesh(obs, mo, out numPoints, out numNormals);
                }

                if (mesh != null)
                {
                    generatedNormals[obs.FrameName] = mesh.HasNormals && numNormals == 0;
                    numTriangles = mesh.Faces.Count;
                    pipeline.LogVerbose("collecting face stats for {0}", obs.Name);
                    faceStats[obs.FrameName] = mesh.CollectFaceStats();
                }

                validPoints[obs.FrameName] = numPoints;
                validNormals[obs.FrameName] = numNormals;
                validTriangles[obs.FrameName] = numTriangles;

                int ibs = WedgeObservations.AutoDecimate(obs.Texture, //null ok
                                                         options.DecimateWedgeImages,
                                                         options.TargetWedgeImageResolution);

                Image img = null;
                if (buildWedgeImages && obs.Texture != null)
                {
                    img = BuildWedgeImage(obs, ibs);
                }

                if (options.MaskImages && !obs.Empty)
                {
                    SaveImage(BuildMaskImage(obs, ibs), sdPrefix + obs.Name + "_mask");
                }

                if (!options.NoWedgeImages && img != null)
                {
                    SaveImage(img, sdPrefix + obs.Name);
                }

                if (options.MergedSiteDriveMeshes && mesh != null)
                {
                    var input = new Tuple<WedgeObservations, Mesh, Image>(obs, mesh, withTextures ? img : null);
                    mergeInputs
                    .AddOrUpdate(siteDrive,
                                 _ => new ConcurrentBag<Tuple<WedgeObservations, Mesh, Image>>(new [] { input }),
                                 (_, bag) => { bag.Add(input); return bag; });
                }

                //save the wedge mesh now that we have both it and its texture
                if (!options.NoWedgeMeshes && mesh != null)
                {
                    if (options.ColorMeshesBy != MeshColor.None && options.ColorMeshesBy != MeshColor.Texture)
                    {
                        if (options.MergedSiteDriveMeshes)
                        {
                            //mutate a copy of the wedge mesh here
                            //because we already saved it for use later in generating the merged sitedrive mesh
                            mesh = new Mesh(mesh);
                        }
                        mesh.ColorBy(options.ColorMeshesBy,
                                     options.ConvertNormalsToTilts ? options.TiltMode : TiltMode.None,
                                     stretch: options.StretchContrast, nStddev: options.StretchStdDev);
                    }
                    SaveMesh(mesh, sdPrefix + obs.Name, (withTextures && img != null) ? (obs.Name + imageExt) : null);
                }
                
                Image mask = null;

                if (options.NormalsImages && obs.Normals != null)
                {
                    string kind = options.ConvertNormalsToTilts ? "Tilts" : "Normals";
                    FinishImage(BuildNormalsImage(obs, mbs, ref mask), mask, mbs, sdPrefix + obs.Name, kind);
                }
                
                if (options.CurvatureImages && obs.Points != null && obs.Normals != null)
                {
                    FinishImage(BuildCurvaturesImage(obs, mbs, ref mask), mask, mbs, sdPrefix + obs.Name, "Curvature");
                }
                
                if (options.ElevationImages && obs.Points != null)
                {
                    FinishImage(BuildElevationsImage(obs, mbs, ref mask), mask, mbs, sdPrefix + obs.Name, "Elevation");
                }
                
                if (options.FrustumHullMeshes && (obs.Texture != null || obs.Points != null))
                {
                    try
                    {
                        var hull = obs.BuildFrustumHull(pipeline, frameCache, mo, uncertaintyInflated: false);
                        SaveMesh(hull.Mesh, sdPrefix + "Frusta/" + obs.Name);
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error creating hull mesh: " + ex.Message);
                    }
                }
                
                if (options.UncertaintyInflatedFrustumHullMeshes && (obs.Texture != null || obs.Points != null))
                {
                    try
                    {
                        var hull = obs.BuildFrustumHull(pipeline, frameCache, mo, uncertaintyInflated: true);
                        SaveMesh(hull.Mesh, sdPrefix + "InflatedFrusta/" + obs.Name);
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error creating uncertainty inflated hull mesh: " + ex.Message);
                    }
                }
                
                if (options.DeltaRangeImages && obs.Points != null)
                {
                    foreach (var otherObs in wedgeObservations)
                    {
                        if (obs != otherObs && otherObs.Points != null &&
                            Overlap.Find(pipeline, project.Name, otherObs.Texture.Name, obs.Texture.Name) != null)
                        {
                            try
                            {
                                Image deltaRange = DeltaRangeImage.Create(pipeline, masker, otherObs, obs, frameCache,
                                                                          options.UsePriors, options.OnlyAligned);
                                string name = sdPrefix + "DeltaRange/" + otherObs.Name + "_in_" + obs.Name;
                                SaveFloatTIFF(deltaRange, name);
                                SaveImage(DeltaRangeImage.CreatePreview(deltaRange), name + "_preview");
                            }
                            catch (Exception ex)
                            {
                                pipeline.LogWarn("error creating delta range image: " + ex.Message);
                            }
                        }
                    }
                }
                
                Interlocked.Decrement(ref np);
                Interlocked.Increment(ref nc);
            });

            foreach (var obs in wedgeObservations)
            {
                if (buildWedgeMeshes)
                {
                    var fn = obs.FrameName;
                    pipeline.LogInfo("{0}: {1} points, {2} normals{3}, {4} triangles{5}{6}{7}{8}",
                                     fn, validPoints[fn], validNormals[fn],
                                     generatedNormals.ContainsKey(fn) && generatedNormals[fn] ? " (generated)" : "",
                                     validTriangles[fn],
                                     wedgeDecimation[fn] > 1 ?
                                     string.Format(" after {0}x decimation", wedgeDecimation[fn])
                                     : "",
                                     faceStats.ContainsKey(fn) ? Environment.NewLine + faceStats[fn].ToString() : "",
                                     Environment.NewLine, obs.ToString(pipeline));
                }
                else
                {
                    pipeline.LogInfo(Environment.NewLine + obs.ToString(pipeline));
                }
            }

            pipeline.LogInfo("generated products for {0} observations", no);
        }

        private Mesh BuildWedgeMesh(WedgeObservations obs, WedgeObservations.MeshOptions mo,
                                    out int numPoints, out int numNormals)
        {
            Mesh mesh = null;
            if (options.PointCloud)
            {
                pipeline.LogVerbose("building point cloud for {0}", obs.Name);
                mesh = obs.BuildPointCloud(pipeline, frameCache, masker, mo);
                obs.CountValid(out numPoints, out numNormals);
            }
            else
            {
                pipeline.LogVerbose("building {0} triangle mesh for {1}", options.ReconstructionMethod, obs.Name);
                Exception ex = null;
                try
                {
                    mesh = obs.BuildMesh(pipeline, frameCache, masker, mo, options.ReconstructionMethod);
                }
                catch (Exception e)
                {
                    ex = e;
                }
                
                obs.CountValid(out numPoints, out numNormals);
                
                if (mesh == null)
                {
                    pipeline.LogWarn("meshing failed on obs {0} ({1} reconstruction, {2} points, {3} normals): {4}",
                                     obs.Name, options.ReconstructionMethod, numPoints, numNormals,
                                     ex != null ? ex.Message : "insufficient data or unknown error");
                }
            }
            return mesh;
        }

        private Image BuildWedgeImage(WedgeObservations obs, int ibs)
        {
            Image img = null;
            try
            {
                if (options.UseBlendedTextures && obs.Texture.BlendedGuid != Guid.Empty)
                {
                    img = pipeline.GetDataProduct<PngDataProduct>(project, obs.Texture.BlendedGuid).Image;
                }
                else
                {
                    img = pipeline.LoadImage(obs.Texture.Url);
                }
                if (ibs > 1)
                {
                    if (ibs != options.DecimateWedgeImages)
                    {
                        pipeline.LogVerbose("auto decimating wedge image {0} with blocksize {1}", obs.Name, ibs);
                    }
                    img = img.Decimated(ibs);
                }
                if (options.StretchContrast)
                {
                    img.ApplyStdDevStretch();
                }
            }
            catch (Exception ex)
            {
                img = null;
                pipeline.LogWarn("error creating wedge image: " + ex.Message);
            }
            return img;
        }

        private Image BuildMaskImage(WedgeObservations obs, int ibs)
        {
            var maskUrl = obs.Mask != null ? obs.Mask.Url : null;
            var imgUrl = obs.Texture != null ? obs.Texture.Url : obs.RoverObs.Url;
            var img = pipeline.LoadImage(imgUrl);
            var dbgImg = new Image(img);
            ImageMasker.MakeMask(pipeline, masker, maskUrl, img, dbgImg);
            if (ibs > 1)
            {
                dbgImg = dbgImg.Decimated(ibs);
            }
            return dbgImg;
        }

        private Image BuildNormalsImage(WedgeObservations obs, int mbs, ref Image mask)
        {
            Image normals = null;
            try
            {
                normals = pipeline.LoadImage(obs.Normals.Url);
                if (mask == null)
                {
                    var maskUrl = obs.Mask != null ? obs.Mask.Url : null;
                    mask = masker.LoadOrBuild(pipeline, maskUrl, normals.Metadata as PDSMetadata);
                }
                Image confidence = null;
                if (options.ScaleNormalsByConfidence)
                {
                    confidence = new PDSImage(pipeline.LoadImage(obs.Points.Url)).GenerateConfidence();
                }
                normals = (new PDSImage(normals)).ConvertNormals(confidence);
                if (normals != null)
                {
                    normals = OrganizedPointCloud.MaskAndDecimateNormals(normals, mbs, mask);
                    if (options.ConvertNormalsToTilts)
                    {
                        normals = OrganizedPointCloud.NormalsToTilt(normals, options.TiltMode);
                    }
                    else
                    {
                        normals.ApplyInPlace(v => Math.Abs(v));
                    }
                }
            }
            catch (Exception ex)
            {
                normals = null;
                pipeline.LogWarn("error creating normals image: " + ex.Message);
            }
            return normals;
        }

        private Image BuildCurvaturesImage(WedgeObservations obs, int mbs, ref Image mask)
        {
            Image curvatures = null;
            try
            {
                var pointsRaw = pipeline.LoadImage(obs.Points.Url);
                if (mask == null)
                {
                    var maskUrl = obs.Mask != null ? obs.Mask.Url : null;
                    mask = masker.LoadOrBuild(pipeline, maskUrl, pointsRaw.Metadata as PDSMetadata);
                }
                var points = (new PDSImage(pointsRaw)).ConvertPoints();
                if (points != null)
                {
                    var normals = (new PDSImage(pipeline.LoadImage(obs.Normals.Url))).ConvertNormals();
                    points = OrganizedPointCloud.MaskAndDecimatePoints(points, mbs, mask);
                    normals = OrganizedPointCloud.MaskAndDecimateNormals(normals, mbs, mask);
                    curvatures = OrganizedPointCloud.Curvatures(points, normals, !options.StretchContrast,
                                                                options.CurvatureNeighborhood);
                }
            }
            catch (Exception ex)
            {
                curvatures = null;
                pipeline.LogWarn("error creating curvature image: " + ex.Message);
            }
            return curvatures;
        }

        private Image BuildElevationsImage(WedgeObservations obs, int mbs, ref Image mask)
        {
            Image elevations = null;
            try
            {
                var pointsRaw = pipeline.LoadImage(obs.Points.Url);
                if (mask == null)
                {
                    var maskUrl = obs.Mask != null ? obs.Mask.Url : null;
                    mask = masker.LoadOrBuild(pipeline, maskUrl, pointsRaw.Metadata as PDSMetadata);
                }
                var points = (new PDSImage(pointsRaw)).ConvertPoints();
                if (points != null)
                {
                    points = OrganizedPointCloud.MaskAndDecimatePoints(points, mbs, mask);
                    elevations = OrganizedPointCloud.Elevations(points, normalize: !options.StretchContrast);
                }
            }
            catch (Exception ex)
            {
                elevations = null;
                pipeline.LogWarn("error creating elevation image: " + ex.Message);
            }
            return elevations;
        }

        private void GenerateMergedSiteDriveMeshes()
        {
            pipeline.LogInfo("generating merged meshes for {0} sitedrives", mergeInputs.Count);

            foreach (var siteDrive in mergeInputs.Keys.OrderBy(name => name))
            {
                //ensure inputs are in a canonical order
                var inputs = mergeInputs[siteDrive]
                    .OrderBy(inp => inp.Item1.FrameName) //order by observation frame
                    .Distinct() //ConcurrentBag is not necessarily a set
                    .ToArray();

                var eye = RoverStereoPair.ParseEyeForGeometry(options.StereoEye, mission);
                if (eye != RoverStereoEye.Any)
                {
                    inputs = inputs
                        .GroupBy(inp => inp.Item1.StereoFrameName)
                        .Select(group => WedgeObservations.FilterForEye(group, eye, inp => inp.Item1))
                        .Where(inp => inp != null)
                        .ToArray();
                }
                
                int hasNormals = 0, hasTextures = 0;
                var bands = new Dictionary<int, int>();
                foreach (var input in inputs)
                {
                    if (input.Item2.HasNormals)
                    {
                        hasNormals++;
                    }
                    if (input.Item3 != null)
                    {
                        hasTextures++;
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
                
                pipeline.LogInfo("generating merged mesh for site drive {0} from {1} {2} eye wedge meshes, " +
                                 "{3} with normals, {4} with textures{5}",
                                 siteDrive, inputs.Length, eye, hasNormals, hasTextures,
                                 hasTextures > 0 ? 
                                 (": " + string.Join(", ", bands.Select(e => string.Format("{0} with {1} bands",
                                                                                           e.Value, e.Key))))
                                 : "");
                
                Mesh mesh = null;
                Image img = null;
                try
                {
                    if (withTextures)
                    {
                        var pair = Mesh.MergeMeshesAndTextures(inputs
                                                               .Select(t => new Tuple<Mesh, Image>(t.Item2, t.Item3))
                                                               .ToArray());
                        mesh = pair.Item1;
                        img = pair.Item2;
                    }
                    else
                    {
                        mesh = Mesh.Merge(inputs.Select(pr => pr.Item2).ToArray());
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogWarn("error creating merged mesh for site drive {0}: {1}", siteDrive, ex.Message);
                }
                
                if (mesh != null && img == null && options.ColorMeshesBy != MeshColor.None)
                {
                    mesh.ColorBy(options.ColorMeshesBy,
                                 options.ConvertNormalsToTilts ? options.TiltMode : TiltMode.None,
                                 allowAdjustColors: true,
                                 stretch: options.StretchContrast, nStddev: options.StretchStdDev);
                }
                
                if (mesh != null && mesh.HasVertices && (options.PointCloud || mesh.HasFaces))
                {
                    if (img != null)
                    {
                        SaveImage(img, siteDrive);
                    }
                    SaveMesh(mesh, siteDrive, img != null ? (siteDrive + imageExt) : null);
                }
            }
        }

        private void FinishImage(Image img, Image mask, int decimateBlocksize, string name, string kind)
        {
            if (img == null)
            {
                return;
            }

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
                if (decimateBlocksize > 1)
                {
                    mask = mask.Decimated(decimateBlocksize);
                } 
                img.AddOuterRegionsToMask(mask, invalid: 0);
                img.Inpaint(options.InpaintImages);
                img.UnionMask(mask, new float[] { 0 } );
            }
            if (img.Bands == 1 && options.ThresholdImages > 0)
            {
                img.ApplyInPlace(v => v > options.ThresholdImages ? 1 : 0);
            }
            pipeline.LogVerbose("saving {0}x{1} {2} image", img.Width, img.Height, kind);
            SaveImage(img, name + "_" + kind);
        }
    }
}
