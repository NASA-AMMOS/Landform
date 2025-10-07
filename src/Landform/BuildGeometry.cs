//#define DBG_UV
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using CommandLine;
using Microsoft.Xna.Framework;
using RTree;
using JPLOPS.Util;
using JPLOPS.MathExtensions;
using JPLOPS.Geometry;
using JPLOPS.Imaging;
using JPLOPS.Pipeline;
using JPLOPS.Pipeline.AlignmentServer;
using System.IO;

/// <summary>
/// Reconstructs scene geometry from observation point clouds in a Landform contextual mesh workflow.
///
/// Runs after all alignment stages (e.g. bev-align, heightmap-align), but before build-tiling-input.
///
/// The mesh frame is typically the frame of the primary sitedrive.  Detailed reconstruction incorporating both surface
/// observations and orbital data, if available, is performed within a square bounding box centered on the origin of
/// mesh frame.  If orbital data is available, coarser reconstruction may also be performed outside that bounds to a
/// larger square bounds.  The inner surface bounds are typically auto expanded to fit the available surface point
/// clouds within the range 64-256m.  The outer bounds is typically set at 1024m.
///
/// The observation pointclouds are typically combined with CleverCombine which attempts to reject outlier points using
/// a grid-based approach, and which also limits the total number of samples per XY grid cell.  Grid cells are typically
/// 2.5cm square, and the limit is typically 6 samples per cell, or about 1 sample per square cm.  Orbital sample points
/// are typically also added at a sampling rate of 8 points per lineal meter.  This both fills holes in the observation
/// pointclouds and defines a square boundary for the input point cloud, which sets up better boundary conditions for
/// mesh reconstruction.
///
/// The mesh is then reconstructed on the combined point cloud, typically with Poisson reconstruction.  Mission normal
/// map RDRs (UVW products) give each point a normal vector, which is usually required.  Points with bad or suspected
/// bad normals are filtered before reconstruction.  The normals are typically scaled by an estimate of the confidence
/// of each point, though at this time ingestion of mission RNE products is still TODO (and such products may not even
/// be available) so we use distance from the camera as a proxy.  Orbital sample points have normals computed from a
/// corresponding organized mesh and a fixed confidence.
///
/// The reconstructed mesh is cleaned and clipped to the surface bounds.  Its vertex normals are recomputed from its
/// faces to avoid issues with bad normals corrupting downstream operations such as reconstruction of parent tile
/// meshes.
///
/// An optional hole filling step is then performed.  This step is typically enabled only if orbital data is not used to
/// fill holes.  The hole filling algorithm first computes an outer polygon boundary of the surface data.  The
/// reconstructed surface mesh is then trimmed again, but this time with less aggressive trimming options.  The
/// resulting mesh is clipped to the polygon boundary.  In this way the potential undesirable effects of less aggressive
/// surface trimming around the outer boundary of the mesh are avoided, but the benefits of allowing more internal hole
/// filling are gained.
///
/// If the total extent is larger than the orbital fine mesh extent, then a coarse orbital mesh is also added, typically
/// sampled at the native resolution of the orbital DEM.
///
/// The resulting mesh is always saved to project storage as a PlyGZDataProduct, with metadata in a SceneMesh object.
///
/// The scene mesh will always have normals but will typically not have texture coordinates.  Because it is usually
/// large and its topology can be complex it can be non-trivial to atlas it.  In the typical contextual mesh workflow
/// this is handled by only atlasing the leaf and parent tile meshes, which are typically much smaller.  Atlasing of the
/// full scene mesh can be attempted by specifying --generateuvs.
///
/// If a tileset is not required the full scene mesh can also be directly saved by specifying an output mesh as the
/// second positional command line argument.  This can be either a relative or absolute disk path with an accepted mesh
/// file extension, or just the extension, in which case a default filename will be used in the current working
/// directory.  The output scene mesh will not be textured.  However, if atlasing was successful, then a textured mesh
/// can be generated with build-texture.
///
/// Example:
///
/// Landform.exe build-geometry windjana
///
/// </summary>
namespace JPLOPS.Landform
{
    public enum OrbitalFillAdjust { None, Min, Max, Med };

    [Verb("build-geometry", HelpText = "create scene mesh from point clouds")]
    [EnvVar("GEOMETRY")]
    public class BuildGeometryOptions : GeometryCommandOptions
    {
        [Value(1, Required = false, HelpText = "URL, file, or file type (extension starting with \".\") to which to save scene mesh", Default = null)]
        public string OutputMesh { get; set; }

        [Option(HelpText = "Decimate the scene mesh to this target number of faces if positive", Default = 0)]
        public int TargetSceneMeshFaces { get; set; }

        [Option(HelpText = "Decimate the surface mesh to this target number of faces if positive", Default = BuildGeometry.DEF_TARGET_SURFACE_MESH_FACES)]
        public int TargetSurfaceMeshFaces { get; set; }

        [Option(HelpText = "Filter out surface reconstruction triangles whose barycenter is further than this from any input point, disabled if non-positive", Default = 0)]
        public double FilterTriangles { get; set; }

        [Option(HelpText = "Text file with wedge product IDs to whitelist, one per line.  Wedges that do not have a points observation (falling back to range) in the whitelist are not used for meshing.  Each line may also contain an optional alternate URL to use for that product, separated by whitespace from the product ID, and that mechanism can be used to replace the points, range, normal, or mask product of any wedge.", Default = null)]
        public string WedgeWhitelist { get; set; }

        [Option(HelpText = "Mesh reconstruction method (FSSR, Poisson)", Default = MeshReconstructionMethod.Poisson)]
        public MeshReconstructionMethod ReconstructionMethod { get; set; }

        [Option(HelpText = "Stereo eye to prefer (auto, left, right, any)", Default = "auto")]
        public string StereoEye { get; set; }

        [Option(HelpText = "Only include faces that intersect these observations, comma separated", Default = null)]
        public string OnlyFacesForObs { get; set; }

        [Option(HelpText = "Apply alternate wedge mesh decimation settings to observations with hulls that include at least one of these points. Semicolon separated list of X,Y,Z points in scene frame", Default = null)]
        public string AlternateWedgeMeshDecimationPoints { get; set; }

        [Option(HelpText = "Alternate wedge mesh decimation blocksize, 0 to disable, -1 for auto", Default = -1)]
        public virtual int AlternateWedgeMeshDecimation { get; set; }

        [Option(HelpText = "Alternate wedge mesh auto decimation target resolution", Default = 4096)]
        public virtual int AlternateTargetWedgeMeshResolution { get; set; }

        [Option(HelpText = "Pre-clip observation point clouds to XY box of this size in meters around mesh frame origin if positive", Default = 0)]
        public double PreClipPointCloudExtent { get; set; }

        [Option(HelpText = "Discard observation point cloud normals with fewer than this many valid 8-neighbors", Default = BuildGeometry.DEF_NORMAL_FILTER)]
        public int NormalFilter { get; set; }

        [Option(HelpText = "Flip downward facing normals", Default = false)]
        public bool FlipDownwardFacingNormals { get; set; }

        [Option(HelpText = "Disable clever combine point cloud merging", Default = false)]
        public bool NoCleverCombine { get; set; }

        [Option(HelpText = "Apply clever combine to individual observations within sitedrives", Default = false)]
        public bool IntraSitedriveCleverCombine { get; set; }

        [Option(HelpText = "Clever combine cell size (meters)", Default = CleverCombine.DEF_CELL_SIZE)]
        public double CleverCombineCellSize { get; set; }

        [Option(HelpText = "Clever combine cell aspect (height relative to width)", Default = CleverCombine.DEF_CELL_ASPECT)]
        public double CleverCombineCellAspect { get; set; }

        [Option(HelpText = "Clever combine max points per cell", Default = CleverCombine.DEF_MAX_POINTS_PER_CELL)]
        public int CleverCombineMaxPointsPerCell { get; set; }

        [Option(HelpText = "Disable peripheral orbital mesh", Default = false)]
        public bool NoPeripheralOrbital { get; set; }

        [Option(HelpText = "Final clip box XY size in meters, 0 to clip to aggregate point cloud bounds", Default = BuildGeometry.DEF_EXTENT)]
        public double Extent { get; set; }

        [Option(HelpText = "Clip reconstructed surface to square XY box of (at least) this size in meters around mesh frame origin if positive", Default = BuildGeometry.DEF_SURFACE_EXTENT)]
        public double SurfaceExtent { get; set; }

        [Option(HelpText = "Don't expand surface extent to fit surface data point cloud bounds", Default = false)]
        public bool NoAutoExpandSurfaceExtent { get; set; }

        [Option(HelpText = "Max auto surface extent, non-positive to disable limit", Default = BuildGeometry.DEF_MAX_AUTO_SURFACE_EXTENT)]
        public double MaxAutoSurfaceExtent { get; set; }

        [Option(HelpText = "If surface extent was auto expanded, save the original value with the scene mesh for use in tiling", Default = false)]
        public bool UseExpandedSurfaceExtentForTiling { get; set; }

        [Option(HelpText = "Don't fill surface extent with orbital samples to cover holes and make a square reconstruction boundary", Default = false)]
        public bool NoOrbitalFill { get; set; }

        [Option(HelpText = "Extend surface extent by this amount for orbital fill to give better boundary conditions for surface reconstruction, 0 to disable", Default = BuildGeometry.DEF_ORBITAL_FILL_PADDING)]
        public double OrbitalFillPadding { get; set; }

