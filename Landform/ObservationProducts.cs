using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using CommandLine;
using Microsoft.Xna.Framework;
using JPLOPS.Util;
using JPLOPS.Imaging;
using JPLOPS.Geometry;
using JPLOPS.Pipeline;
using JPLOPS.Pipeline.AlignmentServer;

///<summary>
/// Utility to create debug products for observation meshes and images.
///
/// The functionality of observation-products somewhat overlaps with convert-pds and convert-iv.  One major difference
/// is that observation-products operates on a Landform alignment project, whereas convert-pds and convert-iv operate
/// directly on RDR files and do not require a Landform alignment project. Thus, observation-products will show you more
/// specifically what has been ingested as observations in an alignment project.
///
/// If you just want to convert a bunch of RDRs, and you have IV meshes, then convert-pds and convert-iv may be more
/// expedient.
///
/// If you don't necessarily have IV meshes, observation-products will rebuild wedge meshes from XYZ/RNG/UVW point cloud
/// RDRs.
///
/// observation-products can create
/// * camera frustum hull meshes
/// * per-wedge texture images
/// * per-wedge point clouds
/// * per-wedge textured meshes
/// * per-wedge mask, normal, tilt, curvature, elevation, or delta-range images
/// * merged textured sitedrive meshes
/// * statistics about the observations in an alignment project.
///
/// Generate per-wedge mask images, all in one directory:
///
/// Landform.exe observation-products windjana --nowedgemeshes --nowedgeimages --maskimages --usepriors
///   --suppresssitedrivedirectories
///
/// Generate merged unaligned sitedrive meshes:
///
/// Landform.exe observation-products windjana --onlymergedsitedrivemeshes --onlyforphases=meshing
///   --usepriors
///
/// Generate merged aligned sitedrive meshes:
///
/// Landform.exe observation-products windjana --onlymergedsitedrivemeshes --onlyforphases=meshing
/// Landform.exe observation-products windjana --onlymergedsitedrivefrustumhullmeshes --onlyforphases=texturing
///
/// Just spew stats:
///
/// Landform.exe observation-products windjana --statsonly
///</summary>
namespace JPLOPS.Landform
{
    [Verb("observation-products", HelpText = "create observation mesh and image products")]
    public class ObservationProductsOptions : GeometryCommandOptions
    {
        [Option(HelpText = "Option disabled for this command", Default = false)]
        public override bool NoSave { get; set; }

        [Option(HelpText = "Auto wedge image decimation target resolution", Default = 512)]
        public override int TargetWedgeImageResolution { get; set; }

        [Option(HelpText = "Auto wedge mesh decimation target resolution", Default = 256)]
        public override int TargetWedgeMeshResolution { get; set; }

        [Option(HelpText = "Only create products for observations marked for use in specific phases, comma separated list of (alignment,meshing,texturing)", Default = null)]
        public string OnlyForPhases { get; set; }

        [Option(HelpText = "Only include observations with frustum hulls that include at least one of these points. Semicolon separated list of X,Y,Z points in scene frame", Default = null)]
        public string OnlyForPoints { get; set; }

        [Option(HelpText = "Only create products for observations with normals", Default = false)]
        public bool RequireNormals { get; set; }

        [Option(HelpText = "Only create products for observations with textures", Default = false)]
        public bool RequireTextures { get; set; }

        [Option(HelpText = "Don't write wedge meshes", Default = false)]
        public bool NoWedgeMeshes { get; set; }

        [Option(HelpText = "Don't write observation images (and don't texture wedge meshes)", Default = false)]
        public bool NoWedgeImages { get; set; }

        [Option(HelpText = "Create point clouds instead of triangle meshes", Default = false)]
        public bool PointCloud { get; set; }

        [Option(HelpText = "Use mesh RDRs when available instead of reconstructing wedge meshes from observation pointclouds", Default = false)]
        public bool UseMeshRDRs { get; set; }

