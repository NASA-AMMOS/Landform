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

//TODO: refactor so that local codepath does not have cloud dependencies
//https://github.jpl.nasa.gov/OnSight/Landform/issues/596
using QueueMessage = OPS.Cloud.QueueMessage;

namespace OPS.Pipeline.TilingServer
{
    public class BuildTilingInputMessage : QueueMessage
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
            //temporarily suppress mastcam point cloud data until validated
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/261
            Mesh surfacedMesh = BuildMesh(pipeline, projectName, out BoundingBox pointBounds, frameCache,
                                          observationCache, "root", usePriors: false, noPriors: false,
                                          onlyForCameras: null, useCleverCombine: false, 
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
                                     bool usePriors, bool noPriors, string onlyForCameras = null,
                                     bool useCleverCombine = false, int decimate = 1,
                                     int targetPointCloudResolution = 1024, Action<string> info = null,
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
            if (observations.Count == 0)
            {
                error("no observations were found to build a point cloud");
                return null;
            }

            var meshOpts = new WedgeObservations.MeshOptions() { Frame = outputFrame, ScaleNormalsByConfidence = true };

            info("building wedge point clouds");
            var obsToMesh = new ConcurrentDictionary<string, Mesh>();
            int no = observations.Count;
            int np = 0, nc = 0, nf = 0;
            CoreLimitedParallel.ForEach(observations, obs => {

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
                        Interlocked.Increment(ref nf);
                        return;
                    }
                    
                    if (mesh.ContainsZeroLengthNormals())
                    {
                        warn(string.Format("pointcloud for observation {0} has zero length normals", obs.Name));
                        Interlocked.Increment(ref nf);
                        return;
                    }

                    obsToMesh.AddOrUpdate(obs.Points.Name, _ => mesh, (_, __) => mesh);

                    Interlocked.Increment(ref nc);
                });

            Mesh aggregatePointCloud = new Mesh(hasNormals: true);
            if (useCleverCombine)
            {
                info("clever combine point cloud");
                var meshes = new List<Mesh>();
                var origins = new List<Vector3>();
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
                aggregatePointCloud = CleverCombinePointClouds.Combine(origins.ToArray(), meshes.ToArray());
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
                Boundary = PoissonReconstruction.BoundaryTypes.Neumann,

                // no features should be finer than this many meters as this is the finest the octree will dice
                MinOctreeCellWidthMeters = 0.05f,

                // a value on the upper end of the suggested range in the docs
                // meaning we think our data in noisy, so wait for this many samples in a cell
                MinOctreeSamplesPerCell = 15,

                // attempts to allow higher order surfaces than the defaults
                BSplineDegree = 2,

                // indicates the normal magnitudes are not uniformly unit scaled
                // to indicate confidence in the position attached to it
                UseNormalsForConfidence = true
            };

            var ret = PoissonReconstruction.Reconstruct(aggregatePointCloud, poissonOpts);

            info(string.Format("Poisson reconstructed mesh with {0} faces", Fmt.KMG(ret.Faces.Count)));

            return ret;
        }
    }
}
