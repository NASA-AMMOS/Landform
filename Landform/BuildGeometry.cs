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

        [Option(HelpText = "Surface density based trimmer octree level (higher means more agressive, 0 disables)", Default = 8.0)]
        public double TrimmerLevel { get; set; }

        [Option(HelpText = "Fill holes in largest island created from surface trimmer, cull other islands (hole filling requires --reconstructionmethod=Poission)", Default = false)]
        public bool NoFillHoles { get; set; }

        [Option(HelpText = "Island removal based on percentage of total surface area (higher means more agressive, 0 disables)", Default = 0.001)]
        public double TrimmerIslandPct { get; set; }

        [Option(HelpText = "Don't use orbital to fill in outer edges of mesh (orbital requires --reconstructionmethod=Poission)", Default = false)]
        public bool NoOrbital { get; set; }

        [Option(HelpText = "Orbital resolution, interpolates for higher density", Default = 20)]
        public double OrbitalPointsPerMeter { get; set; }

        [Option(HelpText = "Mask resolution for clipping surface/orbital", Default = 5)]
        public double ShrinkwrapPointsPerMeter {get; set;}

        [Option(HelpText = "Only use orbital beyond this distance from surface in meters", Default = 0.25)]
        public double FilterRadius { get; set; }

        [Option(HelpText = "Blend orbital within this distance from surface in meters", Default = 5)]
        public double OrbitalBlendRadius { get; set; }

        [Option(HelpText = "Orbital blend min blend, 0-1, larger preserves orbital more", Default = 0.1)]
        public double OrbitalBlendMin { get; set; }

        [Option(HelpText = "Sew orbital within this distance from surface in meters", Default = 0.2)]
        public double OrbitalSewRadius { get; set; }

        [Option(Required = false, Default = null, HelpText = "Override default orbital DEM file path")]
        public string OrbitalDEM { get; set; }

        [Option(Required = false, Default = DEM.DEF_MIN_FILTER, HelpText = "DEM values less than this will be ignored")]
        public double DEMMinFilter { get; set; }

        [Option(Required = false, Default = DEM.DEF_MAX_FILTER, HelpText = "DEM larger than this will be ignored")]
        public double DEMMaxFilter { get; set; }

        [Option(HelpText = "Clever combine cell size (meters)", Default = CleverCombine.DEF_CELL_SIZE)]
        public double CleverCombineCellSize { get; set; }

        [Option(HelpText = "Poisson cell size (meters), mutually exclusive with PoissonTreeDepth, 0 to disable", Default = 0.0)]
        public double PoissonCellSize { get; set; }

        [Option(HelpText = "Deform oribital to fit surface", Default = false)]
        public bool AdjustOrbital { get; set; }

        [Option(HelpText = "Poisson octtree depth, mutually exclusive with PoissonCellSize, 0 to disable", Default = 10)]
        public int PoissonTreeDepth { get; set; }
    }

    public class BuildGeometry : GeometryCommand
    {
        private const string OUT_DIR = "meshing/GeometryProducts";

        private BuildGeometryOptions options;

        private Observation[] onlyForObs;
        private PoissonReconstruction.Options poissonOpts;

        private ConcurrentDictionary<string, Mesh> observationPointClouds = new ConcurrentDictionary<string, Mesh>();
        private Mesh pointCloud;
        private BoundingBox pointCloudBounds;
        private Mesh mesh;
        private SceneMesh sceneMesh;

        private DEM orbitalDEM;
        private Matrix orbitalToMesh, meshToOrbital;

        private Mesh shrinkwrappedSurface;
        private Mesh surfaceMaskMesh;
        private Mesh orbitalMesh;

        private MeshOperator maskUVMeshOp; //for surfaceMaskMesh

        private string dbgMeshPrefix;

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

                RunPhase("build observation point clouds", BuildObservationPointClouds);
                RunPhase("merge point clouds", MergePointClouds);
                RunPhase("reconstruct mesh", ReconstructMesh);

                if (!options.NoOrbital)
                {
                    RunPhase("load orbital DEM", LoadOrbital); //may overwrite options.NoOrbital
                }

                if (options.NoOrbital)
                {
                    dbgMeshPrefix += "-noOrbital";
                }

                if (!options.NoFillHoles || !options.NoOrbital)
                {
                    RunPhase("clip surface mesh", ClipSurfaceMesh);
                    RunPhase("create shrinkwrapped surface mesh", CreateShrinkwrappedSurfaceMesh);
                    RunPhase("create surface mask mesh", CreateSurfaceMaskMesh);
                    RunPhase("reconstruct surface to mask", ReconstructSurfaceToMask);
                }

                if (!options.NoOrbital)
                {
                    RunPhase("reconstruct orbital to mask", ReconstructOrbitalToMask);
                    RunPhase("blend orbital to surface", BlendOrbitalToSurface);
                }
                else if (options.NoFillHoles)
                {
                    //no orbital or hole filling, just clip surface mesh
                    double extent = options.ClipExtent;
                    if (extent <= 0 || (options.ClipSurfaceExtent > 0 && options.ClipSurfaceExtent < extent))
                    {
                        extent = options.ClipSurfaceExtent;
                    }
                    RunPhase("clip final mesh", () => ClipMesh(extent));
                }

                if (options.TargetSceneMeshFaces > 0)
                {
                    RunPhase("decimate mesh", DecimateMesh);
                }

                if (onlyForObs.Length > 0)
                {
                    RunPhase("filter mesh", FilterMesh);
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

            if (!base.ParseArgumentsAndLoadCaches(OUT_DIR))
            {
                return false; //help
            }

            if (!options.NoOrbital && !SiteDrive.IsSiteDriveString(meshFrame))
            {
                throw new Exception(string.Format("mesh frame {0} is not a site drive, cannot use orbital", meshFrame));
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
                MinOctreeSamplesPerCell = 15,
                
                // attempts to allow higher order surfaces than the defaults
                BSplineDegree = 2,
                
                // indicates the normal magnitudes are not uniformly unit scaled
                // to indicate confidence in the position attached to it
                UseNormalsForConfidence = true,

                // remove low density points
                TrimmerLevel = options.TrimmerLevel,

                // remove disconnected islands of pts
                TrimmerIslandPct = options.TrimmerIslandPct
            };

            var obsNames = onlyForObs.Select(o => o.Name).ToArray();
            dbgMeshPrefix = SceneMesh.MakeName(meshFrame, MeshVariant.Default, siteDrives, obsNames);

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

            var meshOpts = new WedgeObservations.MeshOptions() { Frame = meshFrame, ScaleNormalsByConfidence = true };

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
                    var pointsCam = ((CameraModel)JsonHelper.FromJson(pointsObs.CameraModel)) as CAHV;
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

        private void LoadOrbital()
        {
            try
            {
                orbitalDEM = mission.LoadOrbital(new SiteDrive(meshFrame), options.OrbitalDEM,
                                                 minFilter: options.DEMMinFilter, maxFilter: options.DEMMaxFilter,
                                                 logger: pipeline);
            }
            catch (Exception ex)
            {
                pipeline.LogWarn("failed to load orbital DEM or PlacesDB, running without orbital: {0}", ex.Message);
                options.NoOrbital = true;
                return;
            }

            FrameTransform ft = frameCache.GetBestTransform(OrbitalConfig.Instance.OrbitalFrameName);
            if (ft == null)
            {
                pipeline.LogWarn("failed to retrieve aligned orbital transform");
                options.NoOrbital = true;
                return;
            }

            var orbitalToWorld = ft.Transform.Mean;
            var meshToWorld = frameCache.GetBestTransform(meshFrame).Transform.Mean;
            orbitalToMesh = orbitalToWorld * Matrix.Invert(meshToWorld);
            meshToOrbital = Matrix.Invert(orbitalToMesh);
        }

        private void CreateShrinkwrappedSurfaceMesh()
        {
            var bounds = mesh.Bounds();
            Mesh grid = Shrinkwrap.BuildGrid(bounds,
                                             (int)(bounds.Size().X * options.ShrinkwrapPointsPerMeter),
                                             (int)(bounds.Size().Y * options.ShrinkwrapPointsPerMeter),
                                             VertexProjection.ProjectionAxis.Z);
            shrinkwrappedSurface = Shrinkwrap.Wrap(grid, mesh, Shrinkwrap.ShrinkwrapMode.Project,
                                                   VertexProjection.ProjectionAxis.Z,
                                                   Shrinkwrap.ProjectionMissResponse.Clip);
            shrinkwrappedSurface.Clean();

            if (options.WriteDebug)
            {
                SaveMesh(shrinkwrappedSurface, dbgMeshPrefix + "-shrinkwrap");
            }
        }

        private void CreateSurfaceMaskMesh()
        {
            EdgeGraph edgeGraph = new EdgeGraph(shrinkwrappedSurface);
            List<Edge> perimeterEdges = edgeGraph.GetLargestPolygonalBoundary();

            //Ensure perimeter orientation is CCW
            EdgeGraph.EnsureCCW(perimeterEdges);

            surfaceMaskMesh = new Mesh();
            surfaceMaskMesh.Vertices = perimeterEdges.Select(e => new Vertex(e.Src.Position)).ToList();

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
                    surfaceMaskMesh.Faces.Add(new Face(e.Src.ID, e.Dst.ID, e.Left.ID));
                }
            }

            surfaceMaskMesh.ReverseWinding();

            if (options.WriteDebug)
            {
                SaveMesh(surfaceMaskMesh, dbgMeshPrefix + "-surfaceMask");
            }

            foreach (Vertex v in surfaceMaskMesh.Vertices)
            {
                v.UV = new Vector2(v.Position.X, v.Position.Y);
            }
            surfaceMaskMesh.HasUVs = true;

            maskUVMeshOp =
                new MeshOperator(surfaceMaskMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
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

        private void ReconstructOrbitalToMask()
        {
            var cfg = OrbitalConfig.Instance;

            int orbitalRadiusPixels = (int)Math.Ceiling(0.5 * options.ClipExtent / cfg.OrbitalDEMMetersPerPixel);

            var bounds = orbitalDEM.GetSubrectPixels(orbitalRadiusPixels);
            int samplesPerPixel = (int)Math.Ceiling(options.OrbitalPointsPerMeter * cfg.OrbitalDEMMetersPerPixel);
            var points = new Image(3, bounds.Width * samplesPerPixel, bounds.Height * samplesPerPixel);
            points.CreateMask();
            double step = 1.0 / samplesPerPixel;
            for (double r = bounds.MinY; r <= bounds.MaxY; r += step)
            {
                for (double c = bounds.MinX; c <= bounds.MaxX; c += step)
                {
                    int cc = (int)((c - bounds.MinX) / step);
                    int rr = (int)((r - bounds.MinY) / step);
                    bool mask = true;
                    var px = new Vector2(c, r);
                    var pt = orbitalDEM.GetInterpolatedXYZ(px);
                    if (pt.HasValue)
                    {
                        pt = Vector3.Transform(pt.Value, orbitalToMesh);
                        if (maskUVMeshOp.UVToBarycentric(new Vector2(pt.Value.X, pt.Value.Y)) == null)
                        {
                            points[0, rr, cc] = (float)pt.Value.X;
                            points[1, rr, cc] = (float)pt.Value.Y;
                            points[2, rr, cc] = (float)pt.Value.Z;
                            mask = false;
                        }
                    }
                    if (mask)
                    {
                        points.SetMaskValue(rr, cc, true);
                    }
                }
            }

            orbitalMesh = OrganizedPointCloud.BuildOrganizedMesh(points, generateUV: false, generateNormals: true);

            if (options.WriteDebug)
            {
                SaveMesh(orbitalMesh, dbgMeshPrefix + "-orbital");
            }
        }

        private void BlendOrbitalToSurface()
        {
            var meshOp = new MeshOperator(mesh, buildFaceTree: false, buildVertexTree: true, buildUVFaceTree: false);

            double blendMin = options.OrbitalBlendMin;
            double blendRadius = options.OrbitalBlendRadius;
            double sewRadius = options.OrbitalSewRadius;
            double smoothRadius = 0.1 * blendRadius;

            double boundsRadius = options.ClipSurfaceExtent > 0 ? options.ClipSurfaceExtent + blendRadius : 0;
            double blendRadiusSq = blendRadius * blendRadius;
            double sewRadiusSq = sewRadius * sewRadius;

            pipeline.LogInfo("collecting nearest surface vertices within {0}m of orbital", blendRadius);
            var vertPairs = new ConcurrentDictionary<int, int>(); //orbitalMesh vert index -> mesh vert index
            CoreLimitedParallel.For(0, orbitalMesh.Vertices.Count, i =>
            {
                var demVert = orbitalMesh.Vertices[i];
                Vector3 demPt = demVert.Position;
                if (boundsRadius <= 0 || (Math.Abs(demPt.X) <= boundsRadius && Math.Abs(demPt.Y) <= boundsRadius))
                {
                    double minDistSq = double.PositiveInfinity;
                    int closest = -1;
                    foreach (var j in meshOp.NearestVertexIndicesXY(demVert.Position, blendRadius))
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
                    double blend = Math.Min(1.0, Math.Max(blendMin, Math.Sqrt(dist / blendRadius)));
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

        private void SaveMesh()
        {
            if (!options.NoSave)
            {
                pipeline.LogInfo("saving scene mesh in frame {0} to project storage", meshFrame);
                string[] obsNames = onlyForObs.Select(obs => obs.Name).ToArray();
                var variant = MeshVariant.Default;
                sceneMesh = SceneMesh.Find(pipeline, project.Name, meshFrame, variant, siteDrives, obsNames);
                if (sceneMesh != null)
                {
                    sceneMesh.SetBounds(mesh.Bounds());
                    var meshProd = new PlyGZDataProduct(mesh);
                    pipeline.SaveDataProduct(project, meshProd);
                    sceneMesh.MeshGuid = meshProd.Guid;
                    sceneMesh.Save(pipeline);
                }
                else
                {
                    sceneMesh = SceneMesh.Create(pipeline, project, meshFrame, variant, siteDrives, obsNames,
                                                 mesh: mesh);
                }
            }
                
            if (options.WriteDebug)
            {
                SaveMesh(mesh, dbgMeshPrefix);
            }

            var bounds = mesh.Bounds().Size();
            pipeline.LogInfo("scene bounds (meters): {0:F3}x{1:F3}x{2:F3}", bounds.X, bounds.Y, bounds.Z);
        }
    }
}