        [Option(HelpText = "Orbital sampling rate to fill holes, negative to use DEM resolution, 0 to disable", Default = BuildGeometry.DEF_ORBITAL_FILL_POINTS_PER_METER)]
        public double OrbitalFillPointsPerMeter { get; set; }

        [Option(HelpText = "Orbital sampling confidence to fill holes", Default = BuildGeometry.DEF_ORBITAL_FILL_POISSON_CONFIDENCE)]
        public double OrbitalFillPoissonConfidence { get; set; }

        [Option(HelpText = "Orbital fill adjust mode (None, Min, Max, Med)", Default = BuildGeometry.DEF_ORBITAL_FILL_ADJUST)]
        public OrbitalFillAdjust OrbitalFillAdjust { get; set; }

        [Option(HelpText = "Orbital fill adjust blend factor", Default = BuildGeometry.DEF_ORBITAL_FILL_ADJUST_BLEND)]
        public double OrbitalFillAdjustBlend { get; set; }

        [Option(HelpText = "Orbital fill adjust infill width, 0 to disable, negative for unlimited", Default = BuildGeometry.DEF_ORBITAL_FILL_ADJUST_WIDTH)]
        public int OrbitalFillAdjustWidth { get; set; }

        [Option(HelpText = "If positive, linearize point sample confidence from 1 to this min", Default = BuildGeometry.DEF_LINEAR_MIN_POISSON_CONFIDENCE)]
        public double LinearMinPoissonConfidence { get; set; }

        [Option(HelpText = "Scale observation point cloud normals by confidence and then apply this exponent in Poisson reconstruction, 0 disables", Default = PoissonReconstruction.DEF_CONFIDENCE_EXPONENT)]
        public double PoissonConfidenceExponent { get; set; }

        [Option(HelpText = "Resolution for creating surface mask when not using orbital for hole filling", Default = BuildGeometry.DEF_SURFACE_MASK_POINTS_PER_METER)]
        public double SurfaceMaskPointsPerMeter { get; set; }

        [Option(HelpText = "Offset for creating surface mask when not using orbital for hole filling", Default = BuildGeometry.DEF_SURFACE_MASK_OFFSET)]
        public double SurfaceMaskOffset { get; set; }

        [Option(HelpText = "Expand point cloud envelope by this amount", Default = TilingDefaults.PARENT_CLIP_BOUNDS_EXPAND_HEIGHT)]
        public double EnvelopePadding { get; set; }

        [Option(HelpText = "Poisson cell size (meters), mutually exclusive with PoissonTreeDepth, 0 to disable", Default = PoissonReconstruction.DEF_MIN_OCTREE_CELL_WIDTH_METERS)]
        public double PoissonCellSize { get; set; }

        [Option(HelpText = "Poisson octtree depth, mutually exclusive with PoissonCellSize, 0 to disable", Default = PoissonReconstruction.DEF_OCTREE_DEPTH)]
        public int PoissonTreeDepth { get; set; }

        [Option(HelpText = "Pass combined cloud envelop to Poisson", Default = false)]
        public bool PassEnvelopeToPoisson { get; set; }

        [Option(HelpText = "Clip to envelope after Poisson reconstruction but before surface trimming", Default = false)]
        public bool NoPoissonClipToEnvelope { get; set; }

        [Option(HelpText = "Don't remove islands after Poisson reconstruction but before surface trimming", Default = false)]
        public bool NoPoissonRemoveIslands { get; set; }

        [Option(HelpText = "Remove islands whose bounding box diameter is less than this ratio of the max island bounding box diameter", Default = BuildGeometry.DEF_MIN_ISLAND_RATIO)]
        public double MinIslandRatio { get; set; }

        [Option(HelpText = "Poisson reconstruction boundary type, one of Free, Dirichlet, Neumann", Default = PoissonReconstruction.DEF_BOUNDARY_TYPE)]
        public PoissonReconstruction.BoundaryType PoissonBoundaryType { get; set; }

        [Option(HelpText = "Min required samples per octree cell in Poisson reconstruction, higher for noiser data", Default = PoissonReconstruction.DEF_MIN_OCTREE_SAMPLES_PER_CELL)]
        public int PoissonMinSamplesPerCell { get; set; }

        [Option(HelpText = "Poisson reconstruction B-spline degree", Default = PoissonReconstruction.DEF_BSPLINE_DEGREE)]
        public int PoissonBSplineDegree { get; set; }

        [Option(HelpText = "Poisson trimmer octree level (higher means more aggressive, 0 disables)", Default = PoissonReconstruction.DEF_TRIMMER_LEVEL)]
        public double PoissonTrimmerLevel { get; set; }

        [Option(HelpText = "Poisson trimmer octree lenient level when not using orbital to fill holes (higher means more aggressive, 0 disables)", Default = PoissonReconstruction.DEF_TRIMMER_LEVEL_LENIENT)]
        public double PoissonTrimmerLevelLenient { get; set; }

        [Option(HelpText = "FSSR global scale, negative to auto-compute, 0 to use individual point scales", Default = 0)]
        public double FSSRScale { get; set; }

        [Option(HelpText = "Generate full-mesh UVs", Default = false)]
        public bool GenerateUVs { get; set; }

        [Option(HelpText = "UV generation mode for meshes if texture projection is not available (None, UVAtlas, Heightmap, Naive)", Default = AtlasMode.UVAtlas)]
        public override AtlasMode AtlasMode { get; set; }
    }

    public class BuildGeometry : GeometryCommand
    {
        private const string OUT_DIR = "meshing/GeometryProducts";

        public const double DEF_EXTENT = 1024;
        public const double DEF_SURFACE_EXTENT = 64;
        public const double DEF_MAX_AUTO_SURFACE_EXTENT = 256;
        public const int DEF_NORMAL_FILTER = 8;
        public const double DEF_MIN_ISLAND_RATIO = 0.2;
        public const int DEF_TARGET_SURFACE_MESH_FACES = 2000000;
        public const double DEF_ORBITAL_FILL_PADDING = 2;
        public const int DEF_ORBITAL_FILL_POINTS_PER_METER = 8;
        public const double DEF_ORBITAL_FILL_POISSON_CONFIDENCE = 0.5;
        public const OrbitalFillAdjust DEF_ORBITAL_FILL_ADJUST = OrbitalFillAdjust.Med;
        public const double DEF_ORBITAL_FILL_ADJUST_BLEND = 0.9;
        public const int DEF_ORBITAL_FILL_ADJUST_WIDTH = -1;
        public const double DEF_LINEAR_MIN_POISSON_CONFIDENCE = 0.1;
        public const int DEF_SURFACE_MASK_POINTS_PER_METER = 2;
        public const double DEF_SURFACE_MASK_OFFSET = 0.05;

        public const double OBS_CLOUD_MERGE_EPS = 0.005;
        public const double SURFACE_HULL_MERGE_EPS = 0.1;
        public const double SITEDRIVE_MERGE_EPS = 0.01;
        public const double CROSS_SITEDRIVE_MERGE_EPS = 0.01;
        public const int SURFACE_HULL_FILL_HOLES = 10;
        public const double SURFACE_OVERLAP_ORBITAL = 0.1;

        private BuildGeometryOptions options;

        private int dbgMeshCount;

        private RoverObservation[] onlyForObs;

        private List<Vector3> alternateWedgeMeshDecimationPoints;

        private Dictionary<string, string> wedgeWhitelist;

        private PoissonReconstruction.Options poissonOpts;

        private WedgeObservations.MeshOptions wedgeMeshOpts;

        private ConcurrentDictionary<string, Mesh> observationPointClouds = new ConcurrentDictionary<string, Mesh>();
        private Mesh orbitalFillPointCloud;

        private Mesh pointCloud;

        private BoundingBox surfaceBounds;

        private Mesh mesh, untrimmedMesh, orbitalMesh;

        private double originalSurfaceExtent;

        private int orbitalFillSamplesPerPixel;

        private Matrix meshToOrbital, orbitalToMesh;

        private float[][] sourceColors;

        public BuildGeometry(BuildGeometryOptions options) : base(options)
        {
            this.options = options;
        }

