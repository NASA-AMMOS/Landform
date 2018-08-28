using log4net;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using OPS.Pipeline.TileServer;
using OPS.Geometry;
using OPS.Plumbing;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Util;

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
            logger.Info("Texturing mesh...");

            // get tiling information
            TilingProject project = TilingProject.Find(pipeline.DynamoContext, message.ProjectName);
            List<TilingNode> leaves = CollectLeavesToProcess(project);

            //get big mesh
            var inputs = TilingInput.Find(pipeline.DynamoContext, project).ToList();
            InputChunkGroup bigMeshGroup = new InputChunkGroup();
            foreach (var input in inputs)
            {
                foreach (var chunkId in input.ChunkIds)
                {
                    TilingInputChunk chunk = TilingInputChunk.Find(pipeline.DynamoContext, chunkId);
                    bigMeshGroup.Chunks.Add(chunk); //for now bring down all chunks to make big mesh for all leaves   
                }
            }

            // Reconstruct a mesh for each input using only all chunks
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

            Mesh fullMesh = Mesh.Merge(meshes.ToArray());
            fullMesh.Clean();
            MeshOperator op = new MeshOperator(fullMesh);

            // generate leaf tile data
            int tiledMeshes = 0;
            int numLeafTileNodes = leaves.Count();
            Serial.ForEach(leaves, leaf =>
            {
                int curTileIndex = Interlocked.Increment(ref tiledMeshes);
                logger.Info("Generating tile number " + curTileIndex + "/" + numLeafTileNodes + " (" + (int)(curTileIndex / (float)numLeafTileNodes * 100) + "%): " + leaf.Id);

                MeshImagePair leafPair = new MeshImagePair();
                leafPair.Mesh = op.Clip(leaf.GetBounds());

                if (leafPair.Mesh.HasFaces)
                {
                    leafPair.Mesh = UVAtlas.Atlas(leafPair.Mesh, project.TileResolution, project.TileResolution, 0, 1, 1);

                    // placeholder solid texture simulating backproject results 
                    leafPair.Image = new Image(3, project.TileResolution, project.TileResolution);
                    leafPair.Image.ApplyInPlace(0, x => { return 1.0f; });

                    ThroughputManager.Run(() => TilingNode.Find(pipeline.DynamoContext, project, leaf.Id).SaveMesh(leafPair, pipeline, 0));
                    pipeline.CompletionQueue.Enqueue(new TileCompletedMessage(project.Name, leaf.Id));
                }
            });

            logger.Info("Completed generating " + tiledMeshes + " tiles.");
            return 0;
        }

        private List<TilingNode> CollectLeavesToProcess(TilingProject project)
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
    }
}
