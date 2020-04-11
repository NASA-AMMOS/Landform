using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using CommandLine;
using Microsoft.Xna.Framework;
using OPS.Util;
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
/// map RDRs (UVW products) give each point a normal, which is usually required.  Points with bad or suspected bad
/// normals are filtered before reconstruction.  Optionally the normals can be scaled by an estimate of the confidence
/// of each point, though at this time ingestion of mission RNE products is still TODO
/// https://github.jpl.nasa.gov/OnSight/Landform/issues/766 (and such products may not even be available) so we use
/// distance from the camera as a proxy.
///
/// The reconstructed mesh is cleaned and clipped to a surface data bounding box typically 32m square around the origin
/// if the primary sitedrive frame for the contextual mesh.  Its vertex normals are recomputed from its faces to avoid
/// issues with bad normals corrupting downstream operations such as reconstruction of parent tile meshes.
///
/// Hole filling is then typically performed.  A non-convex outer boundary is computed by creating a shrinkwrap mesh
/// and finding its largest boundary polygon.  That polygon is then triangulated and used as a "surface mask" for
/// further operations.  The surface mesh is reconstructed from the full scene point cloud a second time, but this time
/// with less aggressive trimming options for Poisson reconstruction (hole filling is only implemented for Poisson
/// reconstruction).  The resulting mesh is clipped to the surface mask created from the original construction.  In this
/// way the potential undesirable effects of less aggressive Poisson surface trimming around the outer boundary of the
/// mesh are avoided, but the benefits of allowing more internal hole filling are gained.
///
/// If an orbital DEM is available a square portion of it centered on the origin of the primary sitedrive frame is
/// organized meshed.  The bounds of this mesh may be larger than the surface mesh bounds.  For example, if the surface
/// mesh bounds are 32m then the orbital mesh bounds may be 64m (or surface could be 64m and orbital 256m).  It is also
/// possible for the orbital mesh bounds to be the same as the surface mesh bounds, but they can't be smaller.
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
/// If a tileset is not required, the full scene mesh can also be directly saved with the --outputmesh option.
/// When running locally this can be either a relative or absolute disk path with an accepted mesh file extension, or
/// just the extension, in which case a default filename will be used in the current working directory.  When running
/// with --cloud the output mesh must either be a URL within the project venue storage area, or a relative path which
/// will be prepended with the project storage venue URL and "meshing/GeometryProducts", or just a known mesh format
/// extension.  The output scene mesh will not be textured.  However, if atlasing was successful, then a textured mesh
/// can be generated with build-texture.
///
/// Example:
///
/// Landform.exe build-geometry windjana --meshframe 0311472 --orbitaldem out/windjana/orbital/out_deltaradii_smg_1m.tif
///
/// </summary>
namespace OPS.Landform
{
    [Verb("build-geometry", HelpText = "create scene mesh from point clouds")]
    public class BuildGeometryOptions : GeometryCommandOptions
    {
        [Option(HelpText = "Decimate the scene mesh to this target number of faces if positive", Default = 0)]
        public int TargetSceneMeshFaces { get; set; }

        [Option(Default = MeshReconstructionMethod.Poisson, HelpText = "Mesh reconstruction method (FSSR, Poisson)")]
        public MeshReconstructionMethod ReconstructionMethod { get; set; }

        [Option(HelpText = "Stereo eye to prefer (auto, left, right, any)", Default = "auto")]
        public string StereoEye { get; set; }

        [Option(HelpText = "Disable clever combine point cloud merging", Default = false)]
        public bool NoCleverCombine { get; set; }

        [Option(HelpText = "Only include faces that intersect these observations, comma separated", Default = null)]
        public string OnlyFacesForObs { get; set; }

        [Option(HelpText = "Pre-clip observation point clouds to XY box of this size in meters around mesh frame origin if positive", Default = 0)]
        public double PreClipPointCloudExtent { get; set; }

        [Option(HelpText = "Clip reconstructed surface to XY box of this size in meters around mesh frame origin if positive", Default = 32)]
        public double ClipSurfaceExtent { get; set; }

        [Option(HelpText = "Final clip box XY size in meters, 0 to clip to aggregate point cloud bounds", Default = 64)]
        public double ClipExtent { get; set; }

        [Option(HelpText = "Surface density based trimmer octree level (higher means more aggressive, 0 disables)", Default = 7.5)]
        public double TrimmerLevel { get; set; }

