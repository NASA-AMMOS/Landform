using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Linq;
using log4net;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using System.IO;

namespace OPS.Pipeline.TilingServer
{
    public class BuildTilingInputMessage : PipelineMessage
    {
        public BuildTilingInputMessage() { }
        public BuildTilingInputMessage(string projectName) : base(projectName) { }
    }

    class DisjointSetUBR
    {
        int[] parent;
        int[] rank; // height of tree

        public DisjointSetUBR(int len)
        {
            parent = new int[len + 1];
            rank = new int[len + 1];
            for(int i = 0; i < len; ++i)
            {
                MakeSet(i);
            }
        }

        public void MakeSet(int i)
        {
            parent[i] = i;
        }

        public int Find(int i)
        {
            while (i != parent[i]) // If i is not root of tree we set i to his parent until we reach root (parent of all parents)
            {
                i = parent[i];
            }
            return i;
        }

        // Path compression, O(log*n). For practical values of n, log* n <= 5
        public int FindPath(int i)
        {
            if (i != parent[i])
            {
                parent[i] = FindPath(parent[i]);
            }
            return parent[i];
        }

        public void Union(int i, int j)
        {
            int i_id = Find(i); // Find the root of first tree (set) and store it in i_id
            int j_id = Find(j); // // Find the root of second tree (set) and store it in j_id

            if (i_id == j_id) // If roots are equal (they have same parents) than they are in same tree (set)
            {
                return;
            }

            if (rank[i_id] > rank[j_id]) // If height of first tree is larger than second tree
            {
                parent[j_id] = i_id; // We hang second tree under first, parent of second tree is same as first tree
            }
            else
            {
                parent[i_id] = j_id; // We hang first tree under second, parent of first tree is same as second tree
                if (rank[i_id] == rank[j_id]) // If heights are same
                {
                    rank[j_id]++; // We hang first tree under second, that means height of tree is incremented by one
                }
            }
        }
    }

    /// <summary>
    /// create a large mesh from input data and uploads it as the tiling input
    /// </summary>
    public class BuildTilingInput : PipelineOperation
    {
        private readonly BuildTilingInputMessage message;

        public BuildTilingInput(PipelineCore pipeline, BuildTilingInputMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public static void RemoveFloaters(Mesh mesh, int minIslandVertexCount=300)
        {
            DisjointSetUBR disjointSet = new DisjointSetUBR(mesh.Vertices.Count);
            foreach (Face f in mesh.Faces)
            {
                disjointSet.Union(f.P0, f.P1);
                disjointSet.Union(f.P1, f.P2);
            }

            Dictionary<int, int> islandSizes = new Dictionary<int, int>();
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                int p = disjointSet.FindPath(i);
                if (islandSizes.ContainsKey(p))
                {
                    islandSizes[p]++;
                }
                else
                {
                    islandSizes[p] = 1;
                }
            }

            mesh.Faces = mesh.Faces.Where(f =>
            {
                return islandSizes[disjointSet.FindPath(f.P0)] >= minIslandVertexCount &&
                       islandSizes[disjointSet.FindPath(f.P1)] >= minIslandVertexCount &&
                       islandSizes[disjointSet.FindPath(f.P2)] >= minIslandVertexCount;
            }).ToList();

            mesh.RemoveUnreferencedVertices();
        }

        public int Process()
        {
            LogInfo("loading transform and observation caches");

            //load transforms, by filtering by allowed transform sources or allowing all
            var frameCache = new FrameCache(pipeline, projectName);
            frameCache.Preload(loadTransforms: true);
            
            //load observations
            var observationCache = new ObservationCache(pipeline, projectName);
            observationCache.Preload(obs => obs.UseForMeshing);

            LogInfo("building mesh");
            Mesh surfacedMesh = BuildMesh(pipeline, projectName, out BoundingBox pointBounds, frameCache,
                                          observationCache, "root", usePriors: false, noPriors: false,
                                           preclipBounds:new BoundingBox(), onlyForCameras: null, useCleverCombine: false, stereoEye: RoverStereoEye.Left,
                                          info: msg => LogInfo(msg), error: msg => { throw new Exception(msg); });
            if (surfacedMesh == null || surfacedMesh.Vertices.Count == 0)
            {
                LogError("point cloud failed to reconstruct");
                return 1;
            }

            //upload mesh
            string meshName = "FullMesh";
            string meshOutputUrl = pipeline.GetStorageUrl("input", projectName, meshName + ".ply");
            LogInfo("uploading mesh {0}", meshOutputUrl);
            TemporaryFile.GetAndDelete(".ply", tempFile =>
            {
                surfacedMesh.Save(tempFile);
                pipeline.SaveFile(tempFile, meshOutputUrl);
            });

            LogInfo("creating tiling input");

            //create a tiling input
            TilingProject tilingProject = TilingProject.Find(pipeline, projectName);
            TilingInput.Create(pipeline, meshName, tilingProject, meshOutputUrl, null, null);

            //indicate successs to the tiling server master
            pipeline.EnqueueToMaster(new BuildTilingInputMessage(projectName));

            return 0;
        }

