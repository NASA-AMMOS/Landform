using log4net;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.IO;
using OPS.Pipeline;
using OPS.Pipeline.TileServer;
using OPS.Pipeline.AlignmentServer;
using OPS.Geometry;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Util;
using OPS.Alignment;
using OPS.RayTrace;
using System;

namespace OPS.Pipeline.MeshWorker
{
    public class BuildBackprojectLeavesMessage : QueueMessage
    {
        public List<string> TileIds;
        public BuildBackprojectLeavesMessage() { }
        public BuildBackprojectLeavesMessage(string projectName) : base(projectName) { }
    }

    public class BuildBackprojectLeaves : CloudPipelineOperation
    {
        private readonly BuildBackprojectLeavesMessage message;

        public BuildBackprojectLeaves(CloudPipeline pipeline, BuildBackprojectLeavesMessage message)
            : base(pipeline, message)
        {
            this.message = message;
        }

        class InputChunkGroup
        {
            public List<TilingInputChunk> Chunks = new List<TilingInputChunk>();
        }

        /// <summary>
        /// dices a large mesh into the meshes required for the leaf tiling nodes
        /// generates appropriate texture data from observations
        /// uploads data to storage and updates tiling node urls to point at them
        /// </summary>
        /// <returns></returns>
        public int Process()
        {
            LogInfo("started batch of " + message.TileIds.Count + " leaf tiles");

            TilingProject project = TilingProject.Find(pipeline, projectName);

            LogInfo("downloading full mesh");
            Mesh fullMesh = GetFullMesh(project);

            LogInfo("preparing full mesh");
            MeshOperator op = new MeshOperator(fullMesh);
            SceneCaster sc = new SceneCaster();
            sc.AddMesh(fullMesh, null, Matrix.Identity);
            sc.Build();

            LogInfo("building scene graph");
            Project alignmentProject = Project.Find(pipeline, projectName);
            BuildSceneGraph builder = new BuildSceneGraph(pipeline, projectName,
                                                          new BuildSceneGraph.Options() { OnlyKeepBestImages = true });
            AlignmentScene scene = builder.BuildTopDown(alignmentProject.RootFrame);

            // generate leaf tile data
            int tiledMeshes = 0;
            List<TilingNode> leaves = GetLeavesToProcess();
            int numLeafTileNodes = leaves.Count();
            Serial.ForEach(leaves, leaf =>
            {
                int curTileIndex = Interlocked.Increment(ref tiledMeshes);
                LogInfo("generating tile mesh " + curTileIndex + "/" + numLeafTileNodes + " (" + (int)(curTileIndex / (float)numLeafTileNodes * 100) + "%): " + leaf.Id);

                MeshImagePair leafPair = new MeshImagePair();

                // make the leaf tile mesh
                leafPair.Mesh = op.Clip(leaf.GetBounds());
                if (leafPair.Mesh.Vertices.Count < 3)
                {
                    throw new Exception("invalid tile contains less than 3 verts");
                }

                leafPair.Mesh = UVAtlas.Atlas(leafPair.Mesh, project.TileResolution, project.TileResolution);
                ConvexHull meshHull = new ConvexHull(leafPair.Mesh);

                // backproject
                List<BackprojectContext> observations = GetPossibleObservations(scene, leafPair.Mesh.Bounds(), meshHull);
                //...backproject will take place here...

                // placeholder solid texture simulating backproject results 
                leafPair.Image = new Image(3, project.TileResolution, project.TileResolution);
                leafPair.Image.ApplyInPlace(0, x => { return 1.0f; });

                //upload the mesh/texture pair and update the tiling node
                var node = TilingNode.Find(pipeline, projectName, leaf.Id);
                node.SaveMesh(leafPair, pipeline, 0, project.ExportMeshFormat, project.ExportImageFormat,
                              project.GetSkirtMode());

                //notify the tiling server that a tile is ready for building into parent tiles
                pipeline.MasterQueue.Enqueue(new TileCompletedMessage(projectName) { TileId = leaf.Id});                
            });

            LogInfo("batch completed, generated " + tiledMeshes + " leaf tiles");
                        
            return 0;
        }


