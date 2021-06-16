//#define DBG_UV
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using CommandLine;
using Microsoft.Xna.Framework;
using RTree;
using OPS.Util;
using OPS.MathExtensions;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using System.IO;

/// <summary>
/// Reconstructs scene geometry from observation point clouds in a Landform contextual mesh workflow.
///
/// Runs after all alignment stages (e.g. bev-align, heightmap-align), but before build-tiling-input.
///
/// The observation pointclouds are typically combined with CleverCombine which attempts to reject outlier points using
/// a grid-based approach.
///
/// The mesh is then reconstructed on the full scene point cloud, typically with Poisson reconstruction.  Mission normal
/// map RDRs (UVW products) give each point a normal vector, which is usually required.  Points with bad or suspected
/// bad normals are filtered before reconstruction.  Optionally the normals can be scaled by an estimate of the
/// confidence of each point, though at this time ingestion of mission RNE products is still TODO
/// https://github.jpl.nasa.gov/OnSight/Landform/issues/766 (and such products may not even be available) so we use
/// distance from the camera as a proxy.
///
/// The reconstructed mesh is cleaned and clipped to a surface data bounding box typically 64m square around the origin
/// of the primary sitedrive frame for the contextual mesh.  Its vertex normals are recomputed from its faces to avoid
/// issues with bad normals corrupting downstream operations such as reconstruction of parent tile meshes.
///
/// Hole filling is then typically performed.  A non-convex outer boundary is computed by creating a shrinkwrap mesh and
/// finding its largest boundary polygon.  That polygon is then triangulated and used as a "surface mask" for further
/// operations.  The surface mesh is reconstructed from the full scene point cloud a second time, but this time with
/// less aggressive trimming options.  The resulting mesh is clipped to the surface mask created from the original
/// construction.  In this way the potential undesirable effects of less aggressive surface trimming around the outer
/// boundary of the mesh are avoided, but the benefits of allowing more internal hole filling are gained.
///
/// If an orbital DEM is available a square portion of it centered on the origin of the primary sitedrive frame is
/// organized meshed.  The bounds of this mesh may be larger than the surface mesh bounds.  It is also possible for the
/// orbital mesh bounds to be the same as the surface mesh bounds, but they can't be smaller.
///
/// Typically the orbital mesh includes both coarse and fine portions.  A fine area is defined within a small blend
/// radius (typicaly 3m) of the surface bounds. A reasonable level of interpolation (typically 15 samples/meter) is used
/// in that area so that the individal triangles in the organized mesh are not too large.  Trianges with vertices inside
/// the surface mask are not included (the surface mask is computed if either hole filling or orbital meshing is to be
/// performed), so that the orbital fine mesh is approximately periperhal to the surface mesh.  Because the organized
/// mesh triangles in the orbital fine mesh are limited in size the matching at the boundary between the surface and
/// orbital meshes is typically not too far off at this point, but there will still be a gap.  The orbital fine mesh is
/// then sewn and blended to the surface mesh: vertices of the orbital mesh close to a vertex of the surface mesh
/// (typically 0.2m) are snapped to the nearest vertex of the surface mesh.  Other vertices of the orbital fine mesh are
/// adjusted in height with a blend based on the average of the nearest surface vertex heights and the distance to the
/// surface mesh.
///
/// If the total clip extent is larger than the orbital fine mesh extent, then a coarse orbital mesh is also added,
/// with a smaller amount of interpolation (typically 1 sample/meter).  The orbital fine mesh is typically computed with
/// a small outer gutter area (typically 4 samples) that is not subject to blending.  This helps ensure a seamless
/// boundary between the fine and coarse portions of the orbital mesh under certain conditions.  As long as the sampling
/// rate of the coarse portion matches the actual DEM resolution, and the sampling rate of the fine portion is an
/// integer (which it is currently constrained to be), then they should line up because the subsampling is linear.
///
/// The resulting mesh is always saved as a PlyGZDataProduct to project storage with metadata in a SceneMesh object in
/// linked from the alignment project in the project database.
///
/// The scene mesh will always have normals but will typically not have texture coordinates.  Because its topology can
/// be complex it can be non-trivial to atlas it.  In the typical contextual mesh workflow this is handled by only
/// atlasing the leaf and parent tile meshes, which are typically much smaller.
///
/// Atlasing of the full scene mesh can be attempted by specifying --generateuvs.
///
/// If a tileset is not required, the full scene mesh can also be directly saved by specifying an output mesh as the
/// second positional command line argument.  When running locally this can be either a relative or absolute disk path
/// with an accepted mesh file extension, or just the extension, in which case a default filename will be used in the
/// current working directory.  When running with --cloud the output mesh must either be a URL within the project venue
/// storage area, or a relative path which will be prepended with the project storage venue URL and
/// "meshing/GeometryProducts", or just a known mesh format extension.  The output scene mesh will not be textured.
/// However, if atlasing was successful, then a textured mesh can be generated with build-texture.
///
/// Example:
///
/// Landform.exe build-geometry windjana
///
/// </summary>
namespace OPS.Landform
{
    [Verb("build-geometry", HelpText = "create scene mesh from point clouds")]
    public class BuildGeometryOptions : GeometryCommandOptions
    {
        [Value(1, Required = false, HelpText = "URL, file, or file type (extension starting with \".\") to which to save scene mesh", Default = null)]
        public string OutputMesh { get; set; }

        [Option(HelpText = "Decimate the scene mesh to this target number of faces if positive", Default = 0)]
        public int TargetSceneMeshFaces { get; set; }

        [Option(Default = MeshReconstructionMethod.Poisson, HelpText = "Mesh reconstruction method (FSSR, Poisson)")]
        public MeshReconstructionMethod ReconstructionMethod { get; set; }

        [Option(HelpText = "Stereo eye to prefer (auto, left, right, any)", Default = "auto")]
        public string StereoEye { get; set; }