        [Option(HelpText = "Wedge reconstruction method (Organized, Poisson, or FSSR)", Default = MeshReconstructionMethod.Organized)]
        public MeshReconstructionMethod ReconstructionMethod { get; set; }

        [Option(HelpText = "Max triangle aspect ratio for organized mesh reconstruction", Default = 10)]
        public double MaxTriangleAspect { get; set; }

        [Option(HelpText = "Isolated point size for organized mesh reconstruction, 0 to disable", Default = 0)]
        public double IsolatedPointSize { get; set; }

        [Option(HelpText = "Disable generating organized mesh normals when normal image missing", Default = false)]
        public bool NoGenerateNormals { get; set; }

        [Option(HelpText = "Normal scaling mode, one of None, Confidence, PointScale", Default = NormalScale.None)]
        public NormalScale NormalScale { get; set; }

        [Option(HelpText = "Don't split output by site drive", Default = false)]
        public bool SuppressSiteDriveDirectories { get; set; }

        [Option(HelpText = "Write camera frustum hull meshes", Default = false)]
        public bool FrustumHullMeshes { get; set; }

        [Option(HelpText = "Write merged sitedrive camera frustum hull meshes", Default = false)]
        public bool MergedSiteDriveFrustumHullMeshes { get; set; }

        [Option(HelpText = "Write uncertainty inflated camera frustum hull meshes", Default = false)]
        public bool UncertaintyInflatedFrustumHullMeshes { get; set; }

        [Option(HelpText = "Frustum hull near clip distance in meters", Default = ConvexHull.DEF_NEAR_CLIP)]
        public double FrustumHullNearClip { get; set; }

        [Option(HelpText = "Frustum hull far clip distance in meters", Default = ConvexHull.DEF_FAR_CLIP)]
        public double FrustumHullFarClip { get; set; }

        [Option(HelpText = "Assume camera models are linear CAHV for frustum hull geometry", Default = false)]
        public bool FrustumHullForceLinear { get; set; }

        [Option(HelpText = "Write merged site drive meshes", Default = false)]
        public bool MergedSiteDriveMeshes { get; set; }

        [Option(HelpText = "Write only merged site drive meshes", Default = false)]
        public bool OnlyMergedSiteDriveMeshes { get; set; }

        [Option(HelpText = "Write only merged site drive frustum hull meshes", Default = false)]
        public bool OnlyMergedSiteDriveFrustumHullMeshes { get; set; }

        [Option(HelpText = "Stereo eye to prefer for wedges with geometry", Default = "auto")]
        public string StereoEye { get; set; }

        [Option(HelpText = "Write all the things", Default = false)]
        public bool AllTheThings { get; set; }

        [Option(HelpText = "Only generate statistics", Default = false)]
        public bool StatsOnly { get; set; }

        [Option(HelpText = "Synonym for --statsonly", Default = false)]
        public bool DryRun { get; set; }

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

        [Option(HelpText = "Disable LRU image cache (longer runtime but lower memory footprint)", Default = false)]
        public bool DisableImageCache { get; set;}
    } 

    public class ObservationProducts : GeometryCommand
    {
        private ObservationProductsOptions options;

        private bool withTextures;
        private bool buildWedgeMeshes;
        private bool buildWedgeImages;

        private List<WedgeObservations> wedgeObservations;

        private List<Vector3> onlyForPoints;

        //sitedrive name => (observation, mesh, image), (observation, mesh, image), ...
        private ConcurrentDictionary<string, ConcurrentBag<Tuple<WedgeObservations, Mesh, Image>>> mergeWedges =
            new ConcurrentDictionary<string, ConcurrentBag<Tuple<WedgeObservations, Mesh, Image>>>();
        
        //sitedrive name => (observation, hull), (observation, hull), ...
        private ConcurrentDictionary<string, ConcurrentBag<Tuple<WedgeObservations, ConvexHull>>> mergeHulls =
            new ConcurrentDictionary<string, ConcurrentBag<Tuple<WedgeObservations, ConvexHull>>>();