        // assumes mesh is built with the origin at the origin at the root frame the scene graph was built with
        private List<BackprojectContext> GetPossibleObservations(AlignmentScene scene, BoundingBox tileBounds,
                                                                 ConvexHull tileHull)
        {
            List<BackprojectContext> results = new List<BackprojectContext>();
            foreach (SceneNode node in scene.Root.DepthFirstTraverse())
            {
                if (!node.HasComponent<NodeObservation>())
                {
                    continue;
                }

                var obs = node.GetComponent<NodeObservation>().Observation;

                //validate image has a supported config
                PDSMetadata md = pipeline.LoadImage(obs.Url).Metadata as PDSMetadata;
                PDSParser parser = new PDSParser(md);
              
                //coarse frustum cull: does this mesh's hull intersect the cameras
                Matrix imgToWorld = node.GetComponent<NodeUncertainTransform>().To(scene.Root).Mean;
                Matrix worldToImg = Matrix.Invert(imgToWorld);
                ConvexHull focusedImageHull =
                    CreateImageHullForMesh(tileBounds, md.CameraModel, md.Width, md.Height, imgToWorld, worldToImg,
                                           parser.MinimumFocusDistance);
                if (!tileHull.Intersects(focusedImageHull))
                    continue;

                //found an image that could possibly contribute image data, save context for later use by backproject
                BackprojectContext context = new BackprojectContext
                {
                    Parser = parser,
                    CameraModel = new PDSCameraModelParser(parser.metadata).Parse(),
                    FocusedImageHull = focusedImageHull
                };
                results.Add(context);
            }

            return results;
        }

        /// <summary>
        /// creates a convex hull of an image frustum suitable for clipping against a given mesh
        /// creates the hull between the minimum focus distance of the camera and the farthest possible distance a point for this mesh could be from the camera
        /// </summary>
        /// <param name="imageToMesh">the transformation from the mesh (usually primary site drive local level) to the image (usually rover nav frame for the image's sitedrive)</param>
        /// <returns>convex hull in the mesh's coordinate frame</returns>
        private static ConvexHull CreateImageHullForMesh(BoundingBox meshBounds, CameraModel camModel, int imageWidth, int imageHeight, Matrix imageToMesh, Matrix meshToImage, double nearClip)
        { 
            double farClip = 0;
            foreach (Vector3 meshCorner in meshBounds.GetCorners())
            {
                Vector3 roverCorner = Vector3.Transform(meshCorner, meshToImage);
                camModel.Project(roverCorner, out double range);
                farClip = Math.Max(farClip, Math.Abs(range));
            }

            if (farClip <= nearClip)
                throw new InvalidOperationException("this mesh is not visible to the camera");

            ConvexHull imageHull = ConvexHull.FromParams(camModel, imageWidth, imageHeight, nearClip, farClip);
            imageHull.Transform(imageToMesh);
            return imageHull;
        }
        
        /// <summary>
        /// downloads all the chunks for the full mesh to recreate the full mesh
        /// </summary>
        /// <param name="project"></param>
        /// <returns></returns>
        private Mesh GetFullMesh(TilingProject project)
        {
            Mesh fullMesh;
            var inputs = TilingInput.Find(pipeline, project).ToList();
            InputChunkGroup bigMeshGroup = new InputChunkGroup();
            foreach (var input in inputs)
            {
                foreach (var chunkId in input.ChunkIds)
                {
                    TilingInputChunk chunk = TilingInputChunk.Find(pipeline, chunkId);
                    bigMeshGroup.Chunks.Add(chunk); 
                }
            }

            var meshes = bigMeshGroup.Chunks.Select(c =>
            {
                Mesh m = null;
                pipeline.GetFile(c.MeshUrl, f => m = Mesh.Load(f));
                return m;
            });

            fullMesh = Mesh.Merge(meshes.ToArray());
            fullMesh.Clean();
            return fullMesh;
        }

        /// <summary>
        /// get the leaves for the project that are not completed yet
        /// </summary>
        /// <param name="project"></param>
        /// <returns></returns>
        private List<TilingNode> GetLeavesToProcess()
        {
            List<TilingNode> leaves = new List<TilingNode>();

            foreach (var id in message.TileIds)
            {
                leaves.Add(TilingNode.Find(pipeline, projectName, id));
            }

            // Send completion messages for leaves that are already done
            foreach (var n in leaves)
            {
                if (n.MeshUrl != null)
                {
                    LogInfo("leaf " + n.Id + " already complete, skipping");
                    pipeline.MasterQueue.Enqueue(new TileCompletedMessage(projectName) { TileId = n.Id });
                }
            }

            // Filter any completed leaves
            return leaves.Where(n => n.MeshUrl == null).ToList();
        }

        private class BackprojectContext
        {
            public PDSParser Parser;
            public ConvexHull FocusedImageHull;
            public CameraModel CameraModel;
        };
    }
}