        [Option(HelpText = "Fill holes in largest island created from surface trimmer, cull other islands (hole filling requires --reconstructionmethod=Poission)", Default = false)]
        public bool NoFillHoles { get; set; }

        [Option(HelpText = "Island removal based on percentage of total surface area (higher means more aggressive, 0 disables)", Default = 0.001)]
        public double TrimmerIslandPct { get; set; }

        [Option(HelpText = "Orbital sampling rate outside blend radius, non-positive to use DEM resolution", Default = -1)]
        public double OrbitalPointsPerMeter { get; set; }

        [Option(HelpText = "Orbital sampling rate inside blend radius, non-positive to use DEM resolution", Default = 15)]
        public double OrbitalBlendPointsPerMeter { get; set; }

        [Option(HelpText = "Mask resolution for clipping surface/orbital", Default = 5)]
        public double ShrinkwrapPointsPerMeter {get; set;}

        [Option(HelpText = "Blend orbital within this distance from surface in meters, 0 disables blend, negative for default", Default = BuildGeometry.DEF_BLEND_RADIUS)]
        public double OrbitalBlendRadius { get; set; }

        [Option(HelpText = "Sew orbital within this distance from surface in meters, 0 disables sew, negative for default", Default = BuildGeometry.DEF_SEW_RADIUS)]
        public double OrbitalSewRadius { get; set; }

        [Option(HelpText = "Orbital blend min blend, 0-1, larger preserves orbital more", Default = 0.1)]
        public double OrbitalBlendMin { get; set; }

        [Option(HelpText = "Clever combine cell size (meters)", Default = CleverCombine.DEF_CELL_SIZE)]
        public double CleverCombineCellSize { get; set; }

        [Option(HelpText = "Poisson cell size (meters), mutually exclusive with PoissonTreeDepth, 0 to disable", Default = 0.0)]
        public double PoissonCellSize { get; set; }

        [Option(HelpText = "Poisson octtree depth, mutually exclusive with PoissonCellSize, 0 to disable", Default = 10)]
        public int PoissonTreeDepth { get; set; }

        [Option(HelpText = "Discard observation point cloud normals with fewer than this many valid 8-neighbors", Default = 8)]
        public int NormalFilter { get; set; }

        [Option(HelpText = "Scale observation point cloud normals by confidence", Default = true)]
        public bool UsePointCloudConfidence { get; set; }

        [Option(HelpText = "Min required samples per octree cell in Poisson reconstruction, higher for noiser data", Default = 15)]
        public int PoissonMinSamplesPerCell { get; set; }

        [Option(HelpText = "Poisson reconstruction BSpline degree", Default = 2)]
        public int PoissonBSplineDegree { get; set; }

        [Option(HelpText = "Generate full-mesh UVs with UVAtlas", Default = false)]
        public bool GenerateUVs { get; set; }

        [Option(HelpText = "Texture resolution, used if generating UVs, should be power of two", Default = 4096)]
        public int TextureResolution { get; set; }

        [Option(HelpText = "URL, file, or file type (extension starting with \".\") to which to save scene mesh", Default = null)]
        public string OutputMesh { get; set; }
    }

    public class BuildGeometry : GeometryCommand
    {
        private const string OUT_DIR = "meshing/GeometryProducts";

        public const double DEF_BLEND_RADIUS = 3;
        public const double DEF_SEW_RADIUS = 0.2;

        public const int BLEND_GUTTER_SAMPLES = 4;

        private string dbgMeshPrefix;

        private BuildGeometryOptions options;

        private Observation[] onlyForObs;
        private PoissonReconstruction.Options poissonOpts;

        private ConcurrentDictionary<string, Mesh> observationPointClouds = new ConcurrentDictionary<string, Mesh>();
        private Mesh pointCloud;
        private BoundingBox pointCloudBounds;
        private Mesh mesh;
        private SceneMesh sceneMesh;

        private Mesh shrinkwrapMesh;
        private MeshOperator maskUVMeshOp;

        private Mesh orbitalMesh;

        private double blendRadius, sewRadius;
        private int blendSamplesPerPixel, orbitalSamplesPerPixel;

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
                }