        public int Run()
        {
            try
            {
                if (!ParseArgumentsAndLoadCaches())
                {
                    return 0; //help
                }

                if (!options.NoSurface)
                {
                    RunPhase("build observation point clouds", MakeObservationPointClouds);
                    RunPhase("clear LRU image cache", ClearImageCache);
                    if (!options.NoOrbital && !options.NoOrbitalFill && orbitalFillSamplesPerPixel > 0)
                    {
                        RunPhase("build orbital fill point cloud", MakeOrbitalFillPointCloud);
                    }
                    RunPhase("merge point clouds", MergePointClouds);
                    RunPhase("reconstruct mesh", ReconstructMesh);
                    if (options.FilterTriangles > 0)
                    {
                        RunPhase("filter reconstructed mesh", FilterReconstructedMesh);
                    }
                    if (orbitalFillPointCloud == null &&
                        options.ReconstructionMethod == MeshReconstructionMethod.Poisson &&
                        options.PoissonTrimmerLevelLenient < options.PoissonTrimmerLevel)
                    {
                        RunPhase("retrim surface", RetrimSurface); //alternate hole filling approach
                    }
                    RunPhase("clip surface mesh", ClipAndCleanSurfaceMesh);
                }

                if (!options.NoOrbital)
                {
                    if (options.NoSurface || (options.Extent > options.SurfaceExtent && !options.NoPeripheralOrbital))
                    {
                        RunPhase("build orbital mesh", MakeOrbitalMesh);
                    }
                    if (options.NoSurface || mesh == null)
                    {
                        mesh = orbitalMesh;
                    }
                    else if (orbitalMesh != null)
                    {
                        mesh.MergeWith(orbitalMesh);
                    }
                }

                if (options.TargetSceneMeshFaces > 0)
                {
                    RunPhase("decimate mesh", DecimateMesh);
                }

                if (onlyForObs.Length > 0)
                {
                    RunPhase("reduce mesh to specified observations", ClipMeshToObservations);
                }

                if (options.GenerateUVs && !mesh.HasUVs)
                {
                    RunPhase("atlas mesh", AtlasMesh);
                }

                if (!options.NoSave)
                {
                    RunPhase("save mesh", SaveSceneMesh);
                }

                SaveDebugMesh(mesh, "final");

                var bounds = mesh.Bounds().Extent();
                pipeline.LogInfo("scene bounds (meters): {0:f3}x{1:f3}x{2:f3}", bounds.X, bounds.Y, bounds.Z);
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
            if (options.ReconstructionMethod != MeshReconstructionMethod.FSSR &&
                options.ReconstructionMethod != MeshReconstructionMethod.Poisson)
            {
                throw new Exception("unsupported mesh reconstruction method: " + options.ReconstructionMethod);
            }

            if (options.NormalFilter < 0 || options.NormalFilter > 8)
            {
                throw new Exception("--normalfilter must be between 0 and 8");
            }

            if (!base.ParseArgumentsAndLoadCaches(OUT_DIR))
            {
                return false; //help
            }

            if (!options.NoOrbital && !SiteDrive.IsSiteDriveString(meshFrame))
            {
                pipeline.LogWarn("mesh frame \"{0}\" is not a site drive, disabling orbital", meshFrame);
                options.NoOrbital = true;
            }

            if (options.SurfaceExtent == 0)
            {
                options.NoSurface = true;
            }
            originalSurfaceExtent = options.SurfaceExtent;

            if (!options.NoOrbital)
            {
                options.NoOrbital |= !LoadOrbitalDEM();
                if (options.NoOrbital && options.NoSurface)
                {
                    throw new Exception("--nosurface but failed to load orbital");
                }
            }

            onlyForObs = observationCache.ParseList(options.OnlyFacesForObs)
                .Where(obs => obs is RoverObservation)
                .Cast<RoverObservation>()
                .ToArray();

            if (!string.IsNullOrEmpty(options.AlternateWedgeMeshDecimationPoints))
            {
                alternateWedgeMeshDecimationPoints = new List<Vector3>();
                foreach (var pt in StringHelper.ParseList(options.AlternateWedgeMeshDecimationPoints, ';'))
                {
                    var xyz = StringHelper.ParseFloatListSafe(pt, ',');
                    alternateWedgeMeshDecimationPoints.Add(new Vector3(xyz[0], xyz[1], xyz[2]));
                }
            }

            if (!string.IsNullOrEmpty(options.WedgeWhitelist))
            {
                wedgeWhitelist = new Dictionary<string, string>();
                foreach (string line in File.ReadLines(options.WedgeWhitelist))
                {
                    string[] split = line.Split(null); //split on whitespace
                    if (split.Length > 0)
                    {
                        wedgeWhitelist[split[0]] = split.Length > 1 ? split[1] : "";
                    }
                }
                pipeline.LogInfo("read wedge whitelist {0} with {1} entries:",
                                 options.WedgeWhitelist, wedgeWhitelist.Count);
                foreach (var entry in wedgeWhitelist)
                {
                    pipeline.LogInfo("whitelisting {0}{1}", entry.Key,
                                     !string.IsNullOrEmpty(entry.Value) ? $" (replacement {entry.Value})" : "");
                }
            }

            poissonOpts = new PoissonReconstruction.Options
            {
                Boundary = options.PoissonBoundaryType,
                MinOctreeCellWidthMeters = options.PoissonCellSize,
                OctreeDepth = options.PoissonTreeDepth,
                MinOctreeSamplesPerCell = options.PoissonMinSamplesPerCell,
                BSplineDegree = options.PoissonBSplineDegree,
                ConfidenceExponent = options.PoissonConfidenceExponent,
                TrimmerLevel = options.PoissonTrimmerLevel,
                PassEnvelopeToPoisson = options.PassEnvelopeToPoisson,
                ClipToEnvelope = !options.NoPoissonClipToEnvelope,
                MinIslandRatio = !options.NoPoissonRemoveIslands ? options.MinIslandRatio : 0,
                PreserveInputsOnError = true,
                PreserveInputsOverrideFolder = Path.GetTempPath(), //system temp doesn't get wiped, ends with \
                PreserveInputsOverrideName = "landform" //don't preserve more than one set of debug files
            };

            if (!string.IsNullOrEmpty(options.OutputMesh))
            {
                options.OutputMesh =
                    CheckOutputURL(options.OutputMesh, project.Name, OUT_DIR, MeshSerializers.Instance);
            }

            orbitalFillSamplesPerPixel = 0;
            if (options.OrbitalFillPointsPerMeter > 0)
            {
                orbitalFillSamplesPerPixel =
                    (int)Math.Ceiling(options.OrbitalFillPointsPerMeter * orbitalDEMMetersPerPixel);
            }
            else if (options.OrbitalFillPointsPerMeter < 0)
            {
                orbitalFillSamplesPerPixel = 1;
            }

            if (!options.NoOrbital)
            {
                var meshToRoot = frameCache.GetBestTransform(meshFrame).Transform.Mean;
                orbitalToMesh = orbitalDEMToRoot * Matrix.Invert(meshToRoot);
                meshToOrbital = meshToRoot * Matrix.Invert(orbitalDEMToRoot);
            }

            wedgeMeshOpts = new WedgeObservations.MeshOptions()
            {
                Frame = meshFrame,
                NormalFilter = options.NormalFilter,
                NoCacheTextureImages = true,
                NoCacheGeometryImages = true
            };

            if ((options.ReconstructionMethod == MeshReconstructionMethod.Poisson) &&
                (options.PoissonConfidenceExponent != 0))
            {
                wedgeMeshOpts.NormalScale = NormalScale.Confidence;
            }

            if ((options.ReconstructionMethod == MeshReconstructionMethod.FSSR) &&
                (options.FSSRScale == 0))
            {
                wedgeMeshOpts.NormalScale = NormalScale.PointScale;
            }

            return true;
        }

        protected override bool ObservationFilter(RoverObservation obs)
        {
            return obs.UseForMeshing;
        }

        protected override string DescribeObservationFilter()
        {
            return " meshing";
        }

        //potentially mission specific: assumes Z axis is vertical
        private BoundingBox BoundsFromXYExtent(Vector3 center, double extent, double minZ, double maxZ)
        {
            double halfExtent = extent * 0.5;
            Vector3 min = center + new Vector3(-halfExtent, -halfExtent, 0);
            Vector3 max = center + new Vector3(halfExtent, halfExtent, 0);
            min.Z = minZ;
            max.Z = maxZ;
            return new BoundingBox(min, max);
        }

        private void MakeObservationPointClouds()
        {
            var collectOpts = new WedgeObservations.CollectOptions(null, null, options.OnlyForCameras, mission)
                {
                    RequireReconstructable = true,
                    RequireNormals = true,
                    RequireTextures = false,
                    IncludeForAlignment = false,
                    IncludeForMeshing = true,
                    IncludeForTexturing = false,
                    RequirePriorTransform = options.UsePriors,
                    RequireAdjustedTransform = options.OnlyAligned,
                    TargetFrame = meshFrame,
                    FilterMeshableWedgesForEye = RoverStereoPair.ParseEyeForGeometry(options.StereoEye, mission)
                };

            var wedges = WedgeObservations.Collect(frameCache, observationCache, collectOpts);

            if (wedgeWhitelist != null)
            {
                var keepers = new List<WedgeObservations>();
                foreach (var w in wedges)
                {
                    bool whitelisted = false;
                    foreach (var obs in new List<Observation>() { w.Points, w.Range, w.Normals, w.Mask, w.Texture })
                    {
                        if (obs != null)
                        {
                            string id = StringHelper.GetLastUrlPathSegment(obs.Url, stripExtension: true);
                            if (wedgeWhitelist.ContainsKey(id))
                            {
                                string url = wedgeWhitelist[id];
                                if (!string.IsNullOrEmpty(url))
                                {
                                    obs.Url = url;
                                    pipeline.LogInfo("replacing URL for {0} with {1} from whitelist", id, url);
                                }
                                if (obs == w.Points || (w.Points == null && obs == w.Range))
                                {
                                    pipeline.LogInfo("whitelisting {0}", id);
                                    keepers.Add(w);
                                    whitelisted = true;
                                }
                            }
                        }
                    }
                    if (!whitelisted)
                    {
                        string pid = StringHelper.GetLastUrlPathSegment(w.Points != null ? w.Points.Url : w.Range.Url,
                                                                       stripExtension: true);
                        pipeline.LogInfo("discarding wedge {0}: not in whitelist", pid);
                    }
                }
                wedges = keepers;
            }

            if (wedges.Count == 0)
            {
                pipeline.LogError("no wedge observations");
            }

            int no = wedges.Count;
            pipeline.LogInfo("building point clouds for {0} wedges", no);
            int np = 0, nc = 0, nf = 0;
            double lastSpew = UTCTime.Now();
            CoreLimitedParallel.ForEach(wedges, obs =>
            {
                Interlocked.Increment(ref np);

                //bookeep name of the points observation so that we can recover its observation transform later
                string ptsName = obs.Points == null ? obs.Range.Name : obs.Points.Name;

                double now = UTCTime.Now();
                if (!options.NoProgress && ((now - lastSpew) > 10))
                {
                    pipeline.LogInfo("building {0} wedge point clouds in parallel, completed {1}/{2}, {3} failed",
                                     np, nc, no, nf);
                    lastSpew = now;
                }

                var mo = wedgeMeshOpts.Clone();
                int decimateBlocksize = options.DecimateWedgeMeshes;
                int autoDecimateTargetRes = options.TargetWedgeMeshResolution;

                if (alternateWedgeMeshDecimationPoints != null)
                {
                    try
                    {
                        var hull = obs.BuildFrustumHull(pipeline, frameCache, mo, uncertaintyInflated: false);
                        if (hull == null)
                        {
                            throw new Exception("failed to build hull");
                        }
                        foreach (var pt in alternateWedgeMeshDecimationPoints)
                        {
                            if (hull.Contains(pt))
                            {
                                decimateBlocksize = options.AlternateWedgeMeshDecimation;
                                autoDecimateTargetRes = options.AlternateTargetWedgeMeshResolution;
                                pipeline.LogInfo("using alternate wedge mesh decimation blocksize {0}{1} for {2}: " +
                                                 "frustum hull contains point {3}", decimateBlocksize,
                                                 decimateBlocksize < 0 ?
                                                 $" (auto, target resolution {autoDecimateTargetRes})" : "",
                                                 ptsName, pt);
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        pipeline.LogWarn("error creating observation frustum hull: " + ex.Message);
                    }
                }

                mo.Decimate = WedgeObservations.AutoDecimate(obs.Points, decimateBlocksize, autoDecimateTargetRes);
                if (mo.Decimate > 1 && mo.Decimate != decimateBlocksize && !options.NoProgress)
                {
                    pipeline.LogVerbose("auto decimating point cloud for observation {0} with blocksize {1}",
                                        ptsName, mo.Decimate);
                }
                    
                var pc = obs.BuildPointCloud(pipeline, frameCache, masker, mo);

                //even though we required UVW products when we collected wedge observations
                //there might still be no valid normals after masking & filtering
                //and that shows up here as a pointcloud with no normals

                if (pc != null && pc.HasNormals)
                {
                    int nv = pc.Vertices.Count;

                    int nr = pc.RemoveInvalidPoints();
                    if (nr > 0)
                    {
                        pipeline.LogWarn("removed {0}/{1} invalid points for observation {2}",
                                         nv - pc.Vertices.Count, Fmt.KMG(nv), ptsName);
                    }

                    nr = pc.RemoveInvalidNormals();
                    if (nr > 0)
                    {
                        pipeline.LogWarn("removed {0}/{1} points with invalid normals for observation {2}",
                                         nv - pc.Vertices.Count, Fmt.KMG(nv), ptsName);
                    }

                    if (options.PreClipPointCloudExtent > 0)
                    {
                        var bounds = pc.Bounds();
                        bounds = BoundsFromXYExtent(Vector3.Zero, options.PreClipPointCloudExtent,
                                                    bounds.Min.Z, bounds.Max.Z);
                        pc.Clip(bounds);
                        string msg = string.Format("pre-clipped point clound for observation {0} to {1}x{1} box " +
                                                   "in frame {2}, removed {3}/{4} points", ptsName,
                                                   options.PreClipPointCloudExtent, options.PreClipPointCloudExtent,
                                                   meshFrame, nv - pc.Vertices.Count, Fmt.KMG(nv));
                        if (pc.Vertices.Count == 0)
                        {
                            pipeline.LogWarn(msg);
                        }
                        else if (!options.NoProgress)
                        {
                            pipeline.LogVerbose(msg);
                        }
                    }

                    if (OBS_CLOUD_MERGE_EPS > 0)
                    {
                        int ov = pc.Vertices.Count;
                        pc.MergeNearbyVertices(OBS_CLOUD_MERGE_EPS);
                        if (!options.NoProgress)
                        {
                            pipeline.LogVerbose("merged {0} -> {1} points in observation {2}, epsilon {3:f3}m",
                                                Fmt.KMG(ov), Fmt.KMG(pc.Vertices.Count), ptsName, OBS_CLOUD_MERGE_EPS);
                        }
                    }

                    if (wedgeMeshOpts.NormalScale == NormalScale.Confidence && options.LinearMinPoissonConfidence > 0)
                    {
                        double maxLinearConfidence = 1.0;
                        pc.RescaleNormals(l =>
                        {
                            double d = 1.0 / l;
                            if (d < PDSImage.nearLimit)
                            {
                                return maxLinearConfidence;
                            }
                            else if (d > PDSImage.farLimit)
                            {
                                return options.LinearMinPoissonConfidence;
                            }
                            else
                            {
                                double t = (d - PDSImage.nearLimit) / (PDSImage.farLimit - PDSImage.nearLimit);
                                return maxLinearConfidence * (1.0 - t) + options.LinearMinPoissonConfidence * t;
                            }
                        });
                    }

                    if (pc.Vertices.Count > 0)
                    {
                        if (!options.NoProgress)
                        {
                            pipeline.LogVerbose("adding {0}/{1} points from observation {2}",
                                                Fmt.KMG(pc.Vertices.Count), Fmt.KMG(nv), ptsName);
                        }
                        observationPointClouds.AddOrUpdate(ptsName, _ => pc, (_, __) => pc);
                    }
                    else
                    {
                        pipeline.LogWarn("no points for observation {0}", ptsName);
                        Interlocked.Increment(ref nf);
                    }
                }
                else if (pc != null)
                {
                    pipeline.LogWarn("no valid normals in pointcloud for observation {0} ({1} points)",
                                     ptsName, Fmt.KMG(pc.Vertices.Count));
                    Interlocked.Increment(ref nf);
                }
                else
                {
                    pipeline.LogWarn("failed to build pointcloud for observation {0}", ptsName);
                    Interlocked.Increment(ref nf);
                }

                Interlocked.Decrement(ref np);
                Interlocked.Increment(ref nc);
            });

            var pcb = new List<BoundingBox>();
            if (!options.NoAutoExpandSurfaceExtent)
            {
                double autoExtent = options.SurfaceExtent;
                foreach (var pc in observationPointClouds.Values)
                {
                    var bb = pc.Bounds();
                    double e = 2 * Math.Max(Math.Max(Math.Abs(bb.Min.X), Math.Abs(bb.Max.X)),
                                            Math.Max(Math.Abs(bb.Min.Y), Math.Abs(bb.Max.Y)));
                    autoExtent = Math.Max(autoExtent, Math.Ceiling(e));
                    pcb.Add(bb);
                }
                if (options.MaxAutoSurfaceExtent > 0)
                {
                    autoExtent = Math.Min(options.MaxAutoSurfaceExtent, autoExtent);
                }
                if (options.Extent > 0)
                {
                    autoExtent = Math.Min(options.Extent, autoExtent);
                }
                if (autoExtent > options.SurfaceExtent)
                {
                    pipeline.LogInfo("expanding surface extent from {0:f3} to {1:f3}m to fit input points",
                                     options.SurfaceExtent, autoExtent);
                    options.SurfaceExtent = autoExtent;
                }
            }
            else
            {
                pcb = observationPointClouds.Values.Select(pc => pc.Bounds()).ToList();
            }

            BoundingBox b = BoundingBoxExtensions.Union(pcb.ToArray());
            double p = options.EnvelopePadding;
            surfaceBounds = BoundsFromXYExtent(Vector3.Zero, options.SurfaceExtent, b.Min.Z - p, b.Max.Z + p);

            if (options.NoAutoExpandSurfaceExtent)
            {
                foreach (var pc in observationPointClouds.Values)
                {
                    pc.Clip(surfaceBounds);
                }
            }

            SaveDebugMesh(surfaceBounds.ToMesh(), "surface-bounds");

            int sz = observationPointClouds.Values.Sum(c => c.Vertices.Count);
            if (sz == 0)
            {
                throw new Exception("no observation points");
            }
            pipeline.LogInfo("loaded {0} points from {1} observations, {2} failed", Fmt.KMG(sz), nc - nf, nf);
        }

        private void MakeOrbitalFillPointCloud()
        {
            Vector3 meshOriginInOrbital = Vector3.Transform(Vector3.Zero, meshToOrbital);
            double extent = options.SurfaceExtent + 2 * Math.Max(0, options.OrbitalFillPadding);
            int radiusPixels = (int)Math.Ceiling(0.5 * extent / orbitalDEMMetersPerPixel);
            var subrect = orbitalDEM.GetSubrectPixels(radiusPixels, meshOriginInOrbital);

            double ons = 1;
            string nsm = "";
            switch (wedgeMeshOpts.NormalScale)
            {
                case NormalScale.Confidence:
                {
                    ons = options.OrbitalFillPoissonConfidence;
                    nsm = $", confidence {ons:f3}";
                    break;
                }
                case NormalScale.PointScale:
                {
                    ons = 2 * orbitalDEMMetersPerPixel / orbitalFillSamplesPerPixel;
                    nsm = $", point scale {ons:f3}";
                    break;
                }
                case NormalScale.None: break;
            }
            if (ons <= 0)
            {
                throw new ArgumentException($"orbital normal scale {ons} <= 0");
            }

            pipeline.LogInfo("making orbital point cloud{0}", nsm);
            orbitalFillPointCloud = MakeOrbitalMesh(orbitalFillSamplesPerPixel, subrect);
            orbitalFillPointCloud.Faces.Clear();

            if (options.OrbitalFillAdjust != OrbitalFillAdjust.None && !options.NoSurface)
            {
                int w = orbitalFillSamplesPerPixel * subrect.Width;
                int h = orbitalFillSamplesPerPixel * subrect.Height;
                double c = orbitalDEMMetersPerPixel / orbitalFillSamplesPerPixel;

                pipeline.LogInfo("gridding {0} observation points into {1}x{2} {3}m grid for orbital fill {4} adjust",
                                 Fmt.KMG(observationPointClouds.Values.Sum(pc => pc.Vertices.Count)), w, h, c,
                                 options.OrbitalFillAdjust);

                var ofb = orbitalFillPointCloud.Bounds();
                var mmm = new Image(3, w, h); //min, max, med
                mmm.CreateMask(true); //all masked
                for (int i = 0; i < h; i++)
                {
                    for (int j = 0; j < w; j++)
                    {
                        mmm[0, i, j] = float.PositiveInfinity;
                        mmm[1, i, j] = float.NegativeInfinity;
                        mmm[2, i, j] = 0;
                    }
                }
                foreach (var cloud in observationPointClouds.Values)
                {
                    foreach (var v in cloud.Vertices)
                    {
                        int i = Math.Min(h - 1, (int)((v.Position.Y - ofb.Min.Y) / c));
                        int j = Math.Min(w - 1, (int)((v.Position.X - ofb.Min.X) / c));
                        mmm.SetMaskValue(i, j, false);
                        mmm[0, i, j] = (float)Math.Min(mmm[0, i, j], v.Position.Z);
                        mmm[1, i, j] = (float)Math.Max(mmm[1, i, j], v.Position.Z);
                    }
                }
                if (options.OrbitalFillAdjust == OrbitalFillAdjust.Med)
                {
                    for (int i = 0; i < h; i++)
                    {
                        for (int j = 0; j < w; j++)
                        {
                            if (mmm.IsValid(i, j))
                            {
                                mmm[2, i, j] = (float)(0.5 * (mmm[0, i, j] + mmm[1, i, j]));
                            }
                        }
                    }
                }

                var ofz = new Image(1, w, h);
                var ofn = new Image(3, w, h);
                ofz.CreateMask(true);
                foreach (var v in orbitalFillPointCloud.Vertices)
                {
                    int i = Math.Min(h - 1, (int)((v.Position.Y - ofb.Min.Y) / c));
                    int j = Math.Min(w - 1, (int)((v.Position.X - ofb.Min.X) / c));
                    ofz.SetMaskValue(i, j, false);
                    ofz[0, i, j] = (float)(v.Position.Z);
                    ofn[0, i, j] = (float)(v.Normal.X);
                    ofn[1, i, j] = (float)(v.Normal.Y);
                    ofn[2, i, j] = (float)(v.Normal.Z);
                }

                pipeline.LogInfo("adjusting orbital fill height to match {0} surface data height within {1} samples, " +
                                 "falloff scale factor {2}", options.OrbitalFillAdjust,
                                 options.OrbitalFillAdjustWidth, options.OrbitalFillAdjustBlend);

                int band = 0;
                switch (options.OrbitalFillAdjust)
                {
                    case OrbitalFillAdjust.Min: band = 0; break;
                    case OrbitalFillAdjust.Max: band = 1; break;
                    case OrbitalFillAdjust.Med: band = 2; break;
                    default: throw new Exception("unsupported orbital fill adjust mode: " + options.OrbitalFillAdjust);
                }

                var adj = new Image(1, w, h);
                adj.CreateMask(true);
                for (int i = 0; i < h; i++)
                {
                    for (int j = 0; j < w; j++)
                    {
                        if (mmm.IsValid(i, j) && ofz.IsValid(i, j))
                        {
                            adj.SetMaskValue(i, j, false);
                            adj[0, i, j] = (float)(mmm[band, i, j] - ofz[0, i, j]);
                        }
                    }
                }

                if (options.OrbitalFillAdjustWidth != 0 && options.OrbitalFillAdjustBlend > 0)
                {
                    adj.Inpaint(options.OrbitalFillAdjustWidth, blend: (float)options.OrbitalFillAdjustBlend);
                }

                int nv = 0;
                for (int i = 0; i < h; i++)
                {
                    for (int j = 0; j < w; j++)
                    {
                        if (adj.IsValid(i, j))
                        {
                            ofz[0, i, j] += adj[0, i, j];
                            nv++;
                        }
                    }
                }

                orbitalFillPointCloud.Vertices.Clear();
                orbitalFillPointCloud.Vertices.Capacity = nv;
                for (int i = 0; i < h; i++)
                {
                    for (int j = 0; j < w; j++)
                    {
                        if (ofz.IsValid(i, j))
                        {
                            var v = new Vertex(ofb.Min.X + c * j, ofb.Min.Y + c * i, ofz[0, i, j],
                                               //the original normals are no longer correct
                                               //now that we've (non-uniformly) adjusted the point heights
                                               //we do need to pass normals to Poisson though
                                               //and it would be annoying to recompute them entirely now
                                               //so just fudge it and use the original normals
                                               ofn[0, i, j], ofn[1, i, j], ofn[2, i, j]);
                            orbitalFillPointCloud.Vertices.Add(v);
                        }
                    }
                }
            }

            pipeline.LogInfo("orbital fill point cloud has {0} points", Fmt.KMG(orbitalFillPointCloud.Vertices.Count));

            if (ons != 1)
            {
                foreach (Vertex v in orbitalFillPointCloud.Vertices)
                {
                    v.Normal *= ons;
                }
            }

            var ob = orbitalFillPointCloud.Bounds();
            surfaceBounds.Min.Z = Math.Min(surfaceBounds.Min.Z, ob.Min.Z - options.EnvelopePadding);
            surfaceBounds.Max.Z = Math.Max(surfaceBounds.Max.Z, ob.Max.Z + options.EnvelopePadding);
        }

        private void MergePointClouds()
        {
            //typically we do CleverCombine across sitedrives
            //but within sitedrives we just do MeshMerge with SITEDRIVE_MERGE_EPS
            //so groups is typically a map from sitdrive -> list of obs wedge pointclouds
            //however if clever combine is disabled or if intra-sitedrive clevercombine is enabled
            //then groups is obs name -> obs wedge pointcloud
            var groups = new Dictionary<string, List<Mesh>>();
            foreach (var entry in observationPointClouds)
            {
                var obs = observationCache.GetObservation(entry.Key);
                string key =
                    (options.NoCleverCombine || options.IntraSitedriveCleverCombine || !(obs is RoverObservation)) ?
                    entry.Key : (obs as RoverObservation).SiteDrive.ToString();
                if (!groups.ContainsKey(key))
                {
                    groups[key] = new List<Mesh>();
                }
                groups[key].Add(entry.Value);
            }
            observationPointClouds.Clear(); //reduce memory usage

            var cloudList = new List<Mesh>(); //one cloud per entry in groups
            var origins = new List<Vector3>(); //corresponding origin for clevercombine, if enabled
            foreach (var entry in groups)
            {
                if (!options.NoCleverCombine)
                {
                    if (SiteDrive.IsSiteDriveString(entry.Key))
                    {
                        var xform = frameCache.GetRelativeTransform(entry.Key, meshFrame,
                                                                    options.UsePriors, options.OnlyAligned);
                        origins.Add(Vector3.Transform(Vector3.Zero, xform.Mean));
                    }
                    else
                    {
                        var pointsObs = observationCache.GetObservation(entry.Key);
                        var pointsCam = pointsObs.CameraModel as CAHV;
                        var obsToMesh = frameCache.GetObservationTransform(pointsObs, meshFrame,
                                                                           options.UsePriors, options.OnlyAligned);
                        //obsToMesh cannot be null here because WedgeObservations.BuildPointCloud() returned non-null
                        if (pointsCam != null && options.IntraSitedriveCleverCombine)
                        {
                            //the reference point used to determine how good a point is for clever combine
                            //naive version is using distance from camera
                            origins.Add(Vector3.Transform(pointsCam.C, obsToMesh.Mean));
                        }
                        else
                        {
                            if (options.IntraSitedriveCleverCombine)
                            {
                                pipeline.LogWarn("no CAHV camera model for observation {0}, " +
                                                 "using observation frame origin for clever combine", entry.Key);
                            }
                            origins.Add(Vector3.Transform(Vector3.Zero, obsToMesh.Mean));
                        }
                    }
                }

                var obsClouds = entry.Value;
                if (obsClouds.Count == 1)
                {
                    if (SiteDrive.IsSiteDriveString(entry.Key) && SITEDRIVE_MERGE_EPS > 0)
                    {
                        int ov = obsClouds[0].Vertices.Count;
                        obsClouds[0].MergeNearbyVertices(SITEDRIVE_MERGE_EPS);
                        pipeline.LogInfo("merged 1 observation point cloud in sitedrive {0} without clever combine, " +
                                         "total {1} -> {2} points, epsilon {3:f3}m", entry.Key, Fmt.KMG(ov),
                                         Fmt.KMG(obsClouds[0].Vertices.Count), SITEDRIVE_MERGE_EPS);
                    }
                    cloudList.Add(obsClouds[0]);
                }
                else
                {
                    //more than one obs cloud in group means we're doing CleverCombine across but not within sitedrives
                    //so merge this sitedrive directly
                    int ov = obsClouds.Sum(c => c.Vertices.Count);
                    var oc = obsClouds.ToArray();
                    var pc = MeshMerge.Merge(oc, clean: false, mergeNearbyVertices: SITEDRIVE_MERGE_EPS,
                                             afterEach: (i) => { oc[i] = null; } ); //reduce memory usage
                    cloudList.Add(pc);
                    pipeline.LogInfo("merged {0} observation point clouds in sitedrive {1} without clever combine, " +
                                     "total {2} -> {3} points, epsilon {4:f3}m", oc.Length, entry.Key,
                                     Fmt.KMG(ov), Fmt.KMG(pc.Vertices.Count), SITEDRIVE_MERGE_EPS);
                }
                obsClouds.Clear(); //reduce memory usage
            }

            int numNonOrbitalClouds = cloudList.Count;
            if (orbitalFillPointCloud != null)
            {
                pipeline.LogInfo("adding orbital point cloud for hole filling");
                cloudList.Add(orbitalFillPointCloud);
                //no corresponding entry in origins disables CleverCombine origin filter for orbital fill cloud
            }

            var clouds = cloudList.ToArray();
            cloudList.Clear(); //reduce memory usage

            long nv = clouds.Sum(pc => pc.Vertices.Count);

            int numDownward = 0;
            foreach (Mesh cloud in clouds)
            {
                foreach (Vertex v in cloud.Vertices)
                {
                    Vector3 n = v.Normal;
                    if (n.Z > 0)
                    {
                        if (options.FlipDownwardFacingNormals)
                        {
                            n.Z *= -1;
                            v.Normal = n;
                        }
                        numDownward++;
                    }
                }
            }
            pipeline.LogInfo("{0} downward facing normals{1}", Fmt.KMG(numDownward),
                             options.FlipDownwardFacingNormals ? " (flipped)" : "");
        
            pointCloud = new Mesh(hasNormals: true);

            if (options.WriteDebug && clouds.Length > 1)
            {
                sourceColors = Colorspace.RandomHues(numNonOrbitalClouds + 1); //extra color for orbital
                for (int i = 0; i < clouds.Length; i++)
                {
                    clouds[i].SetColor(sourceColors[i]);
                }
                pointCloud.HasColors = true;
            }

            if (options.NoCleverCombine)
            {
                pointCloud.MergeWith(clouds, clean: false, mergeNearbyVertices: CROSS_SITEDRIVE_MERGE_EPS,
                                     afterEach: (i) => { clouds[i] = null; }); //reduce memory usage
                pipeline.LogInfo("merged {0} observation clouds without clever combine, total {1} -> {2} points, " +
                                 "epsilon {3:f3}m", clouds.Length, Fmt.KMG(nv), Fmt.KMG(pointCloud.Vertices.Count),
                                 CROSS_SITEDRIVE_MERGE_EPS);
                SaveDebugMesh(pointCloud, "cloud");
            }
            else
            {
                if (options.WriteDebug || clouds.Length == 1)
                {
                    pointCloud.MergeWith(clouds, clean: false);
                    SaveDebugMesh(pointCloud, "cloud");
                }

                if (clouds.Length > 1)
                {
                    pointCloud = null; //reduce memory usage
                    pipeline.LogInfo("clever combining {0} point clouds, cell size {1}, aspect {2}, " +
                                     "max points per cell {3}, total {4} pts",
                                     clouds.Length, options.CleverCombineCellSize, options.CleverCombineCellAspect,
                                     options.CleverCombineMaxPointsPerCell, Fmt.KMG(nv));
                    
                    var cc = new CleverCombine(options.CleverCombineCellSize, options.CleverCombineCellAspect,
                                               options.CleverCombineMaxPointsPerCell);
                    pointCloud = cc.Combine(clouds, origins.ToArray(), pipeline);
                    
                    pipeline.LogInfo("clever combine returned {0} points", Fmt.KMG(pointCloud.Vertices.Count));
                    
                    SaveDebugMesh(pointCloud , "cloud-combined");
                }
                else
                {
                    pipeline.LogInfo("skipping clever combine: less than two point clouds");
                }
            }

            if (options.WriteDebug && wedgeMeshOpts.NormalScale != NormalScale.None)
            {
                bool isConf = wedgeMeshOpts.NormalScale == NormalScale.Confidence;
                var colored = new Mesh(pointCloud);
                var low =  isConf ? new Vector3(1, 0, 0) : new Vector3(0, 1, 0);
                var high = isConf ? new Vector3(0, 1, 0) : new Vector3(1, 0, 0);
                colored.ColorByNormalMagnitude(low, high);
                colored.NormalizeNormals();
                SaveDebugMesh(colored, wedgeMeshOpts.NormalScale.ToString());
            }
        }

        private void ReconstructMesh()
        {
            pipeline.LogInfo("reconstructing mesh from {0} points with {1}",
                             Fmt.KMG(pointCloud.Vertices.Count), options.ReconstructionMethod);

            void saveUncleanedMesh(Mesh m)
            {
                untrimmedMesh = m;
                SaveDebugMesh(m, "untrimmed");
            }

            void saveUntrimmedMesh(Mesh m)
            {
                untrimmedMesh = m;
                SaveDebugMesh(m, "untrimmed", writeNormalLengthsAsValue: true);

                var colored = new Mesh(untrimmedMesh);
                var red = new Vector3(1, 0, 0);
                var green = new Vector3(0, 1, 0);
                colored.ColorByNormalMagnitude(red, green);
                colored.NormalizeNormals();
                SaveDebugMesh(colored, "density");
            }
            
            void saveRawReconstructedMesh(string file)
            {
                SaveDebugMesh(file, "poisson-raw");
            }
            
            switch (options.ReconstructionMethod)
            {
                case MeshReconstructionMethod.FSSR:
                {
                    mesh = FSSR.Reconstruct(pointCloud, options.FSSRScale, options.FSSRScale == 0, saveUncleanedMesh,
                                            logger: pipeline);
                    break;
                }
                case MeshReconstructionMethod.Poisson:
                {
                    BoundingBox env = surfaceBounds;
                    double pad = options.EnvelopePadding;
                    double soo =
                        !options.NoOrbital && !options.NoPeripheralOrbital && options.Extent > options.SurfaceExtent ?
                        SURFACE_OVERLAP_ORBITAL : 0.0;
                    pad = Math.Max(pad, soo);
                    if (orbitalFillPointCloud != null)
                    {
                        pipeline.LogInfo("disabling Poisson trimmer, using orbital to fill holes");
                        poissonOpts.TrimmerLevel = 0;
                        pad = Math.Max(pad, Math.Max(options.OrbitalFillPadding, 0) + soo);
                        //already applied padding to surfceBounds Z 
                    }
                    else
                    {
                        env = pointCloud.Bounds();
                        env.Min.Z -= options.EnvelopePadding;
                        env.Max.Z += options.EnvelopePadding;
                    }
                    env.Min.X -= pad;
                    env.Max.X += pad;
                    env.Min.Y -= pad;
                    env.Max.Y += pad;
                    poissonOpts.Envelope = env;
                    SaveDebugMesh(env.ToMesh(), "poisson-envelope");
                    mesh = PoissonReconstruction.Reconstruct(pointCloud, poissonOpts,
                                                             rawReconstructedMeshFile: saveRawReconstructedMesh,
                                                             untrimmedMeshWithValueScaledNormals: saveUntrimmedMesh,
                                                             logger: pipeline);
                    break;
                }
                default: throw new Exception("unsupported reconstruction method: " + options.ReconstructionMethod);
            }

            if (mesh == null || mesh.Faces.Count == 0)
            {
                throw new Exception("failed to build mesh");
            }

            SaveDebugMesh(mesh, "reconstructed");
        }

        private void FilterReconstructedMesh()
        {
            pipeline.LogInfo("filtering triangles further than {0}m from any input point", options.FilterTriangles);
            //var kdTree = new VertexKDTree(pointCloud);
            var rTree = new RTree<int>();
            for (int i = 0; i < pointCloud.Vertices.Count; i++)
            {
                rTree.Add(pointCloud.Vertices[i].Position.ToRectangle(), i);
            }
            mesh.FilterFaces(f => {
                Vector3 barycenter = mesh.FaceToTriangle(f).Barycenter();
                //Vector3 nn = kdTree.NearestNeighbor(barycenter).Position;
                //return nn >= 0 && Vector3.Distance(barycenter, nn) <= options.FilterTriangles;
                return rTree.Nearest(barycenter.ToRTreePoint(), (float)options.FilterTriangles).Count > 0;
            });
            if (mesh.Vertices.Count == 0 || mesh.Faces.Count == 0)
            {
                throw new MeshException("empty output after filtering");
            }
            pipeline.LogInfo("filtered mesh has {0} faces", Fmt.KMG(mesh.Faces.Count));
            SaveDebugMesh(mesh, "filtered");
        }

        private void ClipAndCleanSurfaceMesh()
        {
            double ext = options.SurfaceExtent;
            if (!options.NoOrbital && !options.NoPeripheralOrbital && options.Extent > options.SurfaceExtent)
            {
                ext += 2 * Math.Max(0, options.OrbitalFillPadding);
                ext += 2 * SURFACE_OVERLAP_ORBITAL; //paper over any gaps
            }
            ext = Math.Min(ext, options.Extent);

            pipeline.LogInfo("clipping surface mesh to {0:f3}x{0:f3}m", ext, ext);
            var mb = mesh.Bounds();
            mesh.Clip(BoundsFromXYExtent(Vector3.Zero, ext, mb.Min.Z, mb.Max.Z));
            if (mesh.Faces.Count == 0)
            {
                throw new Exception("clipped mesh is empty");
            }
            SaveDebugMesh(mesh, "clipped-surface");

            pipeline.LogInfo("keeping only largest island by vertex count");
            int nr = mesh.RemoveIslands(minIslandRatio: 1.0, useVertexCount: true);
            pipeline.LogInfo("removed {0} islands", nr);

            if (options.TargetSurfaceMeshFaces > 0)
            {
                mesh = DecimateMesh(mesh, "surface", options.TargetSurfaceMeshFaces);
            }

            //both FSSR and Poisson require normals on their input mesh and write normals to their output mesh
            //however we have seen issues with these normals
            //and also they may be confidence scaled still
            //one option would be to just clear the normals and not include normals with the output scene mesh
            //but some kinds of later processing (like building parent tile meshes) will want them
            //so let's just regenerate them from the faces and write them to the scene mesh
            //because we're dealing with natural terrain it is pretty reasonable to compute vertex normals from faces
            //i.e. no sharp crease angles expected

            pipeline.LogInfo("cleaning surface mesh");
            mesh.Clean(); //removes degenerate faces

            mesh.GenerateVertexNormals();

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("cleaned mesh is empty");
            }

            pipeline.LogInfo("cleaned mesh has {0} faces", Fmt.KMG(mesh.Faces.Count));

            SaveDebugMesh(mesh, "cleaned-surface");
        }

        private void RetrimSurface()
        {
            //the mask we create here is non-convex but does not have holes
            //(1) find the largest boundary edge loop in the shrinkwrap mesh (in principle we might be able to skip the
            //    shrinkwrap and just perform this step on the whole surface mesh, but the shrinkwrap helps address
            //    cases where the outermost XY extent of the mesh may differ from its 3D bounding loop due to concave
            //    folds
            //(2) project that to the XY plane and offset it there by options.SurfaceMaskOffset (offset is naive and
            //    doesn't check for self intersection, but that shouldn't matter for the purpose here)
            //(3) triangulate the resulting XY plane polygon
            //(4) create a UV MeshOp where U=X and V=Y so that later queries can know if an XY point is inside the
            //    boundary (they are iff the MeshOp returns valid barycentric coordinates for the query XY point).
            try
            {
                //currently "shrinkwrap" just really means
                //(a) bin verts to grid cells in XY plane
                //(b) organized mesh with a 2.5D assumption
                //also, the shrinkwrap mesh is curently only used for creating the surface mask
                //which is used for hole filling and orbital geometry below
                //(BlendImages might also make and use a different shrinkwrap mesh
                //but that is now non-default legacy behavior)
                var bounds = mesh.Bounds();
                Mesh grid = Shrinkwrap.BuildGrid(bounds,
                                                 (int)(bounds.Extent().X * options.SurfaceMaskPointsPerMeter),
                                                 (int)(bounds.Extent().Y * options.SurfaceMaskPointsPerMeter),
                                                 VertexProjection.ProjectionAxis.Z);
                Mesh shrinkwrapMesh = Shrinkwrap.Wrap(grid, mesh, Shrinkwrap.ShrinkwrapMode.Project,
                                                      VertexProjection.ProjectionAxis.Z,
                                                      Shrinkwrap.ProjectionMissResponse.Clip);
                shrinkwrapMesh.Clean();
                SaveDebugMesh(shrinkwrapMesh, "shrinkwrap");

                //EdgeGraph edgeGraph = new EdgeGraph(mesh); //causes too many failures
                EdgeGraph edgeGraph = new EdgeGraph(shrinkwrapMesh);

                double projectedLengthSquared(Edge e)
                {
                    var s = e.Src.Position;
                    var d = e.Dst.Position;
                    double dx = d.X - s.X;
                    double dy = d.Y - s.Y;
                    return dx * dx + dy * dy;
                }

                List<Edge> perimeterEdges = edgeGraph
                    .GetLargestPolygonalBoundary()
                    .Where(edge => projectedLengthSquared(edge) > 0)
                    .ToList();

                for (int i = 0; i < perimeterEdges.Count; i++)
                {
                    perimeterEdges[i].Dst = perimeterEdges[(i + 1) % perimeterEdges.Count].Src;
                }

                Vector3 nadir = new Vector3(0, 0, 1);
                if (mission != null)
                {
                    mission.GetLocalLevelBasis(out Vector3 north, out Vector3 east, out nadir);
                }

                if (nadir.Z > 0)
                {
                    EdgeGraph.EnsureCCW(perimeterEdges);
                }

                Vertex projectAndOffset(Vertex src, Vertex dst)
                {
                    Vector3 pt = src.Position;
                    Vector3 edgeDir = dst.Position - src.Position;
                    pt.Z = 0;
                    edgeDir.Z = 0;
                    if (options.SurfaceMaskOffset != 0 && edgeDir.LengthSquared() > 0)
                    {
                        var perp = Vector3.Normalize(edgeDir.Cross(nadir));
                        pt += perp * options.SurfaceMaskOffset;
                    }
                    return new Vertex(pt);
                }
                
                var maskMesh = new Mesh();
                maskMesh.Vertices = perimeterEdges
                    .Select(e => projectAndOffset(e.Src, e.Dst))
                    .ToList();
                
                int id = 0;
                foreach (Edge e in perimeterEdges)
                {
                    e.Src.ID = id;
                    id++;
                }
                
                foreach (Edge e in TriangulatePolygon.Triangulate(perimeterEdges))
                {
                    if (e.Left != null)
                    {
                        maskMesh.Faces.Add(new Face(e.Src.ID, e.Dst.ID, e.Left.ID));
                    }
                }

                if (nadir.Z > 0)
                {
                    maskMesh.ReverseWinding();
                }
                
                SaveDebugMesh(maskMesh, "surface-mask");

                maskMesh.XYToUV();
                MeshOperator mo = new MeshOperator(maskMesh,
                                                   buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);

                pipeline.LogInfo("re-trimming with lenient trimmer level {0} instead of {1}",
                                 options.PoissonTrimmerLevelLenient, options.PoissonTrimmerLevel);

                poissonOpts.TrimmerLevel = options.PoissonTrimmerLevelLenient;
                mesh = PoissonReconstruction.Trim(untrimmedMesh, poissonOpts);

                SaveDebugMesh(mesh, "retrimmed-raw");
                    
                pipeline.LogInfo("clipping reconstructed mesh to surface mask");
                bool checkVert(int index)
                {
                    var xy = new Vector2(mesh.Vertices[index].Position.X, mesh.Vertices[index].Position.Y);
                    return mo.UVToBarycentric(xy) != null;
                }
                mesh.Faces = mesh.Faces
                    .Where(face => checkVert(face.P0) || checkVert(face.P1) || checkVert(face.P2))
                    .ToList();
                mesh.RemoveUnreferencedVertices();
                
                SaveDebugMesh(mesh, "retrimmed");
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex, "error retrimming surface", stackTrace: true);
            }
        }

        private Mesh MakeOrbitalMesh(double subsample, Image.Subrect bounds)
        {
            var ret = orbitalDEM.OrganizedMesh(bounds, null, subsample, null, withNormals: true, quadsOnly: true);
            if (sourceColors != null && sourceColors.Length > 0)
            {
                ret.SetColor(sourceColors[sourceColors.Length - 1]);
            }
            return ret.Transformed(orbitalToMesh);
        }

        private void MakeOrbitalMesh()
        {
            Vector3 meshOriginInOrbital = Vector3.Transform(Vector3.Zero, meshToOrbital);
            int orbitalRadiusPixels = (int)Math.Ceiling(0.5 * options.Extent / orbitalDEMMetersPerPixel);
            var orbitalBounds = orbitalDEM.GetSubrectPixels(orbitalRadiusPixels, meshOriginInOrbital);
            double orbitalRadiusMeters = orbitalRadiusPixels * orbitalDEMMetersPerPixel;
            pipeline.LogInfo("making {0:f3}x{0:f3}m peripheral orbital mesh, {1:f3} samples/m",
                             2 * orbitalRadiusMeters, orbitalSamplesPerPixel / orbitalDEMMetersPerPixel);
            orbitalMesh = MakeOrbitalMesh(orbitalSamplesPerPixel, orbitalBounds);
            if (!options.NoSurface && options.SurfaceExtent < options.Extent)
            {
                BoundingBox ob = orbitalMesh.Bounds();
                BoundingBox cut = surfaceBounds;
                cut.Min.Z = Math.Min(cut.Min.Z, ob.Min.Z);
                cut.Max.Z = Math.Max(cut.Max.Z, ob.Max.Z);
                if (options.OrbitalFillPadding > 0)
                {
                    cut.Min.X -= options.OrbitalFillPadding;
                    cut.Min.Y -= options.OrbitalFillPadding;
                    cut.Max.X += options.OrbitalFillPadding;
                    cut.Max.Y += options.OrbitalFillPadding;
                }
                if (Math.Abs(cut.Min.X) >= orbitalRadiusMeters || Math.Abs(cut.Min.Y) >= orbitalRadiusMeters ||
                    Math.Abs(cut.Max.X) >= orbitalRadiusMeters || Math.Abs(cut.Max.Y) >= orbitalRadiusMeters)
                {
                    pipeline.LogInfo("disabling peripheral orbital mesh, " +
                                     "central cutout {0:f3}x{1:f3}m would be larger than extent {2:f3}m",
                                     cut.Max.X - cut.Min.X, cut.Max.Y - cut.Min.Y, 2 * orbitalRadiusMeters);
                    orbitalMesh = null;
                }
                pipeline.LogInfo("cutting out central {0:f3}x{1:f3}m portion of peripheral orbital mesh",
                                 cut.Max.X - cut.Min.X, cut.Max.Y - cut.Min.Y);
                orbitalMesh.Cut(cut);
            }
            if (orbitalMesh != null)
            {
                SaveDebugMesh(orbitalMesh, "orbital");
            }
        }

        private void DecimateMesh()
        {
            mesh = DecimateMesh(mesh, "scene", options.TargetSceneMeshFaces);
        }

        private Mesh DecimateMesh(Mesh mesh, string what, int targetFaces)
        {
            int nf = mesh.Faces.Count;
            if (nf <= targetFaces)
            {
                pipeline.LogInfo("not decimating {0} mesh with {1}, mesh already has {2} <= {3} faces",
                                 what, options.MeshDecimator, Fmt.KMG(nf), Fmt.KMG(targetFaces));
                return mesh;
            }

            pipeline.LogInfo("decimating {0} mesh with {1}, target {2} faces",
                             what, options.MeshDecimator, Fmt.KMG(targetFaces));

            mesh = mesh.Decimated(targetFaces, options.MeshDecimator); //preserves normals

            pipeline.LogInfo("decimated {0} mesh to {1} faces", what, Fmt.KMG(mesh.Faces.Count));

            if (mesh.Faces.Count == 0)
            {
                throw new Exception($"decimated {what} mesh is empty");
            }

            SaveDebugMesh(mesh, $"{what}-decimated");

            return mesh;
        }

        private void ClipMeshToObservations()
        {
            pipeline.LogInfo("only keeping triangles visible in observations: {0}",
                             string.Join(", ", onlyForObs.Select(obs => obs.Name)));

            var hulls = Backproject
                .BuildFrustumHulls(pipeline, frameCache, meshFrame, options.UsePriors, options.OnlyAligned, onlyForObs,
                                   project, options.Redo, options.NoSave)
                .Values;

            Mesh filtered = new Mesh();
            filtered.SetProperties(mesh);
            filtered.Vertices = mesh.Vertices;
            foreach (var face in mesh.Faces)
            {
                foreach (var hull in hulls)
                {
                    if (hull.Intersects(mesh.FaceToTriangle(face)))
                    {
                        filtered.Faces.Add(face);
                        break;
                    }
                }
            }
            mesh = filtered;

            mesh.Clean();

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("mesh is empty");
            }

            pipeline.LogInfo("kept {0} faces visible in specified observations", Fmt.KMG(mesh.Faces.Count));

            SaveDebugMesh(mesh, "filtered");
        }

