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

    public class ChunkInput : TileServerOperation
    {
        static ILog logger = LogManager.GetLogger(typeof(ChunkInput));

        public const string MESH_EXT =  ".ply";
        public const string IMAGE_EXT = ".tif";
        public const int CHUNK_RESOLUTION = 2048;
        const int FacesPerChunk = 100000;

        private ChunkInputMessage message;

        public ChunkInput(ChunkInputMessage message, PipelineCore pipeline, TileServerCloud cloud)
            : base(message.ProjectName, pipeline, cloud, logger)
        {
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
            LogInfo("started chunking input " + message.InputName);
            var project = TilingProject.Find(pipeline.DynamoContext, message.ProjectName);
            var input = TilingInput.Find(pipeline.DynamoContext, project.Name, message.InputName);
            if (input.Chunked)
            {
                LogInfo("input " + message.InputName + " has already been chunked, skipping");
                cloud.MasterQueue.Enqueue(message);
                return;
            }

            LogInfo("downloading " + input.MeshUrl);
            Mesh mesh = null;
            TemporaryFile.GetAndDelete(Path.GetExtension(input.MeshUrl), f =>
            {
                pipeline.Storage(input.MeshUrl).DownloadFile(input.MeshUrl, f);
                mesh = Mesh.Load(f);
                mesh.RemoveInvalidFaces();
                mesh.Clean();
            });
            Image image = null;
            string imageBaseUrl = null;
            if (input.ImageUrl != null)
            {
                LogInfo("downloading " + input.ImageUrl);
                TemporaryFile.GetAndDelete(Path.GetExtension(input.ImageUrl), f =>
                {
                    pipeline.Storage(input.ImageUrl).DownloadFile(input.ImageUrl, f);
                    image = Image.Load(f);
                });
                input.ImageBands = image.Bands;
                input.ImageWidth = image.Width;
                input.ImageHeight = image.Height;

                LogInfo("chunking image for input " + message.InputName);
                var sparseImage = new SparseCloudImage(image, pipeline, CHUNK_RESOLUTION);
                imageBaseUrl =  TileServerConfig.Instance.ChunkUrl(project.Name, Guid.NewGuid().ToString());
                sparseImage.Save<byte>(imageBaseUrl, IMAGE_EXT);

            }
            LogInfo("building acceleration structures to chunk input " + message.InputName);
            var multiClipper = new MultiMeshClipper();
            var dataset = new MultiMeshClipperInput(mesh, image);
            multiClipper.AddInput(dataset);

            LogInfo("building mesh chunks for input " + message.InputName);
            var tilingScheme = new BinaryTreeTilingScheme();
            var splitCriteria = new FaceSplitCriteria(FacesPerChunk);
            var root = TileLocalMesh.BuildBoundsTree(multiClipper, tilingScheme, splitCriteria);
            
            ConcurrentBag<string> chunkIds = new ConcurrentBag<string>();
            var leaves = root.Leaves().ToList();
            Serial.ForEach(leaves, (leaf, pls, i) =>
            {
                TemporaryFile.GetAndDelete(MESH_EXT, f =>
                {
                    BoundingBox bounds = leaf.GetComponent<NodeBounds>().Bounds;
                    string id = Guid.NewGuid().ToString();
                    Mesh m = multiClipper.Clip(bounds, true);
                    m.Save(f);
                    string meshUrl = TileServerConfig.Instance.ChunkUrl(project.Name, id + MESH_EXT);
                    pipeline.Storage(meshUrl).UploadFile(f, meshUrl);
                    TilingInputChunk record = TilingInputChunk.Create(pipeline.DynamoContext, id, project,
                                                                      meshUrl, imageBaseUrl, m.Bounds());
                    chunkIds.Add(id);
                    LogInfo(string.Format("generated chunk {0}/{1} for input {2}",
                                          chunkIds.Count(), leaves.Count, message.InputName));
                });
            });
            input.ChunkIds = chunkIds.ToList();
            input.Chunked = true;
            input.Save(pipeline.DynamoContext);
            cloud.MasterQueue.Enqueue(message);
            LogInfo("completed chunking input " + message.InputName);
        }
    }
}