                if (!options.NoSurface && (!options.NoFillHoles || !options.NoOrbital))
                {
                    RunPhase("clip surface mesh", ClipSurfaceMesh);
                    RunPhase("create shrinkwrapped surface mesh", CreateShrinkwrappedSurfaceMesh);
                    RunPhase("create surface mask mesh", CreateSurfaceMaskMesh);
                    if (maskUVMeshOp != null) //CreateSurfaceMaskMesh() failed
                    {
                        RunPhase("reconstruct surface to mask", ReconstructSurfaceToMask);
                    }
                }

                if (options.NoOrbital)
                {
                    //just clip surface mesh
                    double extent = options.ClipExtent;
                    if (extent <= 0 || (options.ClipSurfaceExtent > 0 && options.ClipSurfaceExtent < extent))
                    {
                        extent = options.ClipSurfaceExtent;
                    }
                    RunPhase("clip mesh", () => ClipMesh(extent));
                }
                else
                {
                    //surface mesh (if any) has already been clipped
                    //and we've already verified that 0 < ClipSurfaceExtent < ClipExtent
                    //now build orbital to ClipExtent

                    RunPhase("build orbital mesh", BuildOrbitalMesh);

                    if (options.NoSurface)
                    {
                        mesh = orbitalMesh;
                    }
                    else if (options.OrbitalBlendRadius > 0 || options.OrbitalSewRadius > 0)
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
                    RunPhase("filter mesh", FilterMesh);
                }

                if (options.GenerateUVs)
                {
                    RunPhase("atlas mesh", () => AtlasMesh(options.TextureResolution));
                }

                RunPhase("save mesh", SaveMesh);
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