        private void AtlasMesh()
        {
            if (orbitalMesh == null || options.Extent <= options.SurfaceExtent || options.NoSurface ||
                options.NoPeripheralOrbital)
            {
                var mode = options.NoSurface ? AtlasMode.Heightmap : options.AtlasMode;
                pipeline.LogInfo("no peripheral orbital, {0} atlassing mesh", mode);
                AtlasMesh(mesh, sceneTextureResolution, "scene", mode);
                SaveDebugMesh(mesh, "atlassed");
                return;
            }

            //atlas surface mesh with whatever the configured atlas mode is
            //heightmap atlas outer orbital periphery
            //we clip and then re-merge those two parts here rather than atlassing them before they are merged
            //to handle workflows involving DecimateMesh() and/or ClipMeshToObservations()
            //then warp overall texture coordinates
            
            ComputeTextureWarp(options.Extent, options.SurfaceExtent,
                               out double srcSurfaceFrac, out double dstSurfaceFrac);

            int surfacePixels = (int)Math.Ceiling(dstSurfaceFrac * sceneTextureResolution);
            
            pipeline.LogInfo("{0} atlassing {1}x{1}m central submesh, resolution {2}x{2}",
                             options.AtlasMode, options.SurfaceExtent, surfacePixels);
            pipeline.LogInfo("heightmap atlassing {0}m orbital periphery",
                             0.5 * (options.Extent - options.SurfaceExtent));
            
            var meshBounds = mesh.Bounds();
            var centralBounds = BoundsFromXYExtent(Vector3.Zero, options.SurfaceExtent,
                                                   meshBounds.Min.Z, meshBounds.Max.Z);
            
            var centralMesh = mesh.Clipped(centralBounds);
            AtlasMesh(centralMesh, surfacePixels, "central");
            centralMesh.RescaleUVs(BoundingBoxExtensions.CreateXY(PointToUV(meshBounds, centralBounds.Min),
                                                                  PointToUV(meshBounds, centralBounds.Max)));
            SaveDebugMesh(centralMesh, "central-atlassed");

            var peripheralMesh = mesh.Cutted(centralBounds);
            HeightmapAtlasMesh(peripheralMesh);
            SaveDebugMesh(peripheralMesh, "peripheral-atlassed");
            
            mesh = MeshMerge.Merge(msg => pipeline.LogWarn(msg), centralMesh, peripheralMesh);

            void saveDebugMeshes(string suffix)
            {
                if (options.WriteDebug)
                {
                    SaveDebugMesh(mesh, suffix);
#if DBG_UV
                    var tmp = new Mesh(mesh);
                    tmp.ColorByUV();
                    SaveDebugMesh(tmp, suffix + "-UV");
                    tmp.ColorByUV(vChannel: -1);
                    SaveDebugMesh(tmp, suffix + "-U");
                    tmp.ColorByUV(uChannel: -1);
                    SaveDebugMesh(tmp, suffix + "-V");
#endif
                }
            }

            if (dstSurfaceFrac > srcSurfaceFrac && !options.NoTextureWarp)
            {
                saveDebugMeshes("prewarp-atlassed");
                
                pipeline.LogInfo("warping {0:F3}x{0:F3} central UVs to {1:F3}x{1:F3}, ease {2:F3}",
                                 srcSurfaceFrac, dstSurfaceFrac, options.EaseTextureWarp);
                
                pipeline.LogInfo("central meters per pixel: {0:F3}",
                                 options.SurfaceExtent / (dstSurfaceFrac * sceneTextureResolution));
                
                pipeline.LogInfo("orbital meters per pixel: {0:F3}",
                                 (options.Extent - options.SurfaceExtent) /
                                 ((1 - dstSurfaceFrac) * sceneTextureResolution));

                var src = BoundingBoxExtensions.CreateXY(PointToUV(meshBounds, centralBounds.Min),
                                                         PointToUV(meshBounds, centralBounds.Max));
                var dst = BoundingBoxExtensions.CreateXY(0.5 * Vector2.One, dstSurfaceFrac);

                mesh.WarpUVs(src, dst, options.EaseTextureWarp);
            }

            saveDebugMeshes("atlassed");
        }