        static public Mesh BuildMesh(PipelineCore pipeline, string projectName, out BoundingBox pointBounds,
                                     FrameCache frameCache, ObservationCache observationCache, string outputFrame,
                                     bool usePriors, bool noPriors, BoundingBox? preclipBounds = null, string onlyForCameras = null,
                                     bool useCleverCombine = false, RoverStereoEye stereoEye = RoverStereoEye.Left, int decimate = 1,
                                     int targetPointCloudResolution = 1024, double trimmerLevel = 0, double trimmerIslandPct = 0,
                                     Action<string> info = null,
                                     Action<string> verbose = null, Action<string> warn = null,
                                     Action<string> error = null)
        {
            pointBounds = new BoundingBox();

            info = info ?? (msg => pipeline.LogInfo(msg));
            verbose = verbose ?? (msg => pipeline.LogVerbose(msg));
            warn = warn ?? (msg => pipeline.LogWarn(msg));
            error = error ?? (msg => pipeline.LogError(msg));

            info("collecting wedges");

            //this is a bit tricky
            //sadly, we currently have "alignment" projects (type Project)
            //and "tiling" projects (type TilingProject)
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/567
            //
            //further, this method can be called from above as part of a tiling workflow
            //or from LocalBuildMeshes in which case the project name is an alignment project
            //
            //we need to resolve a mission to get a comparator
            //so for now see if the project name is recognized as an alignment project
            //and if not fall back to the legacy tiling behavior which is MSL
            var project = Project.Find(pipeline, projectName);
            var mission = MissionSpecific.GetInstance(project != null ? project.Mission : Mission.MSL.ToString());
            var masker = mission.GetMasker();

            var opts = new WedgeObservations.CollectOptions(null, null, onlyForCameras, mission)
                {
                    RequirePoints = true,
                    RequireNormals = true,
                    RequireTextures = false,
                    IncludeForAlignment = false,
                    IncludeForMeshing = true,
                    IncludeForTexturing = false,
                    RequirePriorTransform = usePriors,
                    RequireAdjustedTransform = noPriors,
                    TargetFrame = outputFrame
                };

            var observations = WedgeObservations.Collect(frameCache, observationCache, opts);

            if (stereoEye != RoverStereoEye.Any)
            {
                observations = WedgeObservations.FilterForEye(observations, stereoEye).ToList(); 
            }

            if (observations.Count == 0)
            {
                error("no observations were found to build a point cloud");
                return null;
            }

            var meshOpts = new WedgeObservations.MeshOptions() { Frame = outputFrame, ScaleNormalsByConfidence = true };

            if(preclipBounds.HasValue && preclipBounds.Value.MaxDimension() > 0)
            {
                info(string.Format("preclipping input point clouds"));
            }

            info("building wedge point clouds");
            var obsToMesh = new ConcurrentDictionary<string, Mesh>();
            int no = observations.Count;
            int np = 0, nc = 0, nf = 0;
            CoreLimitedParallel.ForEach(observations, obs => {
                    if (obs.SiteDrive == new SiteDrive(31, 1330)) //TODO delete
                        return;

                    Interlocked.Increment(ref np);

                    info(string.Format("building {0} wedge point clouds in parallel, completed {1}/{2}, {3} failed",
                                       np, nc, no, nf));

                    var mo = meshOpts.Clone();
                    mo.Decimate = WedgeObservations.AutoDecimate(obs.Points, decimate, targetPointCloudResolution);
                    if (mo.Decimate > 1 && mo.Decimate != decimate)
                    {
                        verbose(string.Format("auto decimating point cloud for observation {0} with blocksize {1}",
                                              obs.Name, mo.Decimate));
                    }
                    
                    var mesh = obs.BuildPointCloud(pipeline, frameCache, masker, mo);

                    if (mesh == null)
                    {
                        warn(string.Format("failed to build pointcloud for observation {0}", obs.Name));
                        Interlocked.Decrement(ref np);
                        Interlocked.Increment(ref nf);
                        return;
                    }

                    if (mesh.ContainsZeroLengthNormals())
                    {
                        warn(string.Format("pointcloud for observation {0} has zero length normals", obs.Name));
                        Interlocked.Decrement(ref np);
                        Interlocked.Increment(ref nf);
                        return;
                    }

                    if(preclipBounds.HasValue && preclipBounds.Value.MaxDimension() > 0)
                    {
                        var meshOp = new MeshOperator(mesh, false, true, false);
                        mesh = meshOp.Clip(preclipBounds.Value);

                        if (!mesh.HasVertices)
                        {
                            warn(string.Format("preclipping pointcloud for observation {0} has removed the pointcloud entirely", obs.Name));
                            Interlocked.Decrement(ref np);
                            Interlocked.Increment(ref nf);
                            return;
                        }
                    }

                    obsToMesh.AddOrUpdate(obs.Points.Name, _ => mesh, (_, __) => mesh);

                    Interlocked.Decrement(ref np);
                    Interlocked.Increment(ref nc);
                });

            Mesh aggregatePointCloud = new Mesh(hasNormals: true);
            if (useCleverCombine)
            {
                info("clever combine point cloud");
                var meshes = new List<Mesh>();
                var origins = new List<Vector3?>();
                foreach (var entry in obsToMesh)
                {
                    var pointsObs = observationCache.GetObservation(entry.Key);
                    var obsToOutput = frameCache.GetObservationTransform(pointsObs, outputFrame, usePriors, noPriors);
                    if (obsToOutput == null)
                    {
                        error(string.Format("failed to get transform to {0} for observation {1}", outputFrame, entry.Key));
                        continue;
                    }

                    CAHV cam = (CameraModel)JsonHelper.FromJson(pointsObs.CameraModel) as CAHV;
                    Vector3 cameraPosInOutput = Vector3.Transform(cam.C, obsToOutput.Mean);
                
                    //the reference point used to determine how good a point is for clever combine
                    //naive version is using distance from camera
                    origins.Add(cameraPosInOutput);
                    meshes.Add(entry.Value);
                }


                /*if (false)
                {
                    const int orbitalRadius = 40; //Add 80 x 80 meter orbital
                    const string orbitalFrameName = "Orbital";
                    const double orbitalPointsPerMeter = 10;
                    const double demNormalConfidence = 1/8.0;

                    string demFilePath = Path.Combine(LocalPipelineConfig.Instance.StorageDir, project.Mission, OrbitalConfig.Instance.DEMRelPath);

                    SparseImage dem = new SparseImage(demFilePath);
                    dem.CameraModel = new OrthographicCameraModel(Matrix.Identity, dem.Width, dem.Height, mission.GetDemMetersPerPixel());

                    Matrix demToBaseSiteDrive = frameCache.GetBestTransform(orbitalFrameName).Transform.Mean
                                                * Matrix.Invert(frameCache.GetBestTransform(opts.TargetFrame).Transform.Mean);

                    //Get subset of dem around sitedrive
                    Vector2 center = mission.GetSiteDriveOriginPixelInDem(observations[0].SiteDrive);
                    int pixelRadius = (int)(orbitalRadius / mission.GetDemMetersPerPixel());
                    int baseC = (int)Math.Max(center.X - pixelRadius, 0);
                    int baseR = (int)Math.Max(center.Y - pixelRadius, 0);
                    int pixelWidth = (int)Math.Min(center.X + pixelRadius, dem.Width) - baseC;
                    int pixelHeight = (int)Math.Min(center.Y + pixelRadius, dem.Height) - baseR;

                    if (!dem.HasMask)
                    {
                        dem.CreateMask();
                    }

                    Matrix baseSiteDriveToDem = Matrix.Invert(demToBaseSiteDrive);

                    Mesh demPoints = new Mesh();

                    for (int y = 0; y < 2 * pixelRadius * orbitalPointsPerMeter; y++)
                    {
                        for (int x = 0; x < 2 * pixelRadius * orbitalPointsPerMeter; x++)
                        {
                            double r = baseR + y / orbitalPointsPerMeter;
                            double c = baseC + x / orbitalPointsPerMeter;
                            var pos = DemOperations.GetInterpolatedXYZ(dem, r, c);
                            if (pos.HasValue)
                            {
                                var transformedPos = Vector3.Transform(pos.Value, demToBaseSiteDrive);
                                Vertex v = new Vertex();
                                v.Position = transformedPos;
                                v.Normal = DemOperations.GetInterpolatedNormal(dem, r, c) ?? new Vector3(0, 0, -1);
                                v.Normal = Vector3.Normalize(Vector3.TransformNormal(v.Normal, demToBaseSiteDrive));
                                v.Normal *= demNormalConfidence;
                                demPoints.Vertices.Add(v);
                            }
                        }
                    }

                    origins.Add(null); //Use default orbital distance to camera
                    meshes.Add(demPoints);
                }*/



                int nv = meshes.Aggregate(0, (sum, mesh) => sum + mesh.Vertices.Count);
                pipeline.LogInfo("combining {0} point clouds with clever combine, total {1} points",
                                 meshes.Count, Fmt.KMG(nv));
                aggregatePointCloud = CleverCombinePointClouds.Combine(origins.ToArray(), meshes.ToArray(), pipeline);
            }
            else
            {
                info("merging point clouds");
                var meshes = obsToMesh.Values.ToArray();
                int nv = meshes.Aggregate(0, (sum, mesh) => sum + mesh.Vertices.Count);
                info(string.Format("merging {0} point clouds, total {1} points", meshes.Length, Fmt.KMG(nv)));
                aggregatePointCloud.MergeWith(meshes, normalize: false, removeDuplicateVerts: false);
            }

            //significant memory usage
            obsToMesh.Clear();

            // build the large mesh from the aggregate point cloud using poisson reconstruction
            if (aggregatePointCloud.Vertices.Count == 0)
            {
                error("aggregate point cloud contains no points");
                return null;
            }

            pointBounds = aggregatePointCloud.Bounds();

            const double surfaceTrimmerLevel = 8.0;
            const double surfaceTrimmerIslandPercent = 0.8;

            info(string.Format("Poisson reconstructing mesh from {0} points",
                               Fmt.KMG(aggregatePointCloud.Vertices.Count)));
            PoissonReconstruction.Options poissonOpts = new PoissonReconstruction.Options
            {
                //extrapolates the edges of the mesh
                Boundary = PoissonReconstruction.BoundaryTypes.Free,

                // no features should be finer than this many meters as this is the finest the octree will dice
                MinOctreeCellWidthMeters = 0.05f,

                // a value on the upper end of the suggested range in the docs
                // meaning we think our data in noisy, so wait for this many samples in a cell
                MinOctreeSamplesPerCell = 15,

                // attempts to allow higher order surfaces than the defaults
                BSplineDegree = 2,

                // indicates the normal magnitudes are not uniformly unit scaled
                // to indicate confidence in the position attached to it
                UseNormalsForConfidence = true,

                // remove low density points
                TrimmerLevel = surfaceTrimmerLevel,

                // remove disconnected islands of pts
                TrimmerIslandPct = surfaceTrimmerIslandPercent
            };

            var bestClippedMesh = PoissonReconstruction.Reconstruct(aggregatePointCloud, poissonOpts);
            //const double eps = 0.01;
            //bestClippedMesh.MergeNearbyVertices(eps);
            //bestClippedMesh.Clean();
            //RemoveFloaters(bestClippedMesh);
            //bestClippedMesh.Clean();

            const double resolution = 0.5;
            var bounds = bestClippedMesh.Bounds();
            Mesh grid = Shrinkwrap.BuildGrid(bounds, (int)(bounds.Size().X * resolution), (int)(bounds.Size().Y * resolution), VertexProjection.ProjectionAxis.Z);
            bestClippedMesh = Shrinkwrap.Wrap(grid, bestClippedMesh, Shrinkwrap.ShrinkwrapMode.Project, VertexProjection.ProjectionAxis.Z, Shrinkwrap.ProjectionMissResponse.Clip);
            bestClippedMesh.Clean();
            bestClippedMesh.Save("C:\\Users\\conductor\\Documents\\landform-storage\\local\\meshing\\GeometryProducts\\0311472Frame\\best\\windjana\\surface_clipped.ply");

            /*var maskMesh = Delaunay.Triangulate(bestClippedMesh.Vertices);
            EdgeGraph edgeGraph = new EdgeGraph(maskMesh);
            var perimeterEdges = edgeGraph.GetPerimeterEdges();*/

            EdgeGraph edgeGraph = new EdgeGraph(bestClippedMesh);
            var edges = edgeGraph.GetPerimeterEdges();
            List<Edge> currentGroup;
            List<Edge> perimeterEdges = null;
            double maxArea = 0.0;
            foreach(Edge firstEdge in edges)
            {
                if(!firstEdge.IsPerimeterEdge)
                {
                    continue;
                }
                currentGroup = new List<Edge> { firstEdge };
                List<Edge> splits = new List<Edge>();
                List<int> splitIdxs = new List<int>();
                firstEdge.IsPerimeterEdge = false;
                Edge current = firstEdge;
                bool closed = false;
                while (!closed)
                {
                    bool foundNextEdge = false;
                    foreach (Edge other in current.Dst.AdjacentEdges)
                    {
                        if (other.Dst != current.Src && other.Left != null && other.IsPerimeterEdge)
                        {
                            //Found next perimeter edge...
                            if (foundNextEdge)
                            {
                                splits.Add(other);
                                other.IsPerimeterEdge = false;
                                splitIdxs.Add(currentGroup.Count - 1);
                            }
                            else
                            {
                                foundNextEdge = true;
                                currentGroup.Add(other);
                                other.IsPerimeterEdge = false;
                                current = other;
                            }                            
                        }   
                    }
                    if (!foundNextEdge)
                    {
                        //Backtrack to last split
                        if(splits.Count > 0)
                        {
                            current = splits.Last();
                            int idx = splitIdxs.Last();
                            currentGroup = currentGroup.Take(idx).ToList();
                            currentGroup.Add(current);

                            splits.RemoveAt(splits.Count - 1);
                            splitIdxs.RemoveAt(splitIdxs.Count - 1);
                        } else
                        {
                            currentGroup = null;
                            break;
                        }
                    }
                    closed = (current.Dst == firstEdge.Src);
                }
                if (currentGroup != null)
                {
                    var size = BoundingBox.CreateFromPoints(currentGroup.Select(e => e.Src.Vert.Position)).Size();
                    var area = size.X * size.Y;
                    if (area > maxArea)
                    {
                        maxArea = area;
                        perimeterEdges = currentGroup;
                    }
                }
            }

            //Clip subcycles
            while (EdgeGraph.ClipSubcycle(perimeterEdges)) { }

            //Get perimeter orientation
            VertexNode right = perimeterEdges[0].Src;
            foreach(Edge e in perimeterEdges)
            {
                if(e.Src.Vert.Position.X > right.Vert.Position.X)
                {
                    right = e.Src;
                }
            }
           
            Edge edgeIn = perimeterEdges.Where(e => e.Dst == right).First();
            Edge edgeOut = perimeterEdges.Where(e => e.Src == right).First();
            bool ccw = Edge.IsLeftTurn(edgeIn, edgeOut, 0);
            if (!ccw)
            {
                pipeline.Logger.Info("Reversing perimeter winding.");
                perimeterEdges.ForEach(e =>
                {
                    var tmp = e.Src;
                    e.Src = e.Dst;
                    e.Dst = tmp;
                });
                perimeterEdges.Reverse();
                ccw = true;
            }

            Mesh maskMesh = new Mesh();
            maskMesh.Vertices = perimeterEdges.Select(e => new Vertex(e.Src.Vert.Position)).ToList();

            int id = 0;
            foreach(Edge e in perimeterEdges)
            {
                e.Src.ID = id;
                id++;
            }

            /*bool progress = true;
            while (progress)
            {
                progress = false;
                for (int i = perimeterEdges.Count - 2; i > 0; i--)
                {
                    Edge e1 = perimeterEdges[i];
                    Edge e2 = perimeterEdges[i + 1];
                    if (Edge.IsColinear(e1, e2))
                    {
                        perimeterEdges.RemoveAt(i + 1);
                        perimeterEdges.RemoveAt(i);
                        perimeterEdges.Insert(i, new Edge(e1.Src, e2.Dst, null));
                        progress = true;
                    }
                }
            }
            if(Edge.IsColinear(perimeterEdges[perimeterEdges.Count - 1], perimeterEdges[0]))
            {
                Edge e = new Edge(perimeterEdges[perimeterEdges.Count - 1].Src, perimeterEdges[0].Dst, null);
                perimeterEdges.RemoveAt(perimeterEdges.Count - 1);
                perimeterEdges.RemoveAt(0);
                perimeterEdges.Insert(0, e);
            }*/

            foreach (Edge e in TriangulatePolygon.Triangulate(perimeterEdges, ccw))
            {
                if(e.Left != null)
                {
                    maskMesh.Faces.Add(new Face(e.Src.ID, e.Dst.ID, e.Left.ID));
                }
            }

            maskMesh.Save("C:\\Users\\conductor\\Documents\\landform-storage\\local\\meshing\\GeometryProducts\\0311472Frame\\best\\windjana\\mask.ply");

            foreach (Vertex v in maskMesh.Vertices)
            {
                v.UV = new Vector2(v.Position.X, v.Position.Y);
            }
            maskMesh.HasUVs = true;
            MeshOperator uvMeshOp = new MeshOperator(maskMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);

            poissonOpts.TrimmerIslandPct = 0.8;
            poissonOpts.TrimmerLevel = 6.0;

            var bestMesh = PoissonReconstruction.Reconstruct(aggregatePointCloud, poissonOpts);

            if (bestClippedMesh == null )
            {
                warn("reconstruction failed");
                return null;
            }
            RemoveFloaters(bestMesh);

            foreach (Vertex vert in bestMesh.Vertices)
            {
                vert.UV = new Vector2(vert.Position.X, vert.Position.Y);
            }
            bestMesh.HasUVs = true;
            bestMesh.Faces = bestMesh.Faces.Where(face =>         
                uvMeshOp.UVToBarycentric(new Vector2(bestMesh.Vertices[face.P0].Position.X, bestMesh.Vertices[face.P0].Position.Y)) != null &&
                uvMeshOp.UVToBarycentric(new Vector2(bestMesh.Vertices[face.P1].Position.X, bestMesh.Vertices[face.P1].Position.Y)) != null &&
                uvMeshOp.UVToBarycentric(new Vector2(bestMesh.Vertices[face.P2].Position.X, bestMesh.Vertices[face.P2].Position.Y)) != null
            ).ToList();

            bestMesh.RemoveUnreferencedVertices();

            bestMesh.Save("C:\\Users\\conductor\\Documents\\landform-storage\\local\\meshing\\GeometryProducts\\0311472Frame\\best\\windjana\\surface.ply");

            //Filter points that don't hit the trimmed surface mesh

            //poissonOpts.TrimmerIslandPct = 0.0;
            //poissonOpts.TrimmerLevel = 7.0;
            //Mesh tempSurfaceMesh = PoissonReconstruction.Reconstruct(aggregatePointCloud, poissonOpts);
            //RemoveFloaters(tempSurfaceMesh);
            //foreach (Vertex vert in tempSurfaceMesh.Vertices)
            //{
            //    vert.UV = new Vector2(vert.Position.X, vert.Position.Y);
            //}
            //tempSurfaceMesh.HasUVs = true;
            //MeshOperator uvMeshOp = new MeshOperator(tempSurfaceMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);
            //aggregatePointCloud.Vertices = aggregatePointCloud.Vertices.Where(vert =>
            //{
            //    return uvMeshOp.UVToBarycentric(new Vector2(vert.Position.X, vert.Position.Y)) != null;
            //}).ToList();

            //Add Orbital
            ///////////////////////////////////////////////////////////////////////////////////////////////////
            //if (true)
            //{
                const int orbitalRadius = 40; //Add 80 x 80 meter orbital
                const double filterRadius = 2.0; //Remove orbital points within 10cm of surface data
                const double confidenceDecayRadius = 0.0;
                const string orbitalFrameName = "Orbital";
                const double orbitalPointsPerMeter = 2;
                const double demNormalConfidence = 1.0;

                string demFilePath = Path.Combine(LocalPipelineConfig.Instance.StorageDir, project.Mission, OrbitalConfig.Instance.DEMRelPath);

                SparseImage dem = new SparseImage(demFilePath);
                dem.CameraModel = new OrthographicCameraModel(Matrix.Identity, dem.Width, dem.Height, mission.GetDemMetersPerPixel());

                info("Target frame = " + opts.TargetFrame);

                Matrix demToBaseSiteDrive = frameCache.GetBestTransform(orbitalFrameName).Transform.Mean
                                            * Matrix.Invert(frameCache.GetBestTransform(opts.TargetFrame).Transform.Mean);

                //Get subset of dem around sitedrive
                Vector2 center = mission.GetSiteDriveOriginPixelInDem(observations[0].SiteDrive);
                int pixelRadius = (int)(orbitalRadius / mission.GetDemMetersPerPixel());
                int baseC = (int)Math.Max(center.X - pixelRadius, 0);
                int baseR = (int)Math.Max(center.Y - pixelRadius, 0);
                int pixelWidth = (int)Math.Min(center.X + pixelRadius, dem.Width) - baseC;
                int pixelHeight = (int)Math.Min(center.Y + pixelRadius, dem.Height) - baseR;

                if (!dem.HasMask)
                {
                    dem.CreateMask();
                }

                Matrix baseSiteDriveToDem = Matrix.Invert(demToBaseSiteDrive);

                double[,] minDistsSq = new double[pixelHeight, pixelWidth];
                for(int i = 0; i < pixelWidth; ++i)
                {
                    for(int j = 0; j < pixelHeight; ++j)
                    {
                        minDistsSq[i, j] = Double.PositiveInfinity;
                    }
                }
                
                //double influenceRadius = filterRadius + confidenceDecayRadius;
                //double filterRadiusSq = filterRadius * filterRadius;
                //foreach (var p in aggregatePointCloud.Vertices)
                //{
                //    Vector3 testPoint = Vector3.Transform(p.Position, baseSiteDriveToDem);
                //    Vector2 rc = dem.CameraModel.Project(testPoint, out double throwAwayRange);              
                //    for (int r = (int)Math.Ceiling(Math.Max(rc.Y - influenceRadius, baseR));
                //        r <= (int)Math.Floor(Math.Min(rc.Y + influenceRadius, baseR + pixelHeight - 1)); ++r)
                //    {
                //        for (int c = (int)Math.Ceiling(Math.Max(rc.X - influenceRadius, baseC));
                //            c <= (int)Math.Floor(Math.Min(rc.X + influenceRadius, baseC + pixelWidth - 1)); ++c)
                //        {
                //            double distSq = (rc.Y - r) * (rc.Y - r) + (rc.X - c) * (rc.X - c);
                //            if (distSq < filterRadiusSq)
                //            {
                //                dem.SetMaskValue(r, c, true);
                //            }
                //            minDistsSq[r - baseR, c - baseC] = Math.Min(minDistsSq[r - baseR, c - baseC], distSq);
                //        }
                //    }
                //}

                Mesh demMesh = new Mesh();

                //double confidenceDecayRadiusSq = influenceRadius * influenceRadius;

                for (int y = 0; y < 2 * pixelRadius * orbitalPointsPerMeter; y++)
                {
                    for (int x = 0; x < 2 * pixelRadius * orbitalPointsPerMeter; x++)
                    {
                        double r = baseR + y / orbitalPointsPerMeter;
                        double c = baseC + x / orbitalPointsPerMeter;
                        var pos = DemOperations.GetInterpolatedXYZ(dem, r, c);
                        if (pos.HasValue)
                        {
                            var transformedPos = Vector3.Transform(pos.Value, demToBaseSiteDrive);
                            if (uvMeshOp.UVToBarycentric(new Vector2(transformedPos.X, transformedPos.Y)) == null)
                            {
                                Vertex v = new Vertex();
                                v.Position = transformedPos;
                                v.Normal = DemOperations.GetInterpolatedNormal(dem, r, c) ?? new Vector3(0, 0, -1);
                                v.Normal = Vector3.Normalize(Vector3.TransformNormal(v.Normal, demToBaseSiteDrive));
                                double distSq = minDistsSq[(int)r - baseR, (int)c - baseC];
                                //if(distSq == -1 || distSq > confidenceDecayRadiusSq)
                                //{
                                v.Normal *= demNormalConfidence;
                                //} else
                                //{
                                //    v.Normal *= demNormalConfidence * (Math.Sqrt(distSq) - filterRadius) / confidenceDecayRadius;
                                //}                            
                                //aggregatePointCloud.Vertices.Add(v);
                                demMesh.Vertices.Add(v);
                            }
                        }
                    }
                }

                var perimeterVerts = edgeGraph.GetPerimeterNodes().Select(n => new Vertex(n.Vert.Position)); //TODO: subsample edges
                demMesh.Vertices.AddRange(perimeterVerts);

                //aggregatePointCloud.Save("d:/dems/DEBUG1.obj");

                demMesh = Delaunay.Triangulate(demMesh.Vertices, reverseWinding:true);
                demMesh.Faces = demMesh.Faces.Where(face =>
                {
                    Triangle tri = new Triangle(demMesh.Vertices[face.P0], demMesh.Vertices[face.P1], demMesh.Vertices[face.P2]);
                    Vector3 c = tri.Barycenter();
                    return uvMeshOp.UVToBarycentric(new Vector2(c.X, c.Y)) == null;
                }).ToList();

                demMesh.RemoveUnreferencedVertices();

                demMesh.Save("C:\\Users\\conductor\\Documents\\landform-storage\\local\\meshing\\GeometryProducts\\0311472Frame\\best\\windjana\\orbital.ply");
            //}

            ///////////////////////////////////////////////////////////////////////////////////////////////////

            //Remesh with orbital / lower trim level
            //poissonOpts.TrimmerLevel = trimmerLevel;
            //poissonOpts.TrimmerIslandPct = trimmerIslandPct;

            //var ret = PoissonReconstruction.Reconstruct(aggregatePointCloud, poissonOpts);

            //if (ret != null)
            //{
            //    info(string.Format("Poisson reconstructed mesh with {0} faces", Fmt.KMG(ret.Faces.Count)));
            //}
            //else
            //{
            //    warn("reconstruction failed");
            //}

            //RemoveFloaters(ret);

            ////return ret;

            ////Filter points that don't hit the trimmed surface mesh
            //foreach (Vertex vert in ret.Vertices)
            //{
            //    vert.UV = new Vector2(vert.Position.X, vert.Position.Y);
            //}
            //ret.HasUVs = true;
            //ret.Faces = ret.Faces.Where(face =>
            //{
            //    return uvMeshOp.UVToBarycentric(new Vector2(ret.Vertices[face.P0].Position.X, ret.Vertices[face.P0].Position.Y)) == null &&
            //    uvMeshOp.UVToBarycentric(new Vector2(ret.Vertices[face.P1].Position.X, ret.Vertices[face.P1].Position.Y)) == null &&
            //    uvMeshOp.UVToBarycentric(new Vector2(ret.Vertices[face.P2].Position.X, ret.Vertices[face.P2].Position.Y)) == null;
            //}).ToList();

            //ret.RemoveUnreferencedVertices();
            //return Mesh.Merge(ret, tempSurfaceMesh);
            
            int offset = demMesh.Vertices.Count;
            Mesh merged = new Mesh();
            merged.Vertices = demMesh.Vertices;
            merged.Vertices.AddRange(bestMesh.Vertices);
            merged.Faces = demMesh.Faces;
            merged.Faces.AddRange(bestMesh.Faces.Select(f => new Face(f.P0 + offset, f.P1 + offset, f.P2 + offset)));

            //Mesh tris = Delaunay.Triangulate(merged.Vertices, reverseWinding:true);
            //merged.Faces.AddRange(tris.Faces.Where(f => !(f.P0 < offset && f.P1 < offset && f.P2 < offset ||
            //                                          f.P0 >= offset && f.P1 >= offset && f.P2 >= offset)));

            return merged;
           
        }
    }
}
