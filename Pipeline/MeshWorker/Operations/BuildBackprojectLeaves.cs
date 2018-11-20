using log4net;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.IO;
using OPS.Pipeline;
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

    public class BuildBackprojectLeaves : TileServerOperation
    {
        private BuildBackprojectLeavesMessage message;

        private Options options;

        struct Options
        {
            public string AlignmentProjectName; //the project name that contains the alignment data to be used with this tiling project
        }

        public BuildBackprojectLeaves(BuildBackprojectLeavesMessage message, PipelineCore pipeline, TileServerCloud cloud)
            : base(message, pipeline, cloud)
        {
            this.message = message;

            options.AlignmentProjectName = message.ProjectName;
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
            LogInfo("collecting tiling information");
            TilingProject project = TilingProject.Find(pipeline.DynamoContext, message.ProjectName);
            List<TilingNode> leaves = GetLeavesToProcess(project);

            LogInfo("downloading full mesh");
            Mesh fullMesh = GetFullMesh(project);

            LogInfo("preparing full mesh");
            MeshOperator op = new MeshOperator(fullMesh);
            SceneCaster sc = new SceneCaster();
            sc.AddMesh(fullMesh, null, Matrix.Identity);
            sc.Build();

            LogInfo("building scene graph");
            Frame rootFrame = Frame.Find(pipeline.DynamoContext, options.AlignmentProjectName, MSLProject.ROOT_FRAME_NAME);

            if (rootFrame == null)
            {
                LogError("alignment project " + options.AlignmentProjectName + " not found");
                return 1;
            }

            BuildSceneGraph builder = new BuildSceneGraph(pipeline);
            BuildSceneGraph.Options opts = new BuildSceneGraph.Options
            {
                IncludeObservation = (o, p) => o.UseForReconstruction && o.ObservationType == ObservationType.Image.ToString(),
                RequireFeaturesForImageReferences = false
            };
            AlignmentScene scene = builder.Build(rootFrame, opts);

            // generate leaf tile data
            int tiledMeshes = 0;
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
                MarkUndesiredObservations(scene);
                List<BackprojectContext> observations = GetPossibleObservations(scene, leafPair.Mesh.Bounds(), meshHull);
                //...backproject will take place here...

                // placeholder solid texture simulating backproject results 
                leafPair.Image = new Image(3, project.TileResolution, project.TileResolution);
                leafPair.Image.ApplyInPlace(0, x => { return 1.0f; });

                //upload the mesh/texture pair and update the tiling node
                ThroughputManager.Run(() =>
                {
                    var node = TilingNode.Find(pipeline.DynamoContext, project.Name, leaf.Id);
                    node.SaveMesh(leafPair, pipeline, 0, project.ExportMeshFormat, project.ExportImageFormat);
                });

                //notify the tiling server that a tile is ready for building into parent tiles
                cloud.MasterQueue.Enqueue(new TileCompletedMessage(project.Name, leaf.Id));                
            });

            LogInfo("batch completed, generated " + tiledMeshes + " leaf tiles");
                        
            return 0;
        }

        /// <summary>
        /// sweep through the alignment scene and mark any less desirable versions of the same image ans not for use in backproject
        /// this prioritizes the images with the same code used during alignment (eg. preferring non-linearized versions, preferring the most current version, etc)
        /// </summary>
        /// <param name="scene"></param>
        private void MarkUndesiredObservations(AlignmentScene scene)
        {
            foreach (SceneNode node in scene.Root.DepthFirstTraverse())
            {
                if (node.IsLeaf)
                    continue;

                var imgRefs = node.Children.Where(n => n.HasComponent<NodeImageReference>()).Select(n => n.GetComponent<NodeImageReference>().Reference);
                var obs = imgRefs.Select(n => ((ObservationImageRef)n).Observation);
                var groups = obs.GroupBy(ob => ob.FrameName);

                foreach (var group in groups)
                {
                    if (group.Count() == 1)
                        continue;
                     
                    List<RoverObservation> robs = new List<RoverObservation>();
                    foreach(var ob in group)
                    {
                        RoverObservation rob = ThroughputManager.Run(() => RoverObservation.Find(pipeline.DynamoContext, options.AlignmentProjectName, ob.Name));
                        if (rob != null)
                        {
                            robs.Add(rob);
                        }
                    }
                   
                    RoverObservation best = MSLProject.FindBestImage(robs);

                    foreach(var ob in group)
                    {
                        if(ob.Name != best.Name)
                        {
                            ob.UseForReconstruction = false;
                        }
                    }
                }
            }
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

                Observation obs = ((ObservationImageRef)(imgRef.Reference)).Observation;
                if (obs.UseForReconstruction == false)
                    continue;

                //download image
                Image img = pipeline.Load(imgRef.Reference, false);
               
                //validate image has a supported config
                PDSMetadata md = img.Metadata as PDSMetadata;
                PDSParser parser = new PDSParser(md);
              
                //coarse frustum cull: does this mesh's hull intersect the cameras
                Matrix imgToWorld = node.GetComponent<NodeUncertainTransform>().To(scene.Root).Mean;
                Matrix worldToImg = Matrix.Invert(imgToWorld);
                ConvexHull focusedImageHull = CreateImageHullForMesh(tileBounds, md.CameraModel, md.Width, md.Height, imgToWorld, worldToImg, parser.MinimumFocusDistance);
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
                leaves.Add(TilingNode.Find(pipeline.DynamoContext, project.Name, id));
            }

            // Send completion messages for leaves that are already done
            foreach (var n in leaves)
            {
                if (n.MeshUrl != null)
                {
                    LogInfo("leaf " + n.Id + " already complete, skipping");
                    cloud.MasterQueue.Enqueue(new TileCompletedMessage(project.Name, n.Id));
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
