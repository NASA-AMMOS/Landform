using System;
using System.Collections.Generic;
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
            pipeline.LogInfo("started");

            //load transforms, by filtering by allowed transform sources or allowing all
            var frameCache = new FrameCache(pipeline, projectName);
            frameCache.Preload(loadTransforms: true);
            
            //load observations
            var observationCache = new ObservationCache(pipeline, projectName);
            observationCache.Preload(obs => obs.UseForReconstruction);

            string outputFrame = "root";
            Mesh surfacedMesh = BuildMesh(pipeline, projectName, out BoundingBox pointBounds, frameCache, observationCache, outputFrame, usePriors:false, noPriors:false, onlyForCameras:null, useCleverCombine:false, allowMastcam:false);
            if (surfacedMesh == null || surfacedMesh.Vertices.Count == 0)
            {
                pipeline.LogError("point cloud failed to reconstruct");
                return 1;
            }

            //upload mesh
            string meshName = "FullMesh";
            string meshOutputUrl = pipeline.GetStorageUrl("input", projectName, meshName + ".ply");
            TemporaryFile.GetAndDelete(".ply", tempFile =>
            {
                pipeline.LogInfo("uploading mesh " + meshOutputUrl);
                surfacedMesh.Save(tempFile);
                pipeline.SaveFile(tempFile, meshOutputUrl);
            });

            //create a tiling input
            TilingProject tilingProject = TilingProject.Find(pipeline, projectName);
            TilingInput.Create(pipeline, meshName, tilingProject, meshOutputUrl, null, null);

            //indicate successs to the tiling server master
            pipeline.EnqueueToMaster(new BuildTilingInputMessage(projectName));

            pipeline.LogInfo("complete");

            return 0;
        }

        static public Mesh BuildMesh(PipelineCore pipeline, string projectName, out BoundingBox pointBounds, FrameCache frameCache, ObservationCache observationCache, string outputFrame, bool usePriors, bool noPriors, string onlyForCameras = null, bool useCleverCombine=false, bool allowMastcam=false, int decimate = 1)
        {
            pointBounds = new BoundingBox();
           
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

            //temporarily suppress mastcam point cloud data until validated
            //https://github.jpl.nasa.gov/OnSight/Landform/issues/261
            var opts = new MeshObservations.CollectOptions(null, null, onlyForCameras, mission)
                {
                    AllowMastcam = allowMastcam,
                    RequirePoints = true,
                    RequireNormals = true,
                    RequireTextures = false,
                    RequirePriorTransform = usePriors,
                    RequireAdjustedTransform = noPriors,
                    TargetFrame = outputFrame
                };

            var observations = MeshObservations.Collect(frameCache, observationCache, opts);
            if (observations.Count == 0)
            {
                pipeline.LogError("no observations were found to build a point cloud");
                return null;
            }

            var meshOpts = new MeshObservations.MeshOptions()
            {
                Frame = outputFrame,
                ScaleNormalsByConfidence = true,
                Decimate = decimate
            };

            //accumulate the collection of point clouds
            List<Mesh> meshInputs = new List<Mesh>();
            List<Vector3> meshOrigins = new List<Vector3>();
            for (int idx = 0; idx < observations.Count; idx++)
            {
                var obs = observations[idx];
                pipeline.LogInfo("building point cloud {0}/{1} ({2})%): {3}", idx + 1, observations.Count,
                                    (int)(100 * idx / (float)(observations.Count - 1)), obs.Points.FrameName);

                var mesh = obs.BuildPointCloud(pipeline, frameCache, masker, meshOpts);
                if (mesh == null)
                {
                    pipeline.LogError("failed to build pointcloud for {0}", obs.Name);
                    continue;
                }

                if (mesh.ContainsZeroLengthNormals())
                {
                    pipeline.LogError("pointcloud has zero length normals for {0}", obs.Name);
                    continue;
                }

                //the reference point used to determine how good a point is for clever combine
                //naive version is using distance from camera
                var obsToOutput = frameCache.GetObservationTransform(obs.Points, outputFrame, usePriors, noPriors);
                if (obsToOutput == null)
                {
                    pipeline.LogError("failed to get transform for {0}", obs.Name);
                    continue;
                }

                CAHV cam = (CameraModel)JsonHelper.FromJson(obs.Points.CameraModel) as CAHV;
                Vector3 cameraPosInOutput = Vector3.Transform(cam.C, obsToOutput.Mean);

                meshOrigins.Add(cameraPosInOutput);
                meshInputs.Add(mesh);
            }

            // combine into large point cloud
            Mesh aggregatePointCloud = new Mesh(hasNormals: true);
            if (useCleverCombine)
            {
                pipeline.LogInfo("combining {0} point clouds with clever combine", meshInputs.Count());
                aggregatePointCloud = CleverCombinePointClouds.Combine(meshOrigins.ToArray(), meshInputs.ToArray());
            }
            else
            {
                aggregatePointCloud.MergeWith(meshInputs.ToArray(), normalize: false, removeDuplicateVerts: false);
            }

            //significant memory usage
            meshInputs.Clear();
            meshOrigins.Clear();

            // build the large mesh from the aggregate point cloud using poisson reconstruction
            if (aggregatePointCloud.Vertices.Count == 0)
            {
                pipeline.LogError("aggregate point cloud contains no points");
                return null;
            }

            pointBounds = aggregatePointCloud.Bounds();

            pipeline.LogInfo("reconstructing point cloud: " + aggregatePointCloud.Vertices.Count() + " vertices");
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

            return PoissonReconstruction.Reconstruct(aggregatePointCloud, poissonOpts);
        }
    }
}