        [Option(HelpText = "Only include faces that intersect these observations, comma separated", Default = null)]
        public string OnlyFacesForObs { get; set; }

        [Option(HelpText = "Clip reconstructed surface to XY box of this size in meters around mesh frame origin if positive", Default = BuildGeometry.DEF_SURFACE_EXTENT)]
        public double SurfaceExtent { get; set; }

        [Option(HelpText = "Final clip box XY size in meters, 0 to clip to aggregate point cloud bounds", Default = BuildGeometry.DEF_EXTENT)]
        public double Extent { get; set; }

        [Option(HelpText = "Pre-clip observation point clouds to XY box of this size in meters around mesh frame origin if positive", Default = 0)]
        public double PreClipPointCloudExtent { get; set; }

        [Option(HelpText = "Fill holes in largest island created from surface trimmer, cull other islands", Default = false)]
        public bool NoFillHoles { get; set; }

        [Option(HelpText = "Remove islands whose bounding box diameter is less than this ratio of the max island bounding box diameter", Default = 0.2)]
        public double MinIslandRatio { get; set; }

        [Option(HelpText = "Mask offset for clipping surface/orbital", Default = 0.05)]
        public double MaskOffset { get; set; }

        [Option(HelpText = "Orbital sampling rate inside blend radius, non-positive to use DEM resolution", Default = 15)]
        public double OrbitalBlendPointsPerMeter { get; set; }

        [Option(HelpText = "Orbital sampling rate to fill holes, negative to use DEM resolution, 0 to disable", Default = 100)]
        public double OrbitalFillPointsPerMeter { get; set; }

        [Option(HelpText = "Orbital sampling confidence to fill holes", Default = 0.1)]
        public double OrbitalFillPoissonConfidence { get; set; }

        [Option(HelpText = "Mask resolution for clipping surface/orbital", Default = 2)]
        public double ShrinkwrapPointsPerMeter { get; set; }

        [Option(HelpText = "Blend orbital within this distance from surface in meters, 0 disables blend, negative for default", Default = BuildGeometry.DEF_BLEND_RADIUS)]
        public double OrbitalBlendRadius { get; set; }

        [Option(HelpText = "Sew orbital within this distance from surface in meters, 0 disables sew, negative for default", Default = BuildGeometry.DEF_SEW_RADIUS)]
        public double OrbitalSewRadius { get; set; }

        [Option(HelpText = "Orbital blend min blend, 0-1, larger preserves orbital more", Default = 0.1)]
        public double OrbitalBlendMin { get; set; }

        [Option(HelpText = "Discard observation point cloud normals with fewer than this many valid 8-neighbors", Default = 8)]
        public int NormalFilter { get; set; }

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

        [Option(HelpText = "Expand poinnt bounds to envelope bounds", Default = TilingDefaults.PARENT_CLIP_BOUNDS_EXPAND_HEIGHT)]
        public double ExpandEnvelopeBounds { get; set; }

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

        [Option(HelpText = "Scale observation point cloud normals by confidence and then apply this exponent in Poisson reconstruction, 0 disables, negative for auto", Default = -1)]
        public double PoissonConfidenceExponent { get; set; }

        [Option(HelpText = "Min required samples per octree cell in Poisson reconstruction, higher for noiser data", Default = 15)]
        public int PoissonMinSamplesPerCell { get; set; }

        [Option(HelpText = "Poisson reconstruction BSpline degree", Default = PoissonReconstruction.DEF_BSPLINE_DEGREE)]
        public int PoissonBSplineDegree { get; set; }

        [Option(HelpText = "Surface density based trimmer octree level (higher means more aggressive, 0 disables)", Default = PoissonReconstruction.DEF_TRIMMER_LEVEL)]
        public double PoissonTrimmerLevel { get; set; }

        [Option(HelpText = "Surface density based trimmer octree lenient level (higher means more aggressive, 0 disables)", Default = PoissonReconstruction.DEF_TRIMMER_LEVEL_LENIENT)]
        public double PoissonTrimmerLevelLenient { get; set; }

        [Option(HelpText = "FSSR global scale, negative to auto-compute, 0 to use individual point scales", Default = 0)]
        public double FSSRScale { get; set; }

        [Option(HelpText = "Filter out triangles whose barycenter is further than this from any input point", Default = 0)]
        public double FilterTriangles { get; set; }

        [Option(HelpText = "Flip downward facing normals", Default = false)]
        public bool FlipDownwardFacingNormals { get; set; }

        [Option(HelpText = "Generate full-mesh UVs", Default = false)]
        public bool GenerateUVs { get; set; }
    }

    public class BuildGeometry : GeometryCommand
    {
        private const string OUT_DIR = "meshing/GeometryProducts";

        public const double DEF_EXTENT = 1024;
        public const double DEF_SURFACE_EXTENT = 64;

        public const double DEF_BLEND_RADIUS = 3;
        public const double DEF_SEW_RADIUS = 0.2;

        public const int BLEND_GUTTER_SAMPLES = 4;

        private int dbgMeshCount;

        private BuildGeometryOptions options;

        private RoverObservation[] onlyForObs;

        private PoissonReconstruction.Options poissonOpts;

        private WedgeObservations.MeshOptions wedgeMeshOpts;

        private ConcurrentDictionary<string, Mesh> observationPointClouds = new ConcurrentDictionary<string, Mesh>();
        private Mesh pointCloud;
        private BoundingBox pointCloudBounds, envelopeBounds;
        private Mesh mesh;

        private Mesh untrimmedMesh;
        private MeshOperator maskUVMeshOp;

        private Mesh orbitalMesh;

        private double blendRadius, sewRadius;
        private double blendExtent;
        private int orbitalBlendSamplesPerPixel, orbitalFillSamplesPerPixel;
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
                    RunPhase("build observation point clouds", BuildObservationPointClouds);
                    RunPhase("merge point clouds", MergePointClouds);
                    RunPhase("reconstruct mesh", ReconstructMesh);
                    if (options.FilterTriangles > 0)
                    {
                        RunPhase("filter mesh", FilterMesh);
                    }
                }

