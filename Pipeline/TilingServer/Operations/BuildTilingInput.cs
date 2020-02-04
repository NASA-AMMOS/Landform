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
                                     bool useOrbital = false, 
                                     double orbitalPointsPerMeter = 2, //Orbital resolution, can interpolate
                                     double shrinkwrapPointsPerMeter = 5, //Mask resolution
                                     int orbitalRadius = 64, //Include 2R x 2R orbital grid centered at site drive
                                     double filterRadius = 0.25, //Remove orbital points within X meters of surface data
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
                if (useOrbital)
                {
                    var xform = frameCache.GetBestTransform(obs.SiteDrive.ToString());
                    if (xform == null || xform.Source > TransformSource.LandformOrbital)
                    {
                        return; //If using orbital, only allow height aligned sitedrives
                    }
                }

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

            info(string.Format("Poisson reconstructing mesh from {0} points",
                               Fmt.KMG(aggregatePointCloud.Vertices.Count)));
            PoissonReconstruction.Options poissonOpts = new PoissonReconstruction.Options
            {
                //extrapolates the edges of the mesh
                Boundary = PoissonReconstruction.BoundaryTypes.Dirichlet,

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
                TrimmerLevel = trimmerLevel,

                // remove disconnected islands of pts
                TrimmerIslandPct = trimmerIslandPct
            };

            var bestClippedMesh = PoissonReconstruction.Reconstruct(aggregatePointCloud, poissonOpts);

            if (bestClippedMesh == null) 
            {
                warn("reconstruction failed");
                return null;
            } else
            {
                info(string.Format("Poisson reconstructed mesh with {0} faces", Fmt.KMG(bestClippedMesh.Faces.Count)));
            }

            if (!useOrbital)
            {
                return bestClippedMesh;
            }

            var bounds = bestClippedMesh.Bounds();
            Mesh grid = Shrinkwrap.BuildGrid(bounds, (int)(bounds.Size().X * shrinkwrapPointsPerMeter), 
                (int)(bounds.Size().Y * shrinkwrapPointsPerMeter), VertexProjection.ProjectionAxis.Z);
            bestClippedMesh = Shrinkwrap.Wrap(grid, bestClippedMesh, Shrinkwrap.ShrinkwrapMode.Project, 
                VertexProjection.ProjectionAxis.Z, Shrinkwrap.ProjectionMissResponse.Clip);
            bestClippedMesh.Clean();

            pipeline.LogInfo("Shrinkwrapped surface mesh.");

            EdgeGraph edgeGraph = new EdgeGraph(bestClippedMesh);
            var edges = edgeGraph.GetPerimeterEdges();
            List<Edge> currentGroup;
            HashSet<Edge> usedEdges;
            List<Edge> perimeterEdges = null;
            double maxArea = 0.0;
            foreach(Edge firstEdge in edges)
            {
                if(!firstEdge.IsPerimeterEdge)
                {
                    continue;
                }
                currentGroup = new List<Edge> { firstEdge };
                usedEdges = new HashSet<Edge>() { firstEdge };
                List<Edge> splits = new List<Edge>();
                List<int> splitIdxs = new List<int>();
                Edge current = firstEdge;
                bool closed = false;
                //search for a closed loop of perimeter edges
                while (!closed)
                {
                    bool foundNextEdge = false;
                    foreach (Edge other in current.Dst.AdjacentEdges)
                    {
                        if (other.Dst != current.Src && other.Left != null && other.IsPerimeterEdge && !usedEdges.Contains(other))
                        {
                            if (foundNextEdge)
                            {
                                //if already found a next edge, save this as an option to backtrack to
                                splits.Add(other);
                                splitIdxs.Add(currentGroup.Count - 1);
                            }
                            else
                            {
                                //add the next edge to the group
                                foundNextEdge = true;
                                currentGroup.Add(other);
                                usedEdges.Add(other);
                                current = other;
                            }                            
                        }   
                    }
                    if (!foundNextEdge)
                    {
                        //Backtrack to last split
                        current = null;
                        while(splits.Count > 0)
                        {
                            current = splits.Last();
                            if(usedEdges.Contains(current))
                            {
                                current = null;
                                splits.RemoveAt(splits.Count - 1);
                                splitIdxs.RemoveAt(splitIdxs.Count - 1);
                                continue;
                            }
                            int idx = splitIdxs.Last();
                            currentGroup = currentGroup.Take(idx).ToList();
                            currentGroup.Add(current);                          
                        }
                        if(current == null)
                        {
                            //Failed to find a closed loop
                            currentGroup = null;
                            break;
                        }
                    }
                    closed = (current.Dst == firstEdge.Src);
                }
                if (currentGroup != null)
                {
                    foreach(Edge e in currentGroup)
                    {
                        e.IsPerimeterEdge = false; //Flag as used
                    }
                    //Keep the largest (area) group of edges
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

            pipeline.LogInfo("Computed shrinkwrapped mesh perimeter.");

            //Ensure perimeter orientation is CCW
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
                perimeterEdges.ForEach(e =>
                {
                    var tmp = e.Src;
                    e.Src = e.Dst;
                    e.Dst = tmp;
                });
                perimeterEdges.Reverse();
                ccw = true;
                pipeline.Logger.Info("Reversed perimeter winding to be CCW.");
            }

            //Create mask mesh verts
            Mesh maskMesh = new Mesh();
            maskMesh.Vertices = perimeterEdges.Select(e => new Vertex(e.Src.Vert.Position)).ToList();

            int id = 0;
            foreach (Edge e in perimeterEdges)
            {
                e.Src.ID = id;
                id++;
            }

            //Triangulate mask
            foreach (Edge e in TriangulatePolygon.Triangulate(perimeterEdges, ccw))
            {
                if(e.Left != null)
                {
                    maskMesh.Faces.Add(new Face(e.Src.ID, e.Dst.ID, e.Left.ID));
                }
            }

            pipeline.LogInfo("Created shrinkwrapped surface mesh mask.");

            //Build uv mesh op for mask
            foreach (Vertex v in maskMesh.Vertices)
            {
                v.UV = new Vector2(v.Position.X, v.Position.Y);
            }
            maskMesh.HasUVs = true;
            MeshOperator maskUVMeshOp = new MeshOperator(maskMesh, buildFaceTree: false, buildVertexTree: false, buildUVFaceTree: true);

            /*poissonOpts.TrimmerIslandPct = 0.0; //trim handled by mask
            poissonOpts.TrimmerLevel = 0.0;*/

            //TODO: Have Poisson retlurn both clipped and non-clipped to avoid remeshing
            poissonOpts.TrimmerLevel = Math.Max(0, poissonOpts.TrimmerLevel - 2);
            var bestMesh = PoissonReconstruction.Reconstruct(aggregatePointCloud, poissonOpts);
            bestMesh.RemoveFloaters();

            foreach (Vertex vert in bestMesh.Vertices)
            {
                vert.UV = new Vector2(vert.Position.X, vert.Position.Y);
            }
            bestMesh.HasUVs = true;

            bestMesh.Faces = bestMesh.Faces.Where(face => {
                //Get rid of faces if all of their endpoints fall fall outside mask mesh
                //TODO: clip on any overlap and stitch meshes
                return (maskUVMeshOp.UVToBarycentric(new Vector2(bestMesh.Vertices[face.P0].Position.X, bestMesh.Vertices[face.P0].Position.Y)) != null || //change to && for stronger clip
                        maskUVMeshOp.UVToBarycentric(new Vector2(bestMesh.Vertices[face.P1].Position.X, bestMesh.Vertices[face.P1].Position.Y)) != null ||
                        maskUVMeshOp.UVToBarycentric(new Vector2(bestMesh.Vertices[face.P2].Position.X, bestMesh.Vertices[face.P2].Position.Y)) != null);
            }).ToList();

            bestMesh.RemoveUnreferencedVertices();

            pipeline.LogInfo("Created trimmed surface mesh without holes.");

            const string orbitalFrameName = "Orbital";

            string demFilePath = Path.Combine(LocalPipelineConfig.Instance.StorageDir, project.Mission, OrbitalConfig.Instance.DEMRelPath);

            SparseImage dem = new SparseImage(demFilePath);
            dem.CameraModel = new OrthographicCameraModel(Matrix.Identity, dem.Width, dem.Height, mission.GetDemMetersPerPixel());

            Matrix demToBaseSiteDrive = frameCache.GetBestTransform(orbitalFrameName).Transform.Mean
                                        * Matrix.Invert(frameCache.GetBestTransform(opts.TargetFrame).Transform.Mean);

            //Get subset of dem around sitedrive
            Vector2 center;
            if(!mission.GetSiteDriveOriginPixelInDem(observations[0].SiteDrive, out center))
            {
                throw new Exception("Places needed to build geometry with orbital");
            }
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
                
            double filterRadiusSq = filterRadius * filterRadius;
            foreach (var p in maskMesh.Vertices)
            {
                Vector3 testPoint = Vector3.Transform(p.Position, baseSiteDriveToDem);
                Vector2 rc = dem.CameraModel.Project(testPoint, out double throwAwayRange);
                for (int r = (int)Math.Ceiling(Math.Max(rc.Y - filterRadius, baseR));
                    r <= (int)Math.Floor(Math.Min(rc.Y + filterRadius, baseR + pixelHeight - 1)); ++r)
                {
                    for (int c = (int)Math.Ceiling(Math.Max(rc.X - filterRadius, baseC));
                        c <= (int)Math.Floor(Math.Min(rc.X + filterRadius, baseC + pixelWidth - 1)); ++c)
                    {
                        double distSq = (rc.Y - r) * (rc.Y - r) + (rc.X - c) * (rc.X - c);
                        if (distSq < filterRadiusSq)
                        {
                            dem.SetMaskValue(r, c, true);
                        }
                    }
                }
            }

            Mesh demMesh = new Mesh();

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
                        if (maskUVMeshOp.UVToBarycentric(new Vector2(transformedPos.X, transformedPos.Y)) == null)
                        {
                            Vertex v = new Vertex();
                            v.Position = transformedPos;
                            v.Normal = DemOperations.GetInterpolatedNormal(dem, r, c) ?? new Vector3(0, 0, -1);
                            v.Normal = Vector3.Normalize(Vector3.TransformNormal(v.Normal, demToBaseSiteDrive));
                            demMesh.Vertices.Add(v);
                        }
                    }
                }
            }
            demMesh.Vertices.AddRange(maskMesh.Vertices);

            demMesh = Delaunay.Triangulate(demMesh.Vertices, reverseWinding:true);
            demMesh.Faces = demMesh.Faces.Where(face =>
            {
                Triangle tri = new Triangle(demMesh.Vertices[face.P0], demMesh.Vertices[face.P1], demMesh.Vertices[face.P2]);
                Vector3 c = tri.Barycenter();
                return maskUVMeshOp.UVToBarycentric(new Vector2(c.X, c.Y)) == null;
            }).ToList();

            demMesh.RemoveUnreferencedVertices();

            pipeline.LogInfo("Created orbital mesh around sitedrive");

            int offset = demMesh.Vertices.Count;
            Mesh merged = new Mesh();
            merged.Vertices = demMesh.Vertices;
            merged.Vertices.AddRange(bestMesh.Vertices);
            merged.Faces = demMesh.Faces;
            merged.Faces.AddRange(bestMesh.Faces.Select(f => new Face(f.P0 + offset, f.P1 + offset, f.P2 + offset)));

            Mesh tris = Delaunay.Triangulate(merged.Vertices, reverseWinding:true);
            merged.Faces.AddRange(tris.Faces.Where(f => !(f.P0 < offset && f.P1 < offset && f.P2 < offset ||
                                                      f.P0 >= offset && f.P1 >= offset && f.P2 >= offset)));

            merged.AddSkirt(SkirtMode.Z, invert: true);

            pipeline.LogInfo("Merged surface and orbital mesh.");

            return merged;
           
        }
    }
}
