using log4net;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.IO;
using OPS.Pipeline.TileServer;
using OPS.Geometry;
using OPS.Plumbing;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Util;
using OPS.Alignment;
using OPS.RayTrace;
using System;

namespace OPS.Pipeline.MeshWorker
{
    public class BuildBackprojectLeavesMessage : TilingQueueMessage
    {
        public List<string> TileIds { get; set; }

        public BuildBackprojectLeavesMessage() { }

        public BuildBackprojectLeavesMessage(string projectName, List<string> tileIds) : base(projectName)
        {
            this.TileIds = tileIds;
        }
    }

    class BuildBackprojectLeaves
    {
        static ILog logger = LogManager.GetLogger(typeof(BuildBackprojectLeaves));

        StartWorker pipeline;
        BuildBackprojectLeavesMessage message;

        public BuildBackprojectLeaves(BuildBackprojectLeavesMessage message, StartWorker pipeline)
        {
            this.pipeline = pipeline;
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
            logger.Info("Collecting tiling information...");
            TilingProject project = TilingProject.Find(pipeline.DynamoContext, message.ProjectName);
            List<TilingNode> leaves = GetLeavesToProcess(project);

            logger.Info("Downloading full mesh...");
            Mesh fullMesh = GetFullMesh(project);

            logger.Info("Preparing full mesh...");
            MeshOperator op = new MeshOperator(fullMesh);
            SceneCaster sc = new SceneCaster();
            sc.AddMesh(fullMesh, null, Matrix.Identity);
            sc.Build();

            logger.Info("Building scene graph...");
            Frame rootFrame = Frame.Find(pipeline.DynamoContext, project.Name, MSLProject.ROOT_FRAME_NAME);
            BuildSceneGraph builder = new BuildSceneGraph(pipeline);
            BuildSceneGraph.Options opts = new BuildSceneGraph.Options();
            opts.IncludeObservation = ShouldIncludeObservation;
            AlignmentScene scene = builder.Build(rootFrame, new BuildSceneGraph.Options());

            // generate leaf tile data
            int tiledMeshes = 0;
            int numLeafTileNodes = leaves.Count();
            Serial.ForEach(leaves, leaf =>
            {
                int curTileIndex = Interlocked.Increment(ref tiledMeshes);
                logger.Info("Generating tile mesh number " + curTileIndex + "/" + numLeafTileNodes + " (" + (int)(curTileIndex / (float)numLeafTileNodes * 100) + "%): " + leaf.Id);

                MeshImagePair leafPair = new MeshImagePair();

                // make the leaf tile mesh
                leafPair.Mesh = op.Clip(leaf.GetBounds());
                if (!leafPair.Mesh.HasFaces)
                    throw new InvalidDataException();
                leafPair.Mesh = UVAtlas.Atlas(leafPair.Mesh, project.TileResolution, project.TileResolution, 0, 1, 1);
                ConvexHull meshHull = new ConvexHull(leafPair.Mesh);
                    
                // backproject
                List<BackprojectContext> observations = GetPossibleObservations(scene, leafPair.Mesh.Bounds(), meshHull);
                //TODO: backproject will be integrated here

                // placeholder solid texture simulating backproject results 
                leafPair.Image = new Image(3, project.TileResolution, project.TileResolution);
                leafPair.Image.ApplyInPlace(0, x => { return 1.0f; });

                //upload the mesh/texture pair and update the tiling node
                ThroughputManager.Run(() => TilingNode.Find(pipeline.DynamoContext, project, leaf.Id).SaveMesh(leafPair, pipeline, 0));
                
                //notify the tiling server that a tile is ready for building into parent tiles
                pipeline.CompletionQueue.Enqueue(new TileCompletedMessage(project.Name, leaf.Id));
            });

            logger.Info("Completed generating " + tiledMeshes + " tiles.");
            return 0;
        }