                if (!options.NoSurface)
                {
                    RunPhase("clip surface mesh", ClipSurfaceMesh);
                    RunPhase("create surface mask mesh", CreateSurfaceMaskMesh);
                    if (maskUVMeshOp != null) //CreateSurfaceMaskMesh() failed
                    {
                        RunPhase("reconstruct surface to mask", ReconstructSurfaceToMask);
                    }
                }

                if (options.NoOrbital)
                {
                    //just clip surface mesh
                    double extent = options.Extent;
                    if (extent <= 0 || (options.SurfaceExtent > 0 && options.SurfaceExtent < extent))
                    {
                        extent = options.SurfaceExtent;
                    }
                    RunPhase("clip mesh", () => ClipMesh(extent));
                }
                else
                {
                    //surface mesh (if any) has already been clipped
                    //and we've already verified that 0 < SurfaceExtent < Extent
                    //now build orbital to Extent

                    RunPhase("build orbital mesh", BuildOrbitalMesh);

                    if (options.NoSurface)
                    {
                        mesh = orbitalMesh;
                    }
                    else
                    {
                        RunPhase("blend orbital to surface", BlendOrbitalToSurface);
                    }
                }

                if (options.TargetSceneMeshFaces > 0)
                {
                    RunPhase("decimate mesh", DecimateMesh);
                }

