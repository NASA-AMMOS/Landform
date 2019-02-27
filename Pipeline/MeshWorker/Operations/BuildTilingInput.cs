using System;
using System.Collections.Generic;
using System.Linq;
using log4net;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Cloud;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Pipeline.TileServer;

namespace OPS.Pipeline.MeshWorker
{
    public class BuildTilingInputMessage : QueueMessage
    {
        public BuildTilingInputMessage() { }
        public BuildTilingInputMessage(string projectName) : base(projectName) { }
    }

    /// <summary>
    /// create a large mesh from input data and uploads it as the tiling input
    /// </summary>
    public class BuildTilingInput : CloudPipelineOperation
    {
        private readonly BuildTilingInputMessage message;

        public BuildTilingInput(CloudPipeline pipeline, BuildTilingInputMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public int Process()
        {
            pipeline.LogInfo("started");

            var frameCache = new FrameCache(pipeline, projectName);
            frameCache.Preload(loadTransforms: true);
            var observationCache = new ObservationCache(pipeline, projectName);
            observationCache.Preload(obs => obs.UseForReconstruction);

            List<MeshObservations> observations = Meshing.CollectMeshObservations(frameCache, observationCache,
                                                                                  allowMastcam: false,
                                                                                  requireNormals: true);
            if (observations.Count == 0)
            {
                pipeline.LogError("no observations were found to build a point cloud");
                return 1;
            }
            
            //accumulate the large point cloud
            Mesh aggregatePointCloud = new Mesh(hasNormals: true);
            for (int idx = 0; idx < observations.Count; idx++)
            {
                var obs = observations[idx];
                pipeline.LogInfo("building point cloud {0}/{1} ({2})%): {3}", idx + 1, observations.Count,
                                 (int)(100 * idx / (float)observations.Count), obs.Points.FrameName);
                var mesh = Meshing.BuildPointCloud(pipeline, obs, frameCache, scaleNormalsByConfidence: true);
                aggregatePointCloud.MergeWith(new Mesh[] { mesh }, false);
            }
            
            // build the large mesh from the aggregate point cloud using poisson reconstruction
            if (aggregatePointCloud.Vertices.Count == 0)
            {
                pipeline.LogError("aggregate point cloud contains no points");
                return 1;
            }
          
            pipeline.LogInfo("reconstructing point cloud: " + aggregatePointCloud.Vertices.Count() + " vertices");
            PoissonReconstruction.Options opts = new PoissonReconstruction.Options
            {
                // suppresses the large wings often seen when extrapolating without orbital data 
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
                UseNormalsForConfidence = true
            };

            Mesh surfacedMesh = PoissonReconstruction.Reconstruct(aggregatePointCloud, opts);            
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
            pipeline.MasterQueue.Enqueue(new BuildTilingInputMessage(projectName));

            pipeline.LogInfo("complete");

            return 0;
        }
    }
}