        public ObservationProducts(ObservationProductsOptions options) : base(options)
        {
            this.options = options;

            if (options.OnlyMergedSiteDriveMeshes)
            {
                options.MergedSiteDriveMeshes = true;
                options.NoWedgeMeshes = true;
                options.NoWedgeImages = true;
                options.FrustumHullMeshes = false;
                options.UncertaintyInflatedFrustumHullMeshes = false;
                options.MergedSiteDriveFrustumHullMeshes = false;
                options.MaskImages = false;
                options.NormalsImages = false;
                options.CurvatureImages = false;
                options.ElevationImages = false;
            }

            if (options.OnlyMergedSiteDriveFrustumHullMeshes)
            {
                options.MergedSiteDriveMeshes = false;
                options.NoWedgeMeshes = true;
                options.NoWedgeImages = true;
                options.FrustumHullMeshes = false;
                options.UncertaintyInflatedFrustumHullMeshes = false;
                options.MergedSiteDriveFrustumHullMeshes = true;
                options.MaskImages = false;
                options.NormalsImages = false;
                options.CurvatureImages = false;
                options.ElevationImages = false;
            }

            if (options.AllTheThings)
            {
                options.MergedSiteDriveMeshes = true;
                options.FrustumHullMeshes = true;
                options.UncertaintyInflatedFrustumHullMeshes = true;
                options.MergedSiteDriveFrustumHullMeshes = true;
                options.MaskImages = true;
                options.NormalsImages = true;
                options.CurvatureImages = true;
                options.ElevationImages = true;
            }

            options.StatsOnly |= options.DryRun;
            if (options.StatsOnly)
            {
                options.MergedSiteDriveMeshes = false;
                options.NoWedgeMeshes = true;
                options.NoWedgeImages = true;
                options.FrustumHullMeshes = false;
                options.UncertaintyInflatedFrustumHullMeshes = false;
                options.MergedSiteDriveFrustumHullMeshes = false;
                options.MaskImages = false;
                options.NormalsImages = false;
                options.CurvatureImages = false;
                options.ElevationImages = false;
            }
        }

