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
        public double PreClipExtent { get; set; }

        [Option(HelpText = "Post-meshing clip box XY size in meters, 0 to clip to aggregate point cloud bounds", Default = 32)]
        public double ClipExtent { get; set; }

        [Option(HelpText = "Surface density based trimmer octree level (higher means more agressive, 0 disables)", Default = 8.0)]
        public double TrimmerLevel { get; set; }

        [Option(HelpText = "Island removal based on percentage of total surface area (higher means more agressive, 0 disables)", Default = 0.8)]
        public double TrimmerIslandPct { get; set; }

        [Option(HelpText = "Use orbital to fill in outer edges of mesh", Default = false)]
        public bool UseOrbital { get; set; }

        [Option(HelpText = "Orbital resolution, interpolates for higher density", Default = 2)]
        public double OrbitalPointsPerMeter { get; set; }

        [Option(HelpText = "Mask resolution for clipping surface/orbital", Default = 5)]
        public double ShrinkwrapPointsPerMeter {get; set;}

        [Option(HelpText = "Extent of orbital, still subject to clip extent", Default = 64)]
        public int OrbitalRadius { get; set; }

        [Option(HelpText = "Only use orbital beyond this distance from surface", Default = 0.25)]
        public double FilterRadius { get; set; }

        [Option(HelpText = "Clever combine cell size (meters)", Default = CleverCombine.DEF_CELL_SIZE)]
        public double CleverCombineCellSize { get; set; }

        [Option(HelpText = "Poisson cell size (meters)", Default = 0.05f)]
        public double PoissonCellSize { get; set; }

        [Option(HelpText = "Deform oribital to fit surface", Default = false)]
        public bool AdjustOrbital { get; set; }
    }

    public class BuildGeometry : GeometryCommand
    {
        private const string OUT_DIR = "meshing/GeometryProducts";

        private BuildGeometryOptions options;

        private Observation[] onlyForObs;
        private RoverStereoEye stereoEye;
        private PoissonReconstruction.Options poissonOpts;

        private ConcurrentDictionary<string, Mesh> observationPointClouds = new ConcurrentDictionary<string, Mesh>();
        private Mesh pointCloud;
        private BoundingBox pointCloudBounds;
        private Mesh mesh;        
        private SceneMesh sceneMesh;

        //Intermediates
        private Mesh shrinkwrappedSurface;
        private Mesh surfaceMaskMesh;
        private Mesh orbitalMesh;

        private MeshOperator surfaceUVMeshOp;
        private List<Vector3> tiePoints;

        public BuildGeometry(BuildGeometryOptions options) : base(options)
        {
            this.options = options;
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

                RunPhase("build observation point clouds", BuildObservationPointClouds);
                RunPhase("merge point clouds", MergePointClouds);
                RunPhase("reconstruct mesh", ReconstructMesh);
                if(options.UseOrbital)
                {
                    RunPhase("create shrinkwrapped surface mesh", CreateShrinkwrappedSurfaceMesh);
                    RunPhase("create surface mask mesh", CreateSurfaceMaskMesh);
                    RunPhase("reconstruct surface to mask", ReconstructSurfaceToMask);
                    RunPhase("reconstruct orbital to mask", ReconstructOrbitalToMask);
                    RunPhase("merge orbital to surface", MergeOrbitalToSurface);
                }
                RunPhase("clip mesh", ClipMesh);
                RunPhase("clean mesh", CleanMesh);

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
            if (!base.ParseArgumentsAndLoadCaches(OUT_DIR))
            {
                return false; //help
            }

            onlyForObs = observationCache.ParseList(options.OnlyFacesForObs);

            stereoEye = RoverStereoPair.ParseEyeForGeometry(options.StereoEye, mission);

            if (options.ReconstructionMethod != MeshReconstructionMethod.FSSR &&
                options.ReconstructionMethod != MeshReconstructionMethod.Poisson)
            {
                throw new Exception("unsupported mesh reconstruction method: " + options.ReconstructionMethod);
            }

            poissonOpts = new PoissonReconstruction.Options
            {
                //extrapolates the edges of the mesh
                Boundary = PoissonReconstruction.BoundaryTypes.Neumann,
                
                // no features should be finer than this many meters as this is the finest the octree will dice
                MinOctreeCellWidthMeters = (float)(options.PoissonCellSize),
                
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

        //potentially mission specific: assumes z-down/up 
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
                    TargetFrame = meshFrame
                };

            var wedges = WedgeObservations.Collect(frameCache, observationCache, collectOpts);

            if (stereoEye != RoverStereoEye.Any)
            {
                wedges = WedgeObservations.FilterForEye(wedges, stereoEye).ToList(); 
            }

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

                    if (options.PreClipExtent > 0)
                    {
                        var bounds = pc.Bounds();
                        bounds = BoundsFromXYExtent(Vector3.Zero, options.PreClipExtent, bounds.Min.Z, bounds.Max.Z);
                        pc = Mesh.Clip(pc, bounds);
                        string msg = string.Format("pre-clipped point clound for observation {0} to {1}x{1} box " +
                                                   "in frame {2}, removed {3}/{4} points",
                                                   ptsName,options.PreClipExtent, options.PreClipExtent,
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
        }

        private void CreateShrinkwrappedSurfaceMesh()
        {
            var bounds = mesh.Bounds();
            Mesh grid = Shrinkwrap.BuildGrid(bounds, (int)(bounds.Size().X * options.ShrinkwrapPointsPerMeter),
                (int)(bounds.Size().Y * options.ShrinkwrapPointsPerMeter), VertexProjection.ProjectionAxis.Z);
            shrinkwrappedSurface = Shrinkwrap.Wrap(grid, mesh, Shrinkwrap.ShrinkwrapMode.Project,
                VertexProjection.ProjectionAxis.Z, Shrinkwrap.ProjectionMissResponse.Clip);
            shrinkwrappedSurface.Clean();
        }

        private void CreateSurfaceMaskMesh()
        {
            EdgeGraph edgeGraph = new EdgeGraph(shrinkwrappedSurface);
            List<Edge> perimeterEdges = edgeGraph.GetLargestPolygonalBoundary();

            //Ensure perimeter orientation is CCW
            EdgeGraph.EnsureCCW(perimeterEdges);

            //Create mask mesh verts
            surfaceMaskMesh = new Mesh();
            surfaceMaskMesh.Vertices = perimeterEdges.Select(e => new Vertex(e.Src.Vert.Position)).ToList();

            int id = 0;
            foreach (Edge e in perimeterEdges)
            {
                e.Src.ID = id;
                id++;
            }

            //Triangulate mask
            foreach (Edge e in TriangulatePolygon.Triangulate(perimeterEdges))
            {
                if (e.Left != null)
                {
                    surfaceMaskMesh.Faces.Add(new Face(e.Src.ID, e.Dst.ID, e.Left.ID));
                }
            }
        }

        private void ReconstructSurfaceToMask()
        {
            //Build uv mesh op for mask
            foreach (Vertex v in surfaceMaskMesh.Vertices)
            {
                v.UV = new Vector2(v.Position.X, v.Position.Y);
            }
            surfaceMaskMesh.HasUVs = true;
            MeshOperator maskUVMeshOp = new MeshOperator(surfaceMaskMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);

            foreach (Vertex vert in mesh.Vertices)
            {
                vert.UV = new Vector2(vert.Position.X, vert.Position.Y);
            }
            mesh.HasUVs = true;
            surfaceUVMeshOp = new MeshOperator(mesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);

            /*poissonOpts.TrimmerIslandPct = 0.0; //trim handled by mask
            poissonOpts.TrimmerLevel = 0.0;*/

            //TODO: Have Poisson return both clipped and non-clipped to avoid remeshing
            poissonOpts.TrimmerLevel = Math.Max(0, poissonOpts.TrimmerLevel - 2);
            mesh = PoissonReconstruction.Reconstruct(pointCloud, poissonOpts);
            mesh.RemoveFloaters();

            mesh.Faces = mesh.Faces.Where(face => {
                //Get rid of faces if all of their endpoints fall fall outside mask mesh
                //TODO: clip on any overlap and stitch meshes
                return (maskUVMeshOp.UVToBarycentric(new Vector2(mesh.Vertices[face.P0].Position.X, mesh.Vertices[face.P0].Position.Y)) != null || //change to && for stronger clip
                        maskUVMeshOp.UVToBarycentric(new Vector2(mesh.Vertices[face.P1].Position.X, mesh.Vertices[face.P1].Position.Y)) != null ||
                        maskUVMeshOp.UVToBarycentric(new Vector2(mesh.Vertices[face.P2].Position.X, mesh.Vertices[face.P2].Position.Y)) != null);
            }).ToList();

            mesh.RemoveUnreferencedVertices();
        }

        private void ReconstructOrbitalToMask()
        {
            string orbitalFrameName = OrbitalConfig.Instance.GetOrbitalFrameName();

            string demFilePath = Path.Combine(LocalPipelineConfig.Instance.StorageDir, project.Mission, OrbitalConfig.Instance.DEMRelPath);

            SparseImage dem = new SparseImage(demFilePath);
            dem.CameraModel = new OrthographicCameraModel(Matrix.Identity, dem.Width, dem.Height, mission.GetDemMetersPerPixel());

            Matrix demToBaseSiteDrive = frameCache.GetBestTransform(orbitalFrameName).Transform.Mean
                                        * Matrix.Invert(frameCache.GetBestTransform(meshFrame).Transform.Mean);

            Vector2 center;
            if (!mission.GetSiteDriveOriginPixelInDem(new SiteDrive(meshFrame), out center))
            {
                Matrix baseSiteDriveToDem = Matrix.Invert(demToBaseSiteDrive);
                Vector3 demOriginXYZ = Vector3.Transform(Vector3.Zero, baseSiteDriveToDem);
                center = new Vector2(dem.Width / 2 + demOriginXYZ.X, dem.Height / 2 - demOriginXYZ.Y);
                //throw new Exception("Places needed to build geometry with orbital");
            }

            int orbitalRadiusPixels = (int)(options.OrbitalRadius / mission.GetDemMetersPerPixel());

            Func<Vector3, Vector3> adjust;
            if (options.AdjustOrbital) {
                var adjustments = DemOperations.CreateAdjustments(dem, surfaceUVMeshOp, center, demToBaseSiteDrive, options.OrbitalRadius);
                foreach(Vertex v in surfaceMaskMesh.Vertices)
                {
                    Vector3 demPos = Vector3.Transform(v.Position, Matrix.Invert(demToBaseSiteDrive));
                    Vector2 demRC = dem.CameraModel.Project(demPos, out double throwaway);
                    Vector3? demSample = DemOperations.GetInterpolatedXYZ(dem, demRC.Y, demRC.X);
                    if(demSample.HasValue)
                    {
                        Vector3 demPoint = Vector3.Transform(demSample.Value, demToBaseSiteDrive);
                        adjustments.Add(new Vector3(v.Position.X, v.Position.Y, v.Position.Z - demPoint.Z));
                    }
                }
                Func<double, double> weight = d => 1 / Math.Pow(Math.E, d);
                Func<double, double> decay = d => 1;// 1 / (d / 2 + 1);

                adjust = new Func<Vector3, Vector3>(p =>
                {
                    Vector3 ret = p;
                    double distSq;
                    double zAdjust = 0;
                    double sum = 0;
                    double minD = Double.PositiveInfinity;
                    double w;
                    foreach (Vector3 adj in adjustments)
                    {
                        distSq = Math.Pow(adj.X - p.X, 2) + Math.Pow(adj.Y - p.Y, 2);
                        if(distSq < minD)
                        {
                            minD = distSq;
                        }
                        w = weight(distSq);
                        zAdjust += adj.Z * w;
                        sum += w;
                    }
                    ret.Z += (zAdjust / sum) * decay(minD); //weighted average
                    return ret;
                });
            } else
            {
                adjust = new Func<Vector3, Vector3>(p => p);
            }

            orbitalMesh = DemOperations.BuildOrbitalMeshAroundSurface(dem, surfaceMaskMesh, center, demToBaseSiteDrive,
                options.OrbitalRadius, options.FilterRadius, options.OrbitalPointsPerMeter, adjust);
        }

        private void MergeOrbitalToSurface()
        {
            int offset = orbitalMesh.Vertices.Count;
            Mesh merged = new Mesh();
            merged.Vertices = orbitalMesh.Vertices;
            merged.Vertices.AddRange(mesh.Vertices);
            merged.Faces = orbitalMesh.Faces;
            merged.Faces.AddRange(mesh.Faces.Select(f => new Face(f.P0 + offset, f.P1 + offset, f.P2 + offset)));

            Mesh tris = Delaunay.Triangulate(merged.Vertices, reverseWinding: true);
            merged.Faces.AddRange(tris.Faces.Where(f => !(f.P0 < offset && f.P1 < offset && f.P2 < offset ||
                                                      f.P0 >= offset && f.P1 >= offset && f.P2 >= offset)));
            //merged.AddSkirt(SkirtMode.Z, invert: true);
            mesh = merged;
        }

        private void ClipMesh()
        {
            pipeline.LogInfo("clipping mesh to source point cloud bounds");

            mesh = Mesh.Clip(mesh, pointCloudBounds);

            if (options.ClipExtent > 0)
            {
                pipeline.LogInfo("clipping mesh to {0} meter box around {1} frame origin in XY plane",
                                 options.ClipExtent, meshFrame);

                var bounds = BoundsFromXYExtent(Vector3.Zero, options.ClipExtent,
                                                pointCloudBounds.Min.Z, pointCloudBounds.Max.Z);
                mesh = Mesh.Clip(mesh, bounds);
            }

            if (mesh.Faces.Count == 0)
            {
                throw new Exception("clipped mesh is empty");
            }
        }

        private void CleanMesh()
        {
            mesh.Clean(); // normalizes the normals

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

            pipeline.LogInfo("cleaning mesh");
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
                SaveMesh(mesh, sceneMesh.Name);
            }

            var bounds = mesh.Bounds().Size();
            pipeline.LogInfo("scene bounds (meters): {0:F3}x{1:F3}x{2:F3}", bounds.X, bounds.Y, bounds.Z);
        }
    }
}