        private void SaveSceneMesh()
        {
            pipeline.LogInfo("saving scene mesh in frame {0} to project storage", meshFrame);
            
            double surfaceExtent = -1; //unlimited
            if (options.NoSurface)
            {
                surfaceExtent = 0; //only orbital
            }
            else if (options.SurfaceExtent > 0)
            {
                surfaceExtent = options.SurfaceExtent;
            }

            if (surfaceExtent > originalSurfaceExtent && !options.UseExpandedSurfaceExtentForTiling)
            {
                pipeline.LogInfo("saving original surface extent {0:f3}m for tiling instead of expanded extent {1:f3}m",
                                 originalSurfaceExtent, surfaceExtent);
                surfaceExtent = originalSurfaceExtent;
            }
            
            Mesh tmp = mesh;
            if (tmp.HasColors)
            {
                tmp = new Mesh(mesh);
                tmp.HasColors = false;
            }

            var sceneMesh = SceneMesh.Find(pipeline, project.Name, MeshVariant.Default);
            if (sceneMesh != null)
            {
                sceneMesh.SetBounds(mesh.Bounds());
                var meshProd = new PlyGZDataProduct(tmp);
                pipeline.SaveDataProduct(project, meshProd, noCache: true);
                sceneMesh.MeshGuid = meshProd.Guid;
                sceneMesh.SurfaceExtent = surfaceExtent;
                sceneMesh.Save(pipeline);
            }
            else
            {
                SceneMesh.Create(pipeline, project, mesh: tmp, surfaceExtent: surfaceExtent);
            }
        
            if (!string.IsNullOrEmpty(options.OutputMesh))
            {
                TemporaryFile.GetAndDelete(StringHelper.GetUrlExtension(options.OutputMesh), tmpFile =>
                {
                    tmp.Save(tmpFile);
                    pipeline.SaveFile(tmpFile, options.OutputMesh, constrainToStorage: false);
                });
            }
        }

        private void SaveDebugMesh(Mesh mesh, string suffix, string texture = null,
                                   bool writeNormalLengthsAsValue = false)
        {
            if (options.WriteDebug)
            {
                SaveMesh(mesh, $"{meshFrame}-{dbgMeshCount++}-{suffix}", texture, writeNormalLengthsAsValue);
            }
        }

        private void SaveDebugMesh(string srcFile, string suffix)
        {
            if (options.WriteDebug)
            {
                string name = $"{meshFrame}-{dbgMeshCount++}-{suffix}";
                string dstFile = Path.Combine(localOutputPath, name + meshExt);
                PathHelper.EnsureExists(Path.GetDirectoryName(dstFile)); //name could have a subpath in it
                File.Copy(srcFile, dstFile, overwrite: true);
            }
        }
    }
}