        public int Run()
        {
            try
            {
                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }

                if (options.TextureVariant == TextureVariant.Stretched)
                {
                    RunPhase("build stretched observation images", BuildStretchedObservationImages);
                }

                RunPhase("generate observation products", GenerateObservationProducts);

                if (options.MergedSiteDriveMeshes)
                {
                    RunPhase("generate merged site drive meshes", GenerateMergedSiteDriveMeshes);
                }

                if (options.MergedSiteDriveFrustumHullMeshes)
                {
                    RunPhase("generate merged site drive frustum hull meshes",
                             GenerateMergedSiteDriveFrustumHullMeshes);
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
            if (options.DisableImageCache)
            {
                pipeline.LogInfo("disabling LRU image cache");
                pipeline.SetImageCacheCapacity(0);
            }

            if (options.NoSave)
            {
                throw new Exception("--nosave not implemented for this command");
            }

            buildWedgeMeshes = !options.NoWedgeMeshes || options.MergedSiteDriveMeshes || options.StatsOnly;
            withTextures = buildWedgeMeshes && options.ColorMeshesBy == MeshColor.Texture;
            buildWedgeImages = withTextures || !options.NoWedgeImages;
            
            if (!ParseArgumentsAndLoadCaches("alignment/ObservationProducts"))
            {
                return false; // help
            }

            var phases = StringHelper.ParseList(options.OnlyForPhases).Select(p => p.ToLower()).ToList();

            var opts = new WedgeObservations.CollectOptions(options.OnlyForSiteDrives, options.OnlyForFrames,
                                                            options.OnlyForCameras, mission)
                {
                    RequireNormals = options.RequireNormals,
                    RequireTextures = options.RequireTextures,
                    IncludeForAlignment = phases.Count == 0 || phases.Contains("alignment"),
                    IncludeForMeshing = phases.Count == 0 || phases.Contains("meshing"),
                    IncludeForTexturing = phases.Count == 0 || phases.Contains("texturing"),
                    RequirePriorTransform = options.UsePriors,
                    RequireAdjustedTransform = options.OnlyAligned,
                    TargetFrame = meshFrame,
                    FilterMeshableWedgesForEye = RoverStereoPair.ParseEyeForGeometry(options.StereoEye, mission)
                }; 

            wedgeObservations = WedgeObservations.Collect(frameCache, observationCache, opts);

            if (!string.IsNullOrEmpty(options.OnlyForPoints))
            {
                onlyForPoints = new List<Vector3>();
                foreach (var pt in StringHelper.ParseList(options.OnlyForPoints, ';'))
                {
                    var xyz = StringHelper.ParseFloatListSafe(pt, ',');
                    onlyForPoints.Add(new Vector3(xyz[0], xyz[1], xyz[2]));
                }
            }

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
            var numPoints = new ConcurrentDictionary<string, int>();
            var numNormals = new ConcurrentDictionary<string, int>();
            var numTriangles = new ConcurrentDictionary<string, int>();
            var faceStats = new ConcurrentDictionary<string, Mesh.FaceStats>();
            var wedgeDecimation = new ConcurrentDictionary<string, int>();

            var meshOpts = new WedgeObservations.MeshOptions()
                {
                    Frame = meshFrame,
                    LoadedFrame = mission.GetTacticalMeshFrame(),
                    UsePriors = options.UsePriors,
                    OnlyAligned = options.OnlyAligned,
                    Decimate = options.DecimateWedgeMeshes,
                    NormalScale = options.NormalScale,
                    ApplyTexture = withTextures,
                    MaxTriangleAspect = options.MaxTriangleAspect,
                    IsolatedPointSize = options.IsolatedPointSize,
                    GenerateNormals = !options.NoGenerateNormals,
                    MeshDecimator = options.MeshDecimator,
                    AlwaysReconstruct = !options.UseMeshRDRs,
                    ReconstructionMethod = options.ReconstructionMethod
                };

            int np = 0, nc = 0;
            CoreLimitedParallel.ForEach(wedgeObservations, obs => { 

                Interlocked.Increment(ref np);

                string siteDrive = obs.SiteDrive.ToString();
                string sdPrefix = !options.SuppressSiteDriveDirectories ? siteDrive + "/" : "";

                //mesh decimation blocksize
                var mbsObs = (obs.HasMesh && options.UseMeshRDRs) ? obs.MeshObservation : obs.Points ?? obs.Range;
                int mbs = WedgeObservations.AutoDecimate(mbsObs, //null ok
                                                         options.DecimateWedgeMeshes,
                                                         options.TargetWedgeMeshResolution);
                wedgeDecimation[obs.FrameName] = mbs;

                var mo = meshOpts.Clone();
                mo.Decimate = mbs;

                if (mbsObs != null && mbsObs == obs.MeshObservation)
                {
                    mo.LoadedFrame = mission.GetTacticalMeshFrame(mbsObs.Name);
                }

                ConvexHull hull = null;
                if ((options.FrustumHullMeshes || options.MergedSiteDriveFrustumHullMeshes || onlyForPoints != null) &&
                    (obs.Texture != null || obs.Points != null))
                {
                    try
                    {
                        hull = obs.BuildFrustumHull(pipeline, frameCache, mo, uncertaintyInflated: false,
                                                    options.FrustumHullNearClip, options.FrustumHullFarClip,
                                                    options.FrustumHullForceLinear);
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error creating observation frustum hull: " + ex.Message);
                    }
                }

                if (hull != null && onlyForPoints != null)
                {
                    foreach (var pt in onlyForPoints)
                    {
                        if (!hull.Contains(pt))
                        {
                            //pipeline.LogVerbose("excluding {0}: frustum hull does not contain {1}", obs.Name, pt);
                            Interlocked.Decrement(ref np);
                            Interlocked.Increment(ref nc);
                            return;
                        }
                    }
                    pipeline.LogVerbose("including {0}: frustum hull contains all {1} specified points",
                                        obs.Name, onlyForPoints.Count);
                }

                if (!options.NoProgress)
                {
                    pipeline.LogInfo("computing products for {0} observations in parallel, completed {1}/{2}",
                                     np, nc, no);
                }
                
                if (hull != null && options.FrustumHullMeshes)
                {
                    SaveMesh(hull.Mesh, sdPrefix + "Frusta/" + obs.Name);
                }

                if (options.UncertaintyInflatedFrustumHullMeshes && (obs.Texture != null || obs.Points != null))
                {
                    try
                    {
                        var iHull = obs.BuildFrustumHull(pipeline, frameCache, mo, uncertaintyInflated: true,
                                                         options.FrustumHullNearClip, options.FrustumHullFarClip,
                                                         options.FrustumHullForceLinear);
                        SaveMesh(iHull.Mesh, sdPrefix + "InflatedFrusta/" + obs.Name);
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error creating uncertainty inflated hull mesh: " + ex.Message);
                    }
                }
                
                if (options.MergedSiteDriveFrustumHullMeshes && hull != null)
                {
                    var input = new Tuple<WedgeObservations, ConvexHull>(obs, hull);
                    mergeHulls
                        .AddOrUpdate(siteDrive,
                                     _ => new ConcurrentBag<Tuple<WedgeObservations, ConvexHull>>(new [] { input }),
                                     (_, bag) => { bag.Add(input); return bag; });
                }

                int npts = 0, nn = 0, nt = 0;
                Mesh mesh = null;
                if (buildWedgeMeshes && ((options.UseMeshRDRs && obs.Meshable) ||
                                         (!options.UseMeshRDRs && obs.Reconstructable)))
                {
                    if (mbs > 1 && mbs != options.DecimateWedgeMeshes)
                    {
                        pipeline.LogVerbose("auto decimating wedge mesh {0} with blocksize {1}", obs.Name, mbs);
                    }

                    Exception ex = null;
                    try
                    {
                        mesh = BuildWedgeMesh(obs, mo);
                    }
                    catch (Exception e)
                    {
                        ex = e;
                    }

                    //try to count valid points and normals in the observation images now
                    //after the mesh has been built because that would have loaded the points and normals images
                    //however they may not have been loaded if the mesh itself was loaded from disk (e.g. IV or OBJ)
                    //but in that case the best we can do is use the vertex count of the mesh itself
                    obs.CountValid(out npts, out nn);

                    if (mesh != null)
                    {
                        if (npts == 0)
                        {
                            npts = mesh.Vertices.Count;
                        }
                        if (nn == 0 && mesh.HasNormals)
                        {
                            nn = mesh.Vertices.Count;
                        }
                        nt = mesh.Faces.Count;
                        pipeline.LogVerbose("collecting face stats for {0}", obs.Name);
                        faceStats[obs.FrameName] = mesh.CollectFaceStats();
                    }
                    else
                    {
                        pipeline.LogWarn("meshing failed on obs {0} ({1} reconstruction, {2} points, {3} normals): {4}",
                                         obs.Name, options.ReconstructionMethod, npts, nn,
                                         ex != null ? ex.Message : "insufficient data or unknown error");
                        if (pipeline.StackTraces && ex != null)
                        {
                            pipeline.LogException(ex);
                        }
                    }
                }

                numPoints[obs.FrameName] = npts;
                numNormals[obs.FrameName] = nn;
                numTriangles[obs.FrameName] = nt;

                int ibs = WedgeObservations.AutoDecimate(obs.Texture, //null ok
                                                         options.DecimateWedgeImages,
                                                         options.TargetWedgeImageResolution);

                Image img = null;
                if (buildWedgeImages && obs.Texture != null)
                {
                    try
                    {
                        img = BuildWedgeImage(obs, ibs);
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error building wedge image: " + ex.Message);
                    }
                }

                if (options.MaskImages && !obs.Empty)
                {
                    try
                    {
                        SaveImage(BuildMaskImage(obs, ibs), sdPrefix + obs.Name + "_mask");
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error building or saving mask image: " + ex.Message);
                    }
                }

                if (img != null && !options.NoWedgeImages)
                {
                    try
                    {
                        SaveImage(img, sdPrefix + obs.Name);
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error saving image: " + ex.Message);
                    }
                }

                //save the wedge mesh now that we have both it and its texture
                if (mesh != null && !options.NoWedgeMeshes)
                {
                    try
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
                                         stretch: options.StretchMode == StretchMode.StandardDeviation,
                                         nStddev: options.StretchNumStdDev);
                        }
                        SaveMesh(mesh, sdPrefix + obs.Name,
                                 (withTextures && img != null) ? (obs.Name + imageExt) : null);
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error saving mesh: " + ex.Message);
                    }
                }

                if (mesh != null && options.MergedSiteDriveMeshes)
                {
                    var input = new Tuple<WedgeObservations, Mesh, Image>(obs, mesh, withTextures ? img : null);
                    mergeWedges
                    .AddOrUpdate(siteDrive,
                                 _ => new ConcurrentBag<Tuple<WedgeObservations, Mesh, Image>>(new [] { input }),
                                 (_, bag) => { bag.Add(input); return bag; });
                }
                
                Image mask = null;

                if (options.NormalsImages && obs.Normals != null)
                {
                    try
                    {
                        string kind = options.ConvertNormalsToTilts ? "Tilts" : "Normals";
                        var ni = BuildNormalsImage(obs, mbs, ref mask);
                        FinishImage(ni, mask, mbs, sdPrefix + obs.Name, kind);
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
                        var ci = BuildCurvaturesImage(obs, mbs, ref mask);
                        FinishImage(ci, mask, mbs, sdPrefix + obs.Name, "Curvature");
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
                        var ei = BuildElevationsImage(obs, mbs, ref mask);
                        FinishImage(ei, mask, mbs, sdPrefix + obs.Name, "Elevation");
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error creating elevation image: " + ex.Message);
                    }
                }

                Interlocked.Decrement(ref np);
                Interlocked.Increment(ref nc);
            });