                if (onlyForObs.Length > 0)
                {
                    RunPhase("reduce mesh to specified observations", ReduceMesh);
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

            if (!options.NoOrbital)
            {
                options.NoOrbital |= !LoadOrbitalDEM();
                if (options.NoOrbital && options.NoSurface)
                {
                    throw new Exception("--nosurface but failed to load orbital");
                }
            }

            if (!options.NoOrbital && options.Extent <= 0)
            {
                throw new Exception("outer clip {options.Extent} must be positive to use orbital");
            }

            if (!options.NoOrbital && !options.NoSurface &&
                (options.SurfaceExtent <= 0 || options.Extent <= 0 || options.SurfaceExtent > options.Extent))
            {
                throw new Exception($"surface clip ({options.SurfaceExtent}) must be greater than 0 and less than " +
                                    $"or equal to outer clip ({options.Extent}) to use surface and orbital");
            }

            onlyForObs = observationCache.ParseList(options.OnlyFacesForObs)
                .Where(obs => obs is RoverObservation)
                .Cast<RoverObservation>()
                .ToArray();

            double poissonConfidenceExponent = options.PoissonConfidenceExponent;
            if (poissonConfidenceExponent < 0)
            {
                poissonConfidenceExponent = options.NoOrbital ? 0 : PoissonReconstruction.DEF_CONFIDENCE_EXPONENT;
            }

            poissonOpts = new PoissonReconstruction.Options
            {
                Boundary = PoissonReconstruction.DEF_BOUNDARY_TYPE,
                MinOctreeCellWidthMeters = options.PoissonCellSize,
                OctreeDepth = options.PoissonTreeDepth,
                MinOctreeSamplesPerCell = options.PoissonMinSamplesPerCell,
                BSplineDegree = options.PoissonBSplineDegree,
                ConfidenceExponent = poissonConfidenceExponent,
                TrimmerLevel = options.PoissonTrimmerLevel,
                PassEnvelopeToPoisson = options.PassEnvelopeToPoisson,
                ClipToEnvelope = !options.NoPoissonClipToEnvelope,
                MinIslandRatio = !options.NoPoissonRemoveIslands ? options.MinIslandRatio : 0
            };

            if (!string.IsNullOrEmpty(options.OutputMesh))
            {
                options.OutputMesh =
                    CheckOutputURL(options.OutputMesh, project.Name, OUT_DIR, MeshSerializers.Instance);
            }

            if (!options.NoOrbital && !options.NoSurface)
            {
                sewRadius = options.OrbitalSewRadius;
                if (sewRadius < 0)
                {
                    sewRadius = DEF_SEW_RADIUS;
                }
                
                blendRadius = options.OrbitalBlendRadius;
                if (blendRadius < 0)
                {
                    blendRadius = DEF_BLEND_RADIUS;
                }
                if (blendRadius < sewRadius)
                {
                    blendRadius = sewRadius;
                }

                //already verified 0 < options.SurfaceExtent <= options.Extent
                blendExtent = Math.Min(options.Extent, options.SurfaceExtent + 2 * Math.Max(blendRadius, 0));
            }

            orbitalBlendSamplesPerPixel = 1;
            if (options.OrbitalBlendPointsPerMeter > 0)
            {
                orbitalBlendSamplesPerPixel =
                    (int)Math.Ceiling(options.OrbitalBlendPointsPerMeter * orbitalDEMMetersPerPixel);
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
                NormalFilter = options.NormalFilter
            };

            if ((options.ReconstructionMethod == MeshReconstructionMethod.Poisson) &&
                (poissonConfidenceExponent != 0))
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

        private void BuildObservationPointClouds()
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

            if (wedges.Count == 0)
            {
                pipeline.LogError("no wedge observations");
            }

            int no = wedges.Count;
            pipeline.LogInfo("building point clouds for {0} wedges", no);
            int np = 0, nc = 0, nf = 0;
            CoreLimitedParallel.ForEach(wedges, obs =>
            {
                Interlocked.Increment(ref np);

                //bookeep name of the points observation so that we can recover its observation transform later
                string ptsName = obs.Points == null ? obs.Range.Name : obs.Points.Name;

                pipeline.LogVerbose("building {0} wedge point clouds in parallel, completed {1}/{2}, {3} failed",
                                    np, nc, no, nf);

                var mo = wedgeMeshOpts.Clone();
                mo.Decimate = WedgeObservations.AutoDecimate(obs.Points, options.DecimateWedgeMeshes,
                                                             options.TargetWedgeMeshResolution);
                if (mo.Decimate > 1 && mo.Decimate != options.DecimateWedgeMeshes && !options.NoProgress)
                {
                    pipeline.LogVerbose("auto decimating point cloud for observation {0} with blocksize {1}",
                                        ptsName, mo.Decimate);
                }
                    
                var pc = obs.BuildPointCloud(pipeline, frameCache, masker, mo);

                if (pc != null)
                {
                    int nv = pc.Vertices.Count;

                    if (pc.ContainsZeroLengthNormals())
                    {
                        pc.RemoveZeroLengthNormals();
                        pipeline.LogWarn("removed {0}/{1} points with zero normals in point cloud for observation {2}",
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
                else
                {
                    pipeline.LogWarn("failed to build pointcloud for observation {0}", ptsName);
                    Interlocked.Increment(ref nf);
                }

                Interlocked.Decrement(ref np);
                Interlocked.Increment(ref nc);
            });
        }

        private void MergePointClouds()
        {
            var groups = observationPointClouds.GroupBy(entry =>
            {
                var obs = observationCache.GetObservation(entry.Key);
                if (options.NoCleverCombine || options.IntraSitedriveCleverCombine || !(obs is RoverObservation))
                {
                    return entry.Key;
                }
                return (obs as RoverObservation).SiteDrive.ToString();
            }).ToArray();

            int nv = observationPointClouds.Values.Sum(pc => pc.Vertices.Count);

            var cloudList = groups.Select(group =>
            {
                var obsClouds = group.Select(pair => pair.Value).ToArray();
                if (obsClouds.Length == 1)
                {
                    return obsClouds[0];
                }
                pipeline.LogInfo("merging {0} observation point clouds in sitedrive {1} without clever combine, " +
                                 "total {2} points", obsClouds.Length, group.Key,
                                 Fmt.KMG(obsClouds.Sum(c => c.Vertices.Count)));
                var pc = new Mesh(hasNormals: true);
                pc.MergeWith(obsClouds, normalize: false, removeDuplicateVerts: false);
                return pc;
            }).ToList();

            int numNonOrbitalClouds = cloudList.Count;

            if (!options.NoFillHoles && !options.NoOrbital && orbitalFillSamplesPerPixel > 0 && orbitalDEM != null)
            {
                pipeline.LogInfo("adding orbital point cloud for hole filling, subsample {0}",
                                 orbitalFillSamplesPerPixel);
                var opc = MakeOrbitalPointCloud();
                double ons = 1;
                switch (wedgeMeshOpts.NormalScale)
                {
                    case NormalScale.Confidence: ons = options.OrbitalFillPoissonConfidence; break;
                    case NormalScale.PointScale: ons = 2 * orbitalDEMMetersPerPixel / orbitalFillSamplesPerPixel; break;
                    case NormalScale.None: break;
                }
                if (ons != 1)
                {
                    if (ons <= 0)
                    {
                        throw new ArgumentException($"orbital normal scale {ons} <= 0");
                    }
                    foreach (Vertex v in opc.Vertices)
                    {
                        v.Normal *= ons;
                    }
                }
                cloudList.Add(opc);
                pipeline.LogInfo("orbital point cloud has {0} points", Fmt.KMG(opc.Vertices.Count));
            }

            var clouds = cloudList.ToArray();

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
                pipeline.LogInfo("merging {0} observation point clouds without clever combine, total {1} points",
                                 clouds.Length, Fmt.KMG(nv));
                pointCloud.MergeWith(clouds, normalize: false, removeDuplicateVerts: false);
                SaveDebugMesh(pointCloud, "cloud");
            }
            else
            {
                var origins = groups.Select(group =>
                {
                    if (SiteDrive.IsSiteDriveString(group.Key))
                    {
                        var xform = frameCache.GetRelativeTransform(group.Key, meshFrame,
                                                                    options.UsePriors, options.OnlyAligned);
                        return Vector3.Transform(Vector3.Zero, xform.Mean);
                    }
                    else
                    {
                        var pointsObs = observationCache.GetObservation(group.Key);
                        var pointsCam = pointsObs.CameraModel as CAHV;
                        var obsToMesh = frameCache.GetObservationTransform(pointsObs, meshFrame,
                                                                           options.UsePriors, options.OnlyAligned);
                        //obsToMesh cannot be null here because WedgeObservations.BuildPointCloud() returned non-null
                        if (pointsCam != null && options.IntraSitedriveCleverCombine)
                        {
                            //the reference point used to determine how good a point is for clever combine
                            //naive version is using distance from camera
                            return Vector3.Transform(pointsCam.C, obsToMesh.Mean);
                        }
                        else
                        {
                            if (options.IntraSitedriveCleverCombine)
                            {
                                pipeline.LogWarn("no CAHV camera model for observation {0}, " +
                                                 "using observation frame origin for clever combine", group.Key);
                            }
                            return Vector3.Transform(Vector3.Zero, obsToMesh.Mean);
                        }
                    }
                }).ToArray();

                if (options.WriteDebug || clouds.Length == 1)
                {
                    pointCloud.MergeWith(clouds, normalize: false, removeDuplicateVerts: false);
                }

                SaveDebugMesh(pointCloud, "cloud");

                if (clouds.Length > 1)
                {
                    pipeline.LogInfo("clever combining {0} point clouds, cell size {1}, aspect {2}, total {3} pts",
                                     clouds.Length, options.CleverCombineCellSize, options.CleverCombineCellAspect,
                                     Fmt.KMG(nv));
                    
                    var cc = new CleverCombine(options.CleverCombineCellSize, options.CleverCombineCellAspect,
                                               options.CleverCombineMaxPointsPerCell);
                    pointCloud = cc.Combine(clouds, origins, pipeline);
                    
                    pipeline.LogInfo("clever combine returned {0} points", Fmt.KMG(pointCloud.Vertices.Count));
                    
                    SaveDebugMesh(pointCloud , "cloud-combined");
                }
                else
                {
                    pipeline.LogInfo("skipping clever combine: less than two point clouds");
                }
            }

            observationPointClouds.Clear(); //significant memory usage

            pointCloudBounds = pointCloud.Bounds();

            envelopeBounds = pointCloudBounds;

            envelopeBounds.Max += options.ExpandEnvelopeBounds * Vector3.UnitZ;
            envelopeBounds.Min -= options.ExpandEnvelopeBounds * Vector3.UnitZ;
            poissonOpts.Envelope = envelopeBounds;

            SaveDebugMesh(envelopeBounds.ToMesh(), "envelope");

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
            var pc = pointCloud;

            pipeline.LogInfo("reconstructing mesh from {0} points with {1}",
                             Fmt.KMG(pc.Vertices.Count), options.ReconstructionMethod);

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
                SaveDebugMesh(file, "Poisson-raw");
            }
            
            switch (options.ReconstructionMethod)
            {
                case MeshReconstructionMethod.FSSR:
                {
                    mesh = FSSR.Reconstruct(pc, options.FSSRScale, options.FSSRScale == 0, saveUncleanedMesh,
                                            logger: pipeline);
                    break;
                }
                case MeshReconstructionMethod.Poisson:
                {
                    mesh = PoissonReconstruction.Reconstruct(pc, poissonOpts,
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

            CleanMesh();

            SaveDebugMesh(mesh, "reconstructed");
        }

        private void FilterMesh()
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

        private void CleanMesh()
        {
            mesh.Clip(envelopeBounds);

            if (options.MinIslandRatio > 0)
            {
                int nr = mesh.RemoveIslands(options.MinIslandRatio);
                pipeline.LogInfo("removed {0} islands", nr);
            }

            //both FSSR and Poisson require normals on their input mesh and write normals to their output mesh
            //however we have seen issues with these normals
            //and also they may be confidence scaled still
            //one option would be to just clear the normals and not include normals with the output scene mesh
            //but some kinds of later processing (like building parent tile meshes) will want them
            //so let's just regenerate them from the faces and write them to the scene mesh
            //because we're dealing with natural terrain it is pretty reasonable to compute vertex normals from faces
            //i.e. no sharp crease angles expected
            mesh.Clean(); //removes degenerate faces

            mesh.GenerateVertexNormals();

            pipeline.LogInfo("cleaned mesh has {0} faces", Fmt.KMG(mesh.Faces.Count));
        }

        private void ClipSurfaceMesh()
        {
            ClipMesh(options.SurfaceExtent);
            SaveDebugMesh(mesh, "clipped-surface");
        }

        //populates maskUVMeshOp so that later stages can trim meshes to an XY plane boundary
        //the boundary is non-convex but does not have holes
        //we make the mask by
        //(a) finding the largest boundary edge loop in the shrinkwrap mesh (in principle we might be able to skip the
        //    shrinkwrap and just perform this step on the whole surface mesh, but the shrinkwrap helps address cases
        //    where the outermost XY extent of the mesh may differ from its 3D bounding loop due to concave folds
        //    https://github.jpl.nasa.gov/OnSight/Landform/issues/1145)
        //(b) project that to the XY plane and offset it there by options.MaskOffset (the offset is naive and doesn't
        ///   check for self intersection, but that shouldn't matter for the purpose here)
        //(c) triangulating the resulting XY plane polygon
        //(d) creating a UV MeshOp where U=X and V=Y so that later queries can know if an XY point is inside the
        ///   boundary (they are iff the MeshOp returns valid barycentric coordinates for the query XY point).
        private void CreateSurfaceMaskMesh()
        {
            try
            {
                //currently "shrinkwrap" just really means
                //(a) bin verts to grid cells in XY plane
                //(b) organized mesh with a 2.5D assumption
                //also, the shrinkwrap mesh is curently only used for creating the surface mask
                //which is used for hole filling and orbital geometry below
                //(BlendImages might also make and use a shrinkwrap mesh, but that now non-default legacy behavior)
                var bounds = mesh.Bounds();
                Mesh grid = Shrinkwrap.BuildGrid(bounds,
                                                 (int)(bounds.Extent().X * options.ShrinkwrapPointsPerMeter),
                                                 (int)(bounds.Extent().Y * options.ShrinkwrapPointsPerMeter),
                                                 VertexProjection.ProjectionAxis.Z);
                Mesh shrinkwrapMesh = Shrinkwrap.Wrap(grid, mesh, Shrinkwrap.ShrinkwrapMode.Project,
                                                      VertexProjection.ProjectionAxis.Z,
                                                      Shrinkwrap.ProjectionMissResponse.Clip);
                shrinkwrapMesh.Clean();
                SaveDebugMesh(shrinkwrapMesh, "shrinkwrap");

                //EdgeGraph edgeGraph = new EdgeGraph(mesh); //TODO causes too many failures
                //https://github.jpl.nasa.gov/OnSight/Landform/issues/1145
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
                    if (options.MaskOffset != 0 && edgeDir.LengthSquared() > 0)
                    {
                        var perp = Vector3.Normalize(edgeDir.Cross(nadir));
                        pt += perp * options.MaskOffset;
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
                maskUVMeshOp =
                    new MeshOperator(maskMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
            }
            catch (Exception ex)
            {
                pipeline.LogException(ex, "error creating surface mask, falling back to whole surface mesh",
                                      stackTrace: true);
            }
        }

        //Replace mesh with a new reconstruction that
        //(1) uses less picky trimming
        //(2) but that is clipped in the XY plane to the mask we already created
        //    from the outer boundary of the original reconstructed mesh.
        //This is done to fill holes and/or to prepare for orbital geometry to be added outside the mask.
        private void ReconstructSurfaceToMask()
        {
            mesh = untrimmedMesh;

            if (options.ReconstructionMethod == MeshReconstructionMethod.Poisson &&
                options.PoissonTrimmerLevelLenient < options.PoissonTrimmerLevel)
            {
                pipeline.LogInfo("retrimming Poisson mesh with lenient trimmer level {0} instead of {1}",
                                 options.PoissonTrimmerLevelLenient, options.PoissonTrimmerLevel);
                poissonOpts.TrimmerLevel = options.PoissonTrimmerLevelLenient;
                mesh = PoissonReconstruction.Trim(mesh, poissonOpts);
                SaveDebugMesh(mesh, "trimmed-lenient");
            }

            mesh.Faces = mesh.Faces.Where(face =>
            {
                //Get rid of faces if all of their endpoints fall fall outside mask mesh
                //TODO: clip on any overlap and stitch meshes
                //Currently the output of trimmer does not have a clean boundary which makes stitching difficult
                //change || to && for stronger clip
                return (maskUVMeshOp.UVToBarycentric(new Vector2(mesh.Vertices[face.P0].Position.X,
                                                                 mesh.Vertices[face.P0].Position.Y)) != null ||
                        maskUVMeshOp.UVToBarycentric(new Vector2(mesh.Vertices[face.P1].Position.X,
                                                                 mesh.Vertices[face.P1].Position.Y)) != null ||
                        maskUVMeshOp.UVToBarycentric(new Vector2(mesh.Vertices[face.P2].Position.X,
                                                                 mesh.Vertices[face.P2].Position.Y)) != null);
            }).ToList();

            CleanMesh();

            SaveDebugMesh(mesh, "masked");
        }

        private Mesh MakeOrbitalPointCloud()
        {
            int surfaceRadiusPixels = (int)Math.Ceiling(0.5 * options.SurfaceExtent / orbitalDEMMetersPerPixel);
            Vector3 meshOriginInOrbital = Vector3.Transform(Vector3.Zero, meshToOrbital);
            var surfaceBounds = orbitalDEM.GetSubrectPixels(surfaceRadiusPixels, meshOriginInOrbital);
            var ret = MakeOrbitalMesh(orbitalFillSamplesPerPixel, surfaceBounds);
            ret.Faces.Clear();
            return ret;
        }

        private Mesh MakeOrbitalMesh(double subsample, Image.Subrect outerBounds, Image.Subrect innerBounds = null,
                                     MeshOperator maskOp = null)
        {
            Func<Vector3, bool> filter = pt => 
            {
                pt = Vector3.Transform(pt, orbitalToMesh);
                return maskOp == null || maskOp.UVToBarycentric(new Vector2(pt.X, pt.Y)) == null; //not in surf mesh
            };
            var ret = orbitalDEM.OrganizedMesh(outerBounds, innerBounds, subsample, filter, withNormals: true,
                                               quadsOnly: true);
            if (sourceColors != null && sourceColors.Length > 0)
            {
                ret.SetColor(sourceColors[sourceColors.Length - 1]);
            }
            return ret.Transformed(orbitalToMesh);
        }

        private void BuildOrbitalMesh()
        {
            int orbitalRadiusPixels = (int)Math.Ceiling(0.5 * options.Extent / orbitalDEMMetersPerPixel);
            int blendRadiusPixels = (int)Math.Ceiling(0.5 * blendExtent / orbitalDEMMetersPerPixel);

            var maskOp = maskUVMeshOp;
            if (maskOp == null && mesh != null) //CreateSurfaceMaskMesh() failed
            {
                var tmp = new Mesh(mesh);
                tmp.XYToUV();
                maskOp = new MeshOperator(tmp, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
            }

            Vector3 meshOriginInOrbital = Vector3.Transform(Vector3.Zero, meshToOrbital);

            Image.Subrect blendBounds = null;
            if (orbitalBlendSamplesPerPixel != orbitalSamplesPerPixel && blendRadiusPixels > 0)
            {
                blendBounds = orbitalDEM.GetSubrectPixels(blendRadiusPixels, meshOriginInOrbital);
            }

            if (orbitalRadiusPixels > blendRadiusPixels || blendBounds == null)
            {
                var orbitalBounds = orbitalDEM.GetSubrectPixels(orbitalRadiusPixels, meshOriginInOrbital);

                pipeline.LogInfo("making {0:f3}x{0:f3}m orbital mesh at {1:f3} samples/meter",
                                 2 * orbitalRadiusPixels * orbitalDEMMetersPerPixel,
                                 orbitalSamplesPerPixel / orbitalDEMMetersPerPixel);
                
                orbitalMesh = MakeOrbitalMesh(orbitalSamplesPerPixel, orbitalBounds, blendBounds, maskOp);

                pipeline.LogInfo("made orbital mesh with {0} triangles", Fmt.KMG(orbitalMesh.Faces.Count));
            }

            if (blendBounds != null)
            {
                if (orbitalMesh != null)
                {
                    SaveDebugMesh(orbitalMesh, "outer-orbital");
                }

                pipeline.LogInfo("making {0:f3}x{0:f3} orbital blend mesh at {1:f3} samples/meter",
                                 2 * blendRadiusPixels * orbitalDEMMetersPerPixel,
                                 orbitalBlendSamplesPerPixel / orbitalDEMMetersPerPixel);

                var blendMesh = MakeOrbitalMesh(orbitalBlendSamplesPerPixel, blendBounds, null, maskOp);

                SaveDebugMesh(blendMesh, "preblend-orbital");

                pipeline.LogInfo("made orbital blend mesh with {0} triangles", Fmt.KMG(blendMesh.Faces.Count));

                if (orbitalMesh != null)
                {
                    orbitalMesh.MergeWith(blendMesh);
                    pipeline.LogInfo("combined orbital mesh size {0} triangles", Fmt.KMG(orbitalMesh.Faces.Count));
                }
                else
                {
                    orbitalMesh = blendMesh;
                }
            }
                
            SaveDebugMesh(orbitalMesh, "orbital");
        }

        private void BlendOrbitalToSurface()
        {
            if (orbitalMesh == null || (blendRadius == 0 && sewRadius == 0))
            {
                pipeline.LogInfo("skipping blend orbital to surface: no orbital mesh or no blend or sew radius");
                return;
            }

            double radius = blendRadius;

            if (BLEND_GUTTER_SAMPLES > 0)
            {
                double gutterMeters = BLEND_GUTTER_SAMPLES * (orbitalDEMMetersPerPixel / orbitalBlendSamplesPerPixel);
                if (radius > gutterMeters)
                {
                    radius -= gutterMeters;
                }
            }

            var meshOp = new MeshOperator(mesh, buildFaceTree: false, buildVertexTree: true, buildUVFaceTree: false);

            double blendMin = options.OrbitalBlendMin;
            double smoothRadius = 0.1 * radius;

            double boundsRadius = options.SurfaceExtent > 0 ? (0.5 * options.SurfaceExtent + radius) : 0;
            double blendRadiusSq = radius * radius;
            double sewRadiusSq = sewRadius * sewRadius;

            pipeline.LogInfo("collecting nearest surface vertices within {0:f3}m of orbital", radius);
            var vertPairs = new ConcurrentDictionary<int, int>(); //orbitalMesh vert index -> mesh vert index
            CoreLimitedParallel.For(0, orbitalMesh.Vertices.Count, i =>
            {
                var demVert = orbitalMesh.Vertices[i];
                Vector3 demPt = demVert.Position;
                if (boundsRadius <= 0 || (Math.Abs(demPt.X) <= boundsRadius && Math.Abs(demPt.Y) <= boundsRadius))
                {
                    double minDistSq = double.PositiveInfinity;
                    int closest = -1;
                    foreach (var j in meshOp.NearestVertexIndicesXY(demVert.Position, radius))
                    {
                        Vector3 meshPt = mesh.Vertices[j].Position;
                        double dx = meshPt.X - demPt.X;
                        double dy = meshPt.Y - demPt.Y;
                        double distSq = dx * dx + dy * dy;
                        if (distSq < minDistSq)
                        {
                            minDistSq = distSq;
                            closest = j;
                        }
                    }
                    if (closest >= 0)
                    {
                        vertPairs[i] = closest;
                    }
                }
            });
            
            pipeline.LogInfo("blending {0} orbital vertices", Fmt.KMG(vertPairs.Count));
                    
            var orbitalMeshOp =
                new MeshOperator(orbitalMesh, buildFaceTree: false, buildVertexTree: true, buildUVFaceTree: false);

            var blendedOrbitalMesh = new Mesh(orbitalMesh);
            CoreLimitedParallel.ForEach(vertPairs, pair =>
            {
                var demVert = orbitalMesh.Vertices[pair.Key];
                var blendedVert = blendedOrbitalMesh.Vertices[pair.Key];
                var meshVert = mesh.Vertices[pair.Value];
                var demPt = demVert.Position;
                var meshPt = meshVert.Position;
                double dx = meshPt.X - demPt.X;
                double dy = meshPt.Y - demPt.Y;
                double distSq = dx * dx + dy * dy;
                if (distSq < sewRadiusSq)
                {
                    blendedVert.Position = meshPt;
                }
                else
                {
                    double mz = 0, n = 0;
                    Vector2 mxy = Vector2.Zero;
                    foreach (var i in orbitalMeshOp.NearestVertexIndicesXY(demPt, smoothRadius))
                    {
                        if (vertPairs.ContainsKey(i))
                        {
                            var mv = mesh.Vertices[vertPairs[i]];
                            mz += mv.Position.Z;
                            mxy.X += mv.Position.X;
                            mxy.Y += mv.Position.Y;
                            n++;
                        }
                    }
                    mz = n > 0 ? mz / n : meshVert.Position.Z;
                    double dist = n > 0 ? Vector2.Distance(mxy / n, new Vector2(demPt.X, demPt.Y)) : Math.Sqrt(distSq);
                    double blend = Math.Min(1.0, Math.Max(blendMin, Math.Sqrt(dist / radius)));
                    blendedVert.Position.Z = demPt.Z * blend + mz * (1.0 - blend);
                }
            });

            blendedOrbitalMesh.Clean(); //removes degnerate faces
            blendedOrbitalMesh.GenerateVertexNormals(); //we moved stuff, recompute vertex normals from faces

            SaveDebugMesh(blendedOrbitalMesh, "blended-orbital");

            int nv = mesh.Vertices.Count;
            mesh.Vertices.AddRange(blendedOrbitalMesh.Vertices);
            mesh.Faces.AddRange(blendedOrbitalMesh.Faces.Select(f => new Face(f.P0 + nv, f.P1 + nv, f.P2 + nv)));

            mesh.Clean();

            SaveDebugMesh(mesh, "combined");
        }

        private void ClipMesh(double extent, bool clipToPointCloudBounds = true)
        {
            double minZ = double.NaN, maxZ = double.NaN;

            if (clipToPointCloudBounds)
            {
                pipeline.LogInfo("clipping mesh to source point cloud bounds");
                mesh.Clip(pointCloudBounds);
                minZ = pointCloudBounds.Min.Z;
                maxZ = pointCloudBounds.Max.Z;
            }
            else
            {
                var bounds = mesh.Bounds();
                minZ = bounds.Min.Z;
                maxZ = bounds.Max.Z;
            }

            if (extent > 0)
            {
                pipeline.LogInfo("clipping mesh to {0:f3} meter box around {1} frame origin in XY plane",
                                 extent, meshFrame);
                mesh.Clip(BoundsFromXYExtent(Vector3.Zero, extent, minZ, maxZ));
            }

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("clipped mesh is empty");
            }

            CleanMesh();

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("mesh is empty");
            }
        }

        private void DecimateMesh()
        {
            pipeline.LogInfo("decimating mesh with {0}, target {1} faces",
                             options.MeshDecimator, Fmt.KMG(options.TargetSceneMeshFaces));

            mesh = mesh.Decimated(options.TargetSceneMeshFaces, options.MeshDecimator); //preserves normals

            pipeline.LogInfo("decimated mesh to {0} faces", Fmt.KMG(mesh.Faces.Count));

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("mesh is empty");
            }

            SaveDebugMesh(mesh, "decimated");
        }

        private void ReduceMesh()
        {
            pipeline.LogInfo("only keeping triangles visible in observations: {0}",
                             string.Join(", ", onlyForObs.Select(obs => obs.Name)));

            var hulls = Backproject.BuildFrustumHulls(pipeline, frameCache, meshFrame, options.UsePriors,
                                                      options.OnlyAligned, onlyForObs).Values;

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
            if (options.NoOrbital || blendExtent == options.Extent || orbitalTextureMetersPerPixel <= 0)
            {
                pipeline.LogInfo("no peripheral orbital, {0} atlassing full scene mesh", options.AtlasMode);
                AtlasMesh(mesh, sceneTextureResolution);
                SaveDebugMesh(mesh, "atlassed");
                return;
            }

            double srcSurfaceFrac =  0, dstSurfaceFrac = 0;
            double res = sceneTextureResolution;

            var meshBounds = mesh.Bounds();
            var centralBounds = new BoundingBox();

            if (blendExtent == 0) //mesh is orbital only
            {
                pipeline.LogInfo("no surface data, heightmap atlassing full scene mesh");
                HeightmapAtlasMesh(mesh);
                //still do a warp if we can to allow workflow with only orbital geometry but surface + orbital texture
                if (options.SurfaceExtent > 0 && options.Extent > options.SurfaceExtent)
                {
                    srcSurfaceFrac = options.SurfaceExtent / options.Extent;
                    dstSurfaceFrac = options.MinSurfaceTextureFraction;
                    centralBounds = BoundsFromXYExtent(Vector3.Zero, options.SurfaceExtent,
                                                       meshBounds.Min.Z, meshBounds.Max.Z);
                }
            }
            else
            {
                //uvatlas or heightmap atlas central portion consisting of surface + orbital blend mesh
                //always heightmap atlas outer orbital periphery
                //we clip and then re-merge those two parts here rather than atlassing them before they are merged
                //in BlendOrbitalToSurface() to handle workflows involving DecimateMesh() and/or ReduceMesh()

                ComputeTextureWarp(options.Extent, blendExtent, out srcSurfaceFrac, out dstSurfaceFrac);

                int surfacePixels = (int)Math.Ceiling(dstSurfaceFrac * res);

                pipeline.LogInfo("{0} atlassing {1}x{1}m central submesh, resolution {2}x{2}",
                                 options.AtlasMode, blendExtent, surfacePixels);

                centralBounds = BoundsFromXYExtent(Vector3.Zero, blendExtent, meshBounds.Min.Z, meshBounds.Max.Z);

                var centralMesh = mesh.Clipped(centralBounds);

                SaveDebugMesh(centralMesh, "central");

                switch (options.AtlasMode)
                {
                    case AtlasMode.UVAtlas: UVAtlasMesh(centralMesh, surfacePixels); break;
                    case AtlasMode.Heightmap: HeightmapAtlasMesh(centralMesh); break;
                    default: throw new ArgumentException("unsupported atlas mode: " + options.AtlasMode);
                }

                SaveDebugMesh(centralMesh, "central-atlassed");

                centralMesh.RescaleUVs(BoundingBoxExtensions.CreateXY(PointToUV(meshBounds, centralBounds.Min),
                                                                      PointToUV(meshBounds, centralBounds.Max)));

                SaveDebugMesh(centralMesh, "central-atlassed-rescaled");

                var peripheralMesh = mesh.Cutted(centralBounds);
                pipeline.LogInfo("heightmap atlassing {0}m orbital periphery ({1} tris)",
                                 0.5 * (options.Extent - blendExtent), Fmt.KMG(peripheralMesh.Faces.Count));
                HeightmapAtlasMesh(peripheralMesh);

                SaveDebugMesh(peripheralMesh, "peripheral-atlassed");

                mesh = MeshMerge.Merge(msg => pipeline.LogWarn(msg), centralMesh, peripheralMesh);
            }

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
                saveDebugMeshes("prewarpAtlassed");
                
                pipeline.LogInfo("warping {0:F3}x{0:F3} central UVs to {1:F3}x{1:F3}, ease {2:F3}",
                                 srcSurfaceFrac, dstSurfaceFrac, options.EaseTextureWarp);
                
                pipeline.LogInfo("central meters per pixel: {0:F3}", blendExtent / (dstSurfaceFrac * res));
                
                pipeline.LogInfo("orbital meters per pixel: {0:F3}",
                                 (options.Extent - blendExtent) / ((1 - dstSurfaceFrac) * res));

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
            
            var sceneMesh = SceneMesh.Find(pipeline, project.Name, MeshVariant.Default);
            if (sceneMesh != null)
            {
                sceneMesh.SetBounds(mesh.Bounds());
                Mesh tmp = mesh;
                if (tmp.HasColors)
                {
                    tmp = new Mesh(mesh);
                    tmp.HasColors = false;
                }
                var meshProd = new PlyGZDataProduct(tmp);
                pipeline.SaveDataProduct(project, meshProd);
                sceneMesh.MeshGuid = meshProd.Guid;
                sceneMesh.SurfaceExtent = surfaceExtent;
                sceneMesh.Save(pipeline);
            }
            else
            {
                SceneMesh.Create(pipeline, project, mesh: mesh, surfaceExtent: surfaceExtent);
            }
        
            if (!string.IsNullOrEmpty(options.OutputMesh))
            {
                TemporaryFile.GetAndDelete(StringHelper.GetUrlExtension(options.OutputMesh), tmpFile =>
                {
                    mesh.Save(tmpFile);
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
