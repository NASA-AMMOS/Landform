using log4net;
using OPS.Geometry;
using OPS.Plumbing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;
using OPS.Util;
using Microsoft.Xna.Framework;
using System.Collections.Concurrent; 

namespace OPS.Pipeline.TileServer
{

    public class ChunkInputMessage : TilingQueueMessage
    {
        public string InputName { get; set; }

        public ChunkInputMessage() { }

        public ChunkInputMessage(string projectName, string inputName) : base(projectName)
        {
            this.InputName = inputName;
        }
    }

    public class ChunkInput
    {

        static ILog logger = LogManager.GetLogger(typeof(ChunkInput));

        public const string MESH_EXT =  ".ply";
        public const string IMAGE_EXT = ".tif";
        public const int CHUNK_RESPLUTION = 2046;
        const int FacesPerChunk = 100000;
        StartWorker pipeline;
        ChunkInputMessage message;

        public ChunkInput(ChunkInputMessage message, StartWorker pipeline)
        {
            this.pipeline = pipeline;
            this.message = message;
        }

        class ChunkData
        {
            public string NodeId;
            public BoundingBox Bounds;
            public ChunkData(string id, BoundingBox box)
            {
                NodeId = id;
                Bounds = box;
            }
        }


        public void Process()
        {
            logger.Info("Processing message");
            var project = TilingProject.Find(pipeline.DynamoContext, this.message.ProjectName);
            var input = TilingInput.Find(pipeline.DynamoContext, project, message.InputName);
            if (input.Chunked)
            {
                logger.Info("Input has already been chunked");
                pipeline.CompeltionQueue.Enqueue(this.message);
                return;
            }

            logger.Info("Downloading: " + input.MeshUrl);
            Mesh mesh = null;
            TemporaryFile.GetAndDelete(Path.GetExtension(input.MeshUrl), f =>
            {
                pipeline.Storage.DownloadFile(input.MeshUrl, f);
                mesh = Mesh.Load(f);
                mesh.RemoveInvalidFaces();
                mesh.Clean();
            });
            Image image = null;
            string imageBaseUrl = null;
            if (input.ImageUrl != null)
            {
                logger.Info("Downloading: " + input.ImageUrl);
                TemporaryFile.GetAndDelete(Path.GetExtension(input.ImageUrl), f =>
                {
                    pipeline.Storage.DownloadFile(input.ImageUrl, f);
                    image = Image.Load(f);
                });
                input.ImageBands = image.Bands;
                input.ImageWidth = image.Width;
                input.ImageHeight = image.Height;

                logger.Info("Chunk image");
                var sparseImage = new SparseCloudImage(image, this.pipeline, CHUNK_RESPLUTION);
                imageBaseUrl = Path.Combine(Path.Combine(TileServerCloud.ChunkUrlBase, project.Name), Guid.NewGuid().ToString());
                // TODO: maintain original bit depth
                sparseImage.Save<byte>(imageBaseUrl, IMAGE_EXT);

            }
            logger.Info("Building acceleration structures");
            var thing = new TileLocalMesh.TilingInput();
            var dataset = new TileLocalMesh.TilingInputDataset(mesh, image);
            thing.AddDataset(dataset);
            // TODO: migrate toward using sparse image so we don't need to know tile definitions

            logger.Info("Building mesh chunks");
            var tilingScheme = new BinaryTreeTilingScheme();
            var splitCriteria = new FaceLimitSplitCriteria(FacesPerChunk);
            var root = TileLocalMesh.BuildBoundsTree(thing, tilingScheme, splitCriteria);
            
            ConcurrentBag<string> chunkIds = new ConcurrentBag<string>();
            var leaves = root.Leaves().ToList();
            Serial.ForEach(leaves, (leaf, pls, i) =>
            {
                TemporaryFile.GetAndDelete(MESH_EXT, f =>
                {
                    BoundingBox bounds = leaf.GetComponent<NodeBounds>().Bounds;
                    string id = Guid.NewGuid().ToString();
                    Mesh m = thing.Clip(bounds, true);
                    m.Save(f);
                    string meshUrl = new Uri(Path.Combine(Path.Combine(TileServerCloud.ChunkUrlBase, project.Name), id + MESH_EXT)).ToString();
                    pipeline.Storage.UploadFile(f, meshUrl);
                    TilingInputChunk record = TilingInputChunk.Create(pipeline.DynamoContext, id, project, meshUrl, imageBaseUrl, m.Bounds());
                    chunkIds.Add(id);
                    logger.Info(string.Format("Chunk: {0}/{1}", chunkIds.Count(), leaves.Count));
                });
            });
            input.ChunkIds = chunkIds.ToList();
            input.Chunked = true;
            input.Save(pipeline.DynamoContext);
            pipeline.CompeltionQueue.Enqueue(this.message);
            logger.Info("Done");
        }

    }
}