            foreach (var obs in wedgeObservations)
            {
                var fn = obs.FrameName;
                if (buildWedgeMeshes && numPoints.ContainsKey(fn))
                {
                    pipeline.LogInfo("{0}: {1} points, {2} normals, {3} triangles{4}{5}{6}{7}",
                                     fn, numPoints[fn], numNormals[fn], numTriangles[fn],
                                     wedgeDecimation[fn] > 1 ?
                                     string.Format(" ({0}x decimation)", wedgeDecimation[fn]) : "",
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

        private Mesh BuildWedgeMesh(WedgeObservations obs, WedgeObservations.MeshOptions mo)
        {
            if (options.PointCloud)
            {
                pipeline.LogVerbose("building point cloud for {0}", obs.Name);
                return obs.BuildPointCloud(pipeline, frameCache, masker, mo);
            }
            else
            {
                pipeline.LogVerbose("building triangle mesh for {0}", obs.Name);
                return obs.BuildMesh(pipeline, frameCache, masker, mo);
            }
        }

        private Image BuildWedgeImage(WedgeObservations obs, int ibs)
        {
            Image img = null;
            try
            {
                var textureVariant = obs.Texture.GetTextureVariantWithFallback(options.TextureVariant);
                if (textureVariant != TextureVariant.Original)
                {
                    var guid = obs.Texture.GetTextureVariantGuid(textureVariant);
                    img = pipeline.GetDataProduct<PngDataProduct>(project, guid, noCache: true).Image;
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
                Image scale = null;
                PDSImage points = new PDSImage(pipeline.LoadImage(obs.Points.Url));
                switch (options.NormalScale)
                {
                    case NormalScale.Confidence: scale = points.GenerateConfidence(); break;
                    case NormalScale.PointScale: scale = points.GenerateScale(); break;
                    case NormalScale.None: break;
                    default: throw new ArgumentException("unknown normal scaling mode " + options.NormalScale);
                }
                normals = (new PDSImage(normals)).ConvertNormals(scale, points.ConvertPoints());
                if (normals != null)
                {
                    normals = OrganizedPointCloud.MaskAndDecimateNormals(normals, mbs, mask, normalize: true);
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
                    normals = OrganizedPointCloud.MaskAndDecimateNormals(normals, mbs, mask, normalize: true);
                    curvatures = OrganizedPointCloud.Curvatures(points, normals, normalize: true,
                                                                neighborhood: options.CurvatureNeighborhood);
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
                    elevations = OrganizedPointCloud.Elevations(points, normalize: true);
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
            pipeline.LogInfo("generating merged meshes for {0} sitedrives", mergeWedges.Count);

            foreach (var siteDrive in mergeWedges.Keys.OrderBy(name => name))
            {
                //ensure inputs are in a canonical order
                var inputs = mergeWedges[siteDrive]
                    .OrderBy(inp => inp.Item1.FrameName) //order by observation frame
                    .Distinct() //ConcurrentBag is not necessarily a set
                    .ToArray();

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
                                 siteDrive, inputs.Length, options.StereoEye, hasNormals, hasTextures,
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
                        var pair =
                            MeshMerge.MergeMeshesAndTextures(inputs
                                                             .Select(t => new Tuple<Mesh, Image>(t.Item2, t.Item3))
                                                             .ToArray());
                        mesh = pair.Item1;
                        img = pair.Item2;
                    }
                    else
                    {
                        mesh = MeshMerge.Merge(inputs.Select(pr => pr.Item2).ToArray());
                    }

                    if (mesh == null)
                    {
                        throw new Exception("failed to generate merged mesh");
                    }
                
                    if (img == null && options.ColorMeshesBy != MeshColor.None)
                    {
                        mesh.ColorBy(options.ColorMeshesBy,
                                     options.ConvertNormalsToTilts ? options.TiltMode : TiltMode.None,
                                     allowAdjustColors: true,
                                     stretch: options.StretchMode == StretchMode.StandardDeviation,
                                     nStddev: options.StretchNumStdDev);
                    }
                    
                    if (mesh.HasVertices && (options.PointCloud || mesh.HasFaces))
                    {
                        if (img != null)
                        {
                            SaveImage(img, siteDrive);
                        }
                        SaveMesh(mesh, siteDrive, img != null ? (siteDrive + imageExt) : null);
                    }
                }
                catch (Exception ex)
                {
                    pipeline.LogWarn("error creating merged mesh for site drive {0}: {1}", siteDrive, ex.Message);
                }
            }
        }

        private void GenerateMergedSiteDriveFrustumHullMeshes()
        {
            pipeline.LogInfo("generating merged frustum hull meshes for {0} sitedrives", mergeHulls.Count);

            foreach (var siteDrive in mergeHulls.Keys.OrderBy(name => name))
            {
                //ensure inputs are in a canonical order
                var inputs = mergeHulls[siteDrive]
                    .OrderBy(inp => inp.Item1.FrameName) //order by observation frame
                    .Distinct() //ConcurrentBag is not necessarily a set
                    .ToArray();

                pipeline.LogInfo("generating merged frustum hull mesh for site drive {0} from {1} observations",
                                 siteDrive, inputs.Length);
                
                var mesh = MeshMerge.Join(inputs.Select(pr => pr.Item2.Mesh).ToArray());
                SaveMesh(mesh, siteDrive + "_hulls");
            }
        }

        private void FinishImage(Image img, Image mask, int decimateBlocksize, string name, string kind)
        {
            if (img == null)
            {
                return;
            }

            if (options.StretchMode == StretchMode.StandardDeviation)
            {
                img.ApplyStdDevStretch(options.StretchNumStdDev);
            }
            else if (options.StretchMode == StretchMode.HistogramPercent)
            {
                img.HistogramPercentStretch();
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