        private bool ShouldIncludeObservation(Observation observation, SceneNode parent)
        {
            RoverObservation ro = observation as RoverObservation;
            return ro.ObservationType == ObservationType.Image.ToString() && 
                   ro.Sensor != RoverProductCamera.MAHLI.ToString() &&
                   ro.Sensor != RoverProductCamera.Unknown.ToString();
        }

        // assumes mesh is built with the origin at the origin at the root frame the scene graph was built with
        private List<BackprojectContext> GetPossibleObservations(AlignmentScene scene, BoundingBox tileBounds, ConvexHull tileHull)
        {
            List<BackprojectContext> results = new List<BackprojectContext>();

            foreach ( SceneNode node in scene.Root.DepthFirstTraverse())
            {
                NodeImageReference imgRef = node.GetComponent<NodeImageReference>();
                if (imgRef == null)
                    continue;

                //download image
                Image img = pipeline.Load(imgRef.Reference, false);
               
                //validate image has a supported config
                PDSMetadata md = img.Metadata as PDSMetadata;
                PDSParser parser = new PDSParser(md);
                if (!SupportedPDSFile(parser))
                    continue;

                //coarse frustum cull: does this mesh's hull intersect the cameras
                Matrix imgToWorld = node.GetComponent<NodeUncertainTransform>().To(scene.Root).Mean;
                Matrix worldToImg = Matrix.Invert(imgToWorld);
                ConvexHull focusedImageHull = CreateImageHullForMesh(tileBounds, md.CameraModel, md.Width, md.Height, imgToWorld, worldToImg, parser.MinimumFocusDistance);
                if (!tileHull.Intersects(focusedImageHull))
                    continue;

                //found an image that could possibly contribute image data, save context for later use by backproject
                BackprojectContext context = new BackprojectContext();
                context.Parser = parser;
                context.CameraModel = new PDSCameraModelParser(parser.metadata).Parse();
                context.FocusedImageHull = focusedImageHull;
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
            var inputs = TilingInput.Find(pipeline.DynamoContext, project).ToList();
            InputChunkGroup bigMeshGroup = new InputChunkGroup();
            foreach (var input in inputs)
            {
                foreach (var chunkId in input.ChunkIds)
                {
                    TilingInputChunk chunk = TilingInputChunk.Find(pipeline.DynamoContext, chunkId);
                    bigMeshGroup.Chunks.Add(chunk); 
                }
            }

            var meshes = bigMeshGroup.Chunks.Select(c =>
            {
                Mesh m = null;
                TemporaryFile.GetAndDelete(Path.GetExtension(c.MeshUrl), f =>
                {
                    pipeline.Storage(c.MeshUrl).DownloadFile(c.MeshUrl, f);
                    m = Mesh.Load(f);
                });
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
        private List<TilingNode> GetLeavesToProcess(TilingProject project)
        {
            List<TilingNode> leaves = new List<TilingNode>();

            foreach (var id in this.message.TileIds)
            {
                leaves.Add(TilingNode.Find(pipeline.DynamoContext, project, id));
            }

            // Send completion messages for leaves that are already done
            foreach (var n in leaves)
            {
                if (n.MeshUrl != null)
                {
                    logger.Info(n.Id + " skipping");
                    pipeline.CompletionQueue.Enqueue(new TileCompletedMessage(project.Name, n.Id));
                }
            }

            // Filter any completed leaves
            return leaves.Where(n => n.MeshUrl == null).ToList();
        }

        bool SupportedPDSFile(PDSParser parser)
        {
            // backproject needs to be validated with these image types 
            if (parser.IsDownsampled || parser.ImageSizeType == RoverProductSize.Thumbnail)
                return false; 

            //no way to tell good or bad pixels (not marked with missing/invalid constant in the image that was tested)
            if (parser.IsPartial)
                return false;
     
            return true;
        }

        private class BackprojectContext
        {
            public PDSParser Parser;
            public ConvexHull FocusedImageHull;
            public CameraModel CameraModel;
        };
    }
}
