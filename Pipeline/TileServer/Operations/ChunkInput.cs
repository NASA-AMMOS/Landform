using log4net;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent; 
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;

namespace OPS.Pipeline.TileServer
{

    public class ChunkInputMessage : QueueMessage
    {
        public string InputName;
        public ChunkInputMessage() { }
        public ChunkInputMessage(string projectName) : base(projectName) { }
    }

    public class ChunkInput : CloudPipelineOperation
    {
        public const string MESH_EXT =  ".ply";
        public const string IMAGE_EXT = ".tif";
        public const int CHUNK_RESOLUTION = 2048;
        public const int FACES_PER_CHUNK = 100000;

        private readonly ChunkInputMessage message;

        public ChunkInput(CloudPipeline pipeline, ChunkInputMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        private class ChunkData
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
            pipeline.LogInfo("started chunking input " + message.InputName);
            var input = TilingInput.Find(pipeline, projectName, message.InputName);
            if (input.Chunked)
            {
                pipeline.LogInfo("input " + message.InputName + " has already been chunked, skipping");
                pipeline.MasterQueue.Enqueue(message);
                return;
            }

            pipeline.LogInfo("downloading " + input.MeshUrl);
            Mesh mesh = null;
            pipeline.GetFile(input.MeshUrl, f =>
            {
                mesh = Mesh.Load(f);
                mesh.RemoveInvalidFaces();
                mesh.Clean();
            });
            Image image = null;
            string imageBaseUrl = null;
            if (input.ImageUrl != null)
            {
                pipeline.LogInfo("downloading " + input.ImageUrl);
                pipeline.GetFile(input.ImageUrl, f => image = Image.Load(f));
                input.ImageBands = image.Bands;
                input.ImageWidth = image.Width;
                input.ImageHeight = image.Height;

                pipeline.LogInfo("chunking image for input " + message.InputName);
                var sparseImage = new SparseCloudImage(image, pipeline, CHUNK_RESOLUTION);
                imageBaseUrl =  pipeline.GetStorageUrl("chunk", projectName, Guid.NewGuid().ToString());
                sparseImage.Save<byte>(imageBaseUrl, IMAGE_EXT);

            }
            pipeline.LogInfo("building acceleration structures to chunk input " + message.InputName);
            var multiClipper = new MultiMeshClipper();
            var dataset = new MultiMeshClipperInput(mesh, image);
            multiClipper.AddInput(dataset);

            pipeline.LogInfo("building mesh chunks for input " + message.InputName);
            var tilingScheme = new BinaryTreeTilingScheme();
            var root = TileLocalMesh.BuildBoundsTree(multiClipper, tilingScheme, new ITileSplitCriteria[] { new FaceSplitCriteria(FACES_PER_CHUNK) });
            
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
                    string meshUrl = pipeline.GetStorageUrl("chunk", projectName, id + MESH_EXT);
                    pipeline.SaveFile(f, meshUrl);
                    TilingInputChunk record = TilingInputChunk.Create(pipeline, id, meshUrl, imageBaseUrl, m.Bounds());
                    chunkIds.Add(id);
                    pipeline.LogInfo("generated chunk {0}/{1} for input {2}", chunkIds.Count(), leaves.Count, message.InputName);
                });
            });
            lock (input.ChunkIds)
            {
                input.ChunkIds.UnionWith(chunkIds);
            }
            input.Chunked = true;
            input.Save(pipeline);
            pipeline.MasterQueue.Enqueue(message);
            pipeline.LogInfo("completed chunking input " + message.InputName);
        }
    }
}
