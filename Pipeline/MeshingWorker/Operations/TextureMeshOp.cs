using log4net;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OPS.Pipeline.TileServer;
using OPS.Geometry;
using OPS.Plumbing;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Util;

namespace OPS.Pipeline.MeshingWorker
{
    // sent to the TextureMeshOp when the project's fullmesh TilingInputNode is ready to be processed
    public class TextureMeshMessage : TilingQueueMessage
    {
        public TextureMeshMessage() { }

        public TextureMeshMessage(string projectName) : base(projectName)
        { }
    }

    // sent by the TextureMeshOp as the last leaf tile completes processing
    public class TextureMeshCompletedMessage : TilingQueueMessage
    {
        public List<string> TileIds { get; set; }

        public TextureMeshCompletedMessage(string projectName, List<string> tileIds) : base(projectName)
        {
            this.TileIds = tileIds;
        }
    }

    class TextureMeshOp
    {
        static ILog logger = LogManager.GetLogger(typeof(TextureMeshOp));

        StartWorker pipeline;
        TextureMeshMessage message;

        public TextureMeshOp(TextureMeshMessage message, StartWorker pipeline)
        {
            this.pipeline = pipeline;
            this.message = message;
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

            // download the parent mesh
            TilingInputChunk tilingInput = TilingInputChunk.Find(pipeline.DynamoContext, "FullMesh");
            Mesh fullMesh = DownloadFullMesh(pipeline, tilingInput);
            fullMesh.Clean();
            MeshOperator op = new MeshOperator(fullMesh);

            // get tiling information
            TilingProject project = TilingProject.Find(pipeline.DynamoContext, message.ProjectName);
            SceneNode tilingRoot = TilingNode.BuildTreeFromDatabase(pipeline.DynamoContext, project);

            // generate leaf tile data
            int tiledMeshes = 0;
            int textureDimension = 128;
            int numLeafTileNodes = tilingRoot.Leaves().Count();
            Parallel.ForEach(tilingRoot.Leaves(), leaf =>
            {
                int curTileIndex = Interlocked.Increment(ref tiledMeshes);
                logger.Info("Generating tile number " + curTileIndex + "/" + numLeafTileNodes + " (" + (int)(curTileIndex / (float)numLeafTileNodes * 100) + "%): " + leaf.Name);

                MeshImagePair leafPair = new MeshImagePair();
                leafPair.Mesh = op.Clip(leaf.GetComponent<NodeBounds>().Bounds);

                if (leafPair.Mesh.HasFaces)
                {
                    leafPair.Mesh = UVAtlas.Atlas(leafPair.Mesh, textureDimension, textureDimension, 0, 1, 1);

                    // placeholder solid texture simulating backproject results 
                    leafPair.Image = new Image(3, textureDimension, textureDimension);
                    leafPair.Image.ApplyInPlace(0, x => { return (byte)255; });

                    ThroughputManager.Run(() => TilingNode.Find(pipeline.DynamoContext, project, leaf.Name).SaveMesh(leafPair, pipeline, 0));
                }
            });

            logger.Info("Completed generating " + tiledMeshes + " tiles.");
            return 0;
        }

        /// <summary>
        /// downloads the large parent mesh for the project and loads it into memory
        /// </summary>
        /// <param name="pipeline"></param>
        /// <param name="input"></param>
        /// <returns></returns>
        public Mesh DownloadFullMesh(PipelineCore pipeline, TilingInputChunk input)
        {
            Mesh result = null;
            TemporaryFile.GetAndDelete(".ply", f =>
            {
                logger.Info("Downloading parent mesh: " + input.MeshUrl);
                pipeline.Storage(input.MeshUrl).DownloadFile(input.MeshUrl, f);
                result = Mesh.Load(f);
            });

            if (result == null)
                throw new CloudException("Failed to download full project mesh");

            return result;
        }
        //}
    }
}