            if ((!options.NoOrbital || !options.NoFillHoles) &&
                options.ReconstructionMethod != MeshReconstructionMethod.Poisson)
            {
                throw new Exception("orbital geometry and hole filling require poisson surface trimmer");
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

            if (!options.NoOrbital)
            {
                LoadOrbitalDEM(new SiteDrive(meshFrame)); //may overwrite options.NoOrbital
                
                if (options.NoOrbital && options.NoSurface)
                {
                    throw new Exception("--nosurface but failed to load orbital");
                }
            }
            
            onlyForObs = observationCache.ParseList(options.OnlyFacesForObs);

            poissonOpts = new PoissonReconstruction.Options
            {
                //extrapolates the edges of the mesh
                Boundary = PoissonReconstruction.BoundaryTypes.Neumann,

                // no features should be finer than this many meters as this is the finest the octree will dice
                MinOctreeCellWidthMeters = (float)(options.PoissonCellSize),

                // depth the octree should resolve to. mutually exclusive with MinOctreeCellWidthMeters
                OctreeDepth = options.PoissonTreeDepth,

                // a value on the upper end of the suggested range in the docs
                // meaning we think our data in noisy, so wait for this many samples in a cell
                MinOctreeSamplesPerCell = options.PoissonMinSamplesPerCell,
                
                // attempts to allow higher order surfaces than the defaults
                BSplineDegree = options.PoissonBSplineDegree,
                
                // indicates the normal magnitudes are not uniformly unit scaled
                // to indicate confidence in the position attached to it
                UseNormalsForConfidence = options.UsePointCloudConfidence,

                // remove low density points
                TrimmerLevel = options.TrimmerLevel,

                // remove disconnected islands of pts
                TrimmerIslandPct = options.TrimmerIslandPct
            };

            var obsNames = onlyForObs.Select(o => o.Name).ToArray();
            dbgMeshPrefix = SceneMesh.MakeName(meshFrame, MeshVariant.Default, siteDrives, obsNames);

            if (!string.IsNullOrEmpty(options.OutputMesh))
            {
                options.OutputMesh =
                    CheckOutputURL(options.OutputMesh, dbgMeshPrefix, OUT_DIR, MeshSerializers.Instance);
            }

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

            if (!options.NoOrbital && (options.ClipSurfaceExtent <= 0 || options.ClipExtent <= 0 ||
                                       options.ClipSurfaceExtent > options.ClipExtent))
            {
                throw new Exception(string.Format("surface clip {0} must be greater than 0 and less than outer clip {1}"
                                                  + " to use orbital", options.ClipSurfaceExtent, options.ClipExtent));
            }

            orbitalSamplesPerPixel = 1;
            if (options.OrbitalPointsPerMeter > 0)
            {
                orbitalSamplesPerPixel = (int)Math.Ceiling(options.OrbitalPointsPerMeter * orbitalAvgMetersPerPixel);
            }
            
            blendSamplesPerPixel = 1;
            if (options.OrbitalBlendPointsPerMeter > 0)
            {
                blendSamplesPerPixel = (int)Math.Ceiling(options.OrbitalBlendPointsPerMeter * orbitalAvgMetersPerPixel);
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
                    RequirePoints = true,
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

            var meshOpts = new WedgeObservations.MeshOptions()
            {
                Frame = meshFrame,
                NormalFilter = options.NormalFilter,
                ScaleNormalsByConfidence = options.UsePointCloudConfidence
            };

            int no = wedges.Count;
            pipeline.LogInfo("building point clouds for {0} wedges", no);
            int np = 0, nc = 0, nf = 0;
            CoreLimitedParallel.ForEach(wedges, obs =>
            {
                Interlocked.Increment(ref np);

                //bookeep name of the points observation so that we can recover its observation transform later
                string ptsName = obs.Points.Name;

                if (!options.NoProgress)
                {
                    pipeline.LogInfo("building {0} wedge point clouds in parallel, completed {1}/{2}, {3} failed",
                                     np, nc, no, nf);
                }

                var mo = meshOpts.Clone();
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
                        pc = Mesh.Clip(pc, bounds);
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
            if (options.NoCleverCombine)
            {
                var clouds = observationPointClouds.Values.ToArray();
                int nv = clouds.Sum(pc => pc.Vertices.Count);
                pipeline.LogInfo("merging {0} observation point clouds without clever combine, total {1} points",
                                 clouds.Length, Fmt.KMG(nv));
                pointCloud = new Mesh(hasNormals: true);
                pointCloud.MergeWith(clouds, normalize: false, removeDuplicateVerts: false);
            }
            else
            {
                var clouds = new List<Mesh>();
                var origins = new List<Vector3>();
                foreach (var entry in observationPointClouds)
                {
                    clouds.Add(entry.Value);
                    var pointsObs = observationCache.GetObservation(entry.Key);
                    var pointsCam = pointsObs.CameraModel as CAHV;
                    var obsToMesh = frameCache.GetObservationTransform(pointsObs, meshFrame,
                                                                       options.UsePriors, options.OnlyAligned);
                    //obsToMesh cannot be null here because WedgeObservations.BuildPointCloud() returned non-null
                    if (pointsCam != null)
                    {
                        //the reference point used to determine how good a point is for clever combine
                        //naive version is using distance from camera
                        origins.Add(Vector3.Transform(pointsCam.C, obsToMesh.Mean));
                    }
                    else
                    {
                        pipeline.LogWarn("no CAHV camera model for observation {0}, " +
                                         "using observation frame origin for clever combine", entry.Key);
                        origins.Add(Vector3.Transform(Vector3.Zero, obsToMesh.Mean));
                    }
                }
                int nv = clouds.Sum(pc => pc.Vertices.Count);
                pipeline.LogInfo("clever combining {0} observation point clouds, cell size {1}, total {2} points",
                                 clouds.Count, options.CleverCombineCellSize, Fmt.KMG(nv));
                var cc = new CleverCombine(options.CleverCombineCellSize);
                pointCloud = cc.Combine(origins.ToArray(), clouds.ToArray(), pipeline);
                pipeline.LogInfo("clever combine returned {0} points", Fmt.KMG(pointCloud.Vertices.Count));
            }

            //significant memory usage
            observationPointClouds.Clear();

            pointCloudBounds = pointCloud.Bounds();
        }

        private void ReconstructMesh()
        {
            var pc = pointCloud;

            pipeline.LogInfo("reconstructing mesh from {0} points with {1}",
                             Fmt.KMG(pc.Vertices.Count), options.ReconstructionMethod);

            switch (options.ReconstructionMethod)
            {
                case MeshReconstructionMethod.FSSR: mesh = FSSR.Reconstruct(pc); break;
                case MeshReconstructionMethod.Poisson: mesh = PoissonReconstruction.Reconstruct(pc, poissonOpts); break;
                default: throw new Exception("unsupported reconstruction method: " + options.ReconstructionMethod);
            }
            if (mesh == null || mesh.Faces.Count == 0)
            {
                throw new Exception("failed to build mesh");
            }

            mesh.RemoveFloaters();

            //both FSSR and Poisson require normals on their input mesh and write normals to their output mesh
            //however we have seen issues with these normals
            //and also they may be confidence scaled still
            //one option would be to just clear the normals and not include normals with the output scene mesh
            //but some kinds of later processing (like building parent tile meshes) will want them
            //so let's just regenerate them from the faces and write them to the scene mesh
            //because we're dealing with natural terrain it is pretty reasonable to compute vertex normals from faces
            //i.e. no sharp crease angles expected
            mesh.Clean(); //removes degenerate faces

            if (options.WriteDebug)
            {
                var colored = new Mesh(mesh);
                var red = new Vector3(1, 0, 0);
                var green = new Vector3(0, 1, 0);
                colored.ColorByNormalMagnitude(red, green);
                SaveMesh(colored, dbgMeshPrefix + "-confidence");
            }

            mesh.GenerateVertexNormals();
        }

        private void ClipSurfaceMesh()
        {
            ClipMesh(options.ClipSurfaceExtent);
            if (options.WriteDebug)
            {
                SaveMesh(mesh, dbgMeshPrefix + "-clippedSurface");
            }
        }

        private void CreateShrinkwrappedSurfaceMesh()
        {
            var bounds = mesh.Bounds();
            Mesh grid = Shrinkwrap.BuildGrid(bounds,
                                             (int)(bounds.Size().X * options.ShrinkwrapPointsPerMeter),
                                             (int)(bounds.Size().Y * options.ShrinkwrapPointsPerMeter),
                                             VertexProjection.ProjectionAxis.Z);
            shrinkwrapMesh = Shrinkwrap.Wrap(grid, mesh, Shrinkwrap.ShrinkwrapMode.Project,
                                             VertexProjection.ProjectionAxis.Z,
                                             Shrinkwrap.ProjectionMissResponse.Clip);
            shrinkwrapMesh.Clean();

            if (options.WriteDebug)
            {
                SaveMesh(shrinkwrapMesh, dbgMeshPrefix + "-shrinkwrap");
            }
        }

        private void CreateSurfaceMaskMesh()
        {
            try
            {
                EdgeGraph edgeGraph = new EdgeGraph(shrinkwrapMesh);
                List<Edge> perimeterEdges = edgeGraph.GetLargestPolygonalBoundary();
                
                EdgeGraph.EnsureCCW(perimeterEdges);
                
                var maskMesh = new Mesh();
                maskMesh.Vertices = perimeterEdges.Select(e => new Vertex(e.Src.Position)).ToList();
                
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
                
                maskMesh.ReverseWinding();
                
                if (options.WriteDebug)
                {
                    SaveMesh(maskMesh, dbgMeshPrefix + "-surfaceMask");
                }

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

        private void ReconstructSurfaceToMask()
        {
            //TODO: Have Poisson return both clipped and non-clipped to avoid remeshing
            poissonOpts.TrimmerLevel = Math.Max(0, poissonOpts.TrimmerLevel - 2);
            mesh = PoissonReconstruction.Reconstruct(pointCloud, poissonOpts);

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

            mesh.RemoveFloaters();
            mesh.Clean(); //removes degenerate faces
            mesh.GenerateVertexNormals(); //important, see comments in ReconstructMesh()

            if (options.WriteDebug)
            {
                SaveMesh(mesh, dbgMeshPrefix + "-maskedSurface");
            }
        }

        private void BuildOrbitalMesh()
        {
            var maskOp = maskUVMeshOp;
            if (maskOp == null && mesh != null) //CreateSurfaceMaskMesh() failed
            {
                var tmp = new Mesh(mesh);
                tmp.XYToUV();
                maskOp = new MeshOperator(tmp, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
            }

            var meshToRoot = frameCache.GetBestTransform(meshFrame).Transform.Mean;
            var orbitalToMesh = orbitalToRoot * Matrix.Invert(meshToRoot);

            Mesh makeMesh(int subsample, Image.Subrect outerBounds, Image.Subrect innerBounds = null)
            {
                int w = (outerBounds.Width - 1) * subsample + 1;
                int h = (outerBounds.Height - 1) * subsample + 1;
                double eps = 0.1;
                var points = new Image(3, w, h);
                points.CreateMask();
                for (int r = 0; r < h; r++)
                {
                    for (int c = 0; c < w; c++)
                    {
                        Vector2 px = outerBounds.Linterp(((double)c) / (w - 1), ((double)r) / (h - 1));
                        bool mask = true;
                        if (innerBounds == null || !innerBounds.ContainsProper(px, eps))
                        {
                            var pt = orbitalDEM.GetInterpolatedXYZ(px);
                            if (pt.HasValue)
                            {
                                pt = Vector3.Transform(pt.Value, orbitalToMesh);
                                if (maskOp.UVToBarycentric(new Vector2(pt.Value.X, pt.Value.Y)) == null)
                                {
                                    points[0, r, c] = (float)pt.Value.X;
                                    points[1, r, c] = (float)pt.Value.Y;
                                    points[2, r, c] = (float)pt.Value.Z;
                                    mask = false;
                                }
                            }
                        }
                        if (mask)
                        {
                            points.SetMaskValue(r, c, true);
                        }
                    }
                }
                return OrganizedPointCloud.BuildOrganizedMesh(points, generateUV: false, generateNormals: true);
            }

            int orbitalExtentPixels = (int)Math.Ceiling(0.5 * options.ClipExtent / orbitalAvgMetersPerPixel);

            double br = blendRadius > 0 ? blendRadius : 0;
            int blendExtentPixels =
                (int)Math.Ceiling(0.5 * (options.ClipSurfaceExtent + br) / orbitalAvgMetersPerPixel);

            Image.Subrect blendBounds = null;
            if (blendSamplesPerPixel != orbitalSamplesPerPixel)
            {
                blendBounds = orbitalDEM.GetSubrectPixels(blendExtentPixels);
            }

            pipeline.LogInfo("making {0}x{0} orbital mesh at {1} samples/meter",
                             2* orbitalExtentPixels * orbitalAvgMetersPerPixel,
                             orbitalSamplesPerPixel / orbitalAvgMetersPerPixel);

            orbitalMesh =
                makeMesh(orbitalSamplesPerPixel, orbitalDEM.GetSubrectPixels(orbitalExtentPixels), blendBounds);

            pipeline.LogInfo("made orbital mesh with {0} triangles", Fmt.KMG(orbitalMesh.Faces.Count));

            if (blendBounds != null)
            {
                if (options.WriteDebug)
                {
                    SaveMesh(orbitalMesh, dbgMeshPrefix + "-outerOrbital");
                }

                pipeline.LogInfo("making {0}x{0} orbital blend mesh at {1} samples/meter",
                                 2 * blendExtentPixels * orbitalAvgMetersPerPixel,
                                 blendSamplesPerPixel / orbitalAvgMetersPerPixel);

                var blendMesh = makeMesh(blendSamplesPerPixel, blendBounds);

                if (options.WriteDebug)
                {
                    SaveMesh(blendMesh, dbgMeshPrefix + "-preblendOrbital");
                }

                pipeline.LogInfo("made orbital blend mesh with {0} triangles, merging with orbital",
                                 Fmt.KMG(blendMesh.Faces.Count));

                orbitalMesh.MergeWith(blendMesh);

                pipeline.LogInfo("total orbital mesh size {0} triangles", Fmt.KMG(orbitalMesh.Faces.Count));
            }
                
            if (options.WriteDebug)
            {
                SaveMesh(orbitalMesh, dbgMeshPrefix + "-orbital");
            }
        }

        private void BlendOrbitalToSurface()
        {
            if (blendRadius == 0 && sewRadius == 0)
            {
                return;
            }

            double radius = this.blendRadius;

            if (BLEND_GUTTER_SAMPLES > 0)
            {
                double gutterMeters = BLEND_GUTTER_SAMPLES * (orbitalAvgMetersPerPixel / blendSamplesPerPixel);
                if (radius > gutterMeters)
                {
                    radius -= gutterMeters;
                }
            }

            var meshOp = new MeshOperator(mesh, buildFaceTree: false, buildVertexTree: true, buildUVFaceTree: false);

            double blendMin = options.OrbitalBlendMin;
            double smoothRadius = 0.1 * radius;

            double boundsRadius = options.ClipSurfaceExtent > 0 ? options.ClipSurfaceExtent + radius : 0;
            double blendRadiusSq = radius * radius;
            double sewRadiusSq = sewRadius * sewRadius;

            pipeline.LogInfo("collecting nearest surface vertices within {0}m of orbital", radius);
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
                        vertPairs[i]= closest;
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

            if (options.WriteDebug)
            {
                SaveMesh(blendedOrbitalMesh, dbgMeshPrefix + "-blendedOrbital");
            }

            int nv = mesh.Vertices.Count;
            mesh.Vertices.AddRange(blendedOrbitalMesh.Vertices);
            mesh.Faces.AddRange(blendedOrbitalMesh.Faces.Select(f => new Face(f.P0 + nv, f.P1 + nv, f.P2 + nv)));

            mesh.Clean();
        }

        private void ClipMesh(double extent, bool clipToPointCloudBounds = true)
        {
            double minZ = double.NaN, maxZ = double.NaN;

            if (clipToPointCloudBounds)
            {
                pipeline.LogInfo("clipping mesh to source point cloud bounds");
                mesh = Mesh.Clip(mesh, pointCloudBounds);
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
                pipeline.LogInfo("clipping mesh to {0} meter box around {1} frame origin in XY plane",
                                 extent, meshFrame);
                mesh = Mesh.Clip(mesh, BoundsFromXYExtent(Vector3.Zero, extent, minZ, maxZ));
            }

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("clipped mesh is empty");
            }

            mesh.RemoveFloaters();
            mesh.Clean(); //removes degenerate faces
            mesh.GenerateVertexNormals();

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("mesh is empty");
            }
        }

        private void DecimateMesh()
        {
            pipeline.LogInfo("decimating mesh with {0}, target {1} faces",
                             options.MeshDecimator, options.TargetSceneMeshFaces);

            mesh = mesh.Decimate(options.TargetSceneMeshFaces, options.MeshDecimator);

            pipeline.LogInfo("decimated mesh to {0} faces", mesh.Faces.Count);

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("mesh is empty");
            }
        }

        private void FilterMesh()
        {
            pipeline.LogInfo("only keeping triangles visible in observations: {0}",
                             string.Join(", ", onlyForObs.Select(obs => obs.Name)));

            var hulls = Backproject.BuildConvexHulls(pipeline, frameCache, meshFrame, options.UsePriors,
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

            pipeline.LogInfo("kept {0} faces visible in specified observations", mesh.Faces.Count);
        }

        private void AtlasMesh(int textureResolution)
        {
            pipeline.LogInfo("atlasing {0} triangles with UVAtlas, texture resolution {1}",
                             Fmt.KMG(mesh.Faces.Count), textureResolution);

            //TODO https://github.jpl.nasa.gov/OnSight/Landform/issues/902
            pipeline.LogWarn("UVAtlas may not work well on large meshes");

            mesh = UVAtlas.Atlas(mesh, textureResolution, textureResolution);

            if (mesh == null)
            {
                throw new Exception("unknown error atlasing mesh");
            }
        }

        private void SaveMesh()
        {
            if (!options.NoSave)
            {
                pipeline.LogInfo("saving scene mesh in frame {0} to project storage", meshFrame);
                double surfaceExtent = -1; //unlimited
                if (options.NoSurface)
                {
                    surfaceExtent = 0; //only orbital
                }
                else if (options.ClipSurfaceExtent > 0)
                {
                    surfaceExtent = options.ClipSurfaceExtent;
                }
                string[] obsNames = onlyForObs.Select(obs => obs.Name).ToArray();
                var variant = MeshVariant.Default;
                sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame, variant, siteDrives, obsNames);
                if (sceneMesh != null)
                {
                    sceneMesh.SetBounds(mesh.Bounds());
                    var meshProd = new PlyGZDataProduct(mesh);
                    pipeline.SaveDataProduct(project, meshProd);
                    sceneMesh.MeshGuid = meshProd.Guid;
                    sceneMesh.SurfaceExtent = surfaceExtent;
                    sceneMesh.Save(pipeline);
                }
                else
                {
                    sceneMesh = SceneMesh.Create(pipeline, project, meshFrame, variant, siteDrives, obsNames,
                                                 mesh: mesh, surfaceExtent: surfaceExtent);
                }
            }
                
            if (options.WriteDebug)
            {
                SaveMesh(mesh, dbgMeshPrefix);
            }

            if (!string.IsNullOrEmpty(options.OutputMesh))
            {
                TemporaryFile.GetAndDelete(StringHelper.GetUrlExtension(options.OutputMesh), tmpFile =>
                {
                    mesh.Save(tmpFile);
                    pipeline.SaveFile(tmpFile, options.OutputMesh, constrainToStorage: false);
                });
            }

            var bounds = mesh.Bounds().Size();
            pipeline.LogInfo("scene bounds (meters): {0:F3}x{1:F3}x{2:F3}", bounds.X, bounds.Y, bounds.Z);
        }
    }
}
