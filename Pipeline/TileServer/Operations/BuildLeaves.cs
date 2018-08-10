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

    public class TileCompletedMessage : TilingQueueMessage
    {
        public string TileId;

        public TileCompletedMessage(string projectName, string id) : base(projectName)
        {
            this.TileId = id;
        }
    }

    public class BuildLeavesMessage : TilingQueueMessage
    {
        public List<string> TileIds { get; set; }

        public BuildLeavesMessage() { }

        public BuildLeavesMessage(string projectName, List<string> tileIds) : base(projectName)
        {
            this.TileIds = tileIds;
        }
    }

    public class BuildLeaves
    {

        static ILog logger = LogManager.GetLogger(typeof(BuildLeaves));

        StartWorker pipeline;
        BuildLeavesMessage message;

        public BuildLeaves(BuildLeavesMessage message, StartWorker pipeline)
        {
            this.pipeline = pipeline;
            this.message = message;
        }


        class InputChunkGroup
        {
            public TilingInput Input;
            public List<TilingInputChunk> Chunks = new List<TilingInputChunk>();
        }

        public void Process()
        {
            var project = TilingProject.Find(pipeline.DynamoContext, this.message.ProjectName);

            List<TilingNode> leaves = new List<TilingNode>();
            foreach(var id in this.message.TileIds)
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
            leaves = leaves.Where(n => n.MeshUrl == null).ToList();
            // Get a list of all chunks that overlap with a leaf tile
            var inputs = TilingInput.Find(pipeline.DynamoContext, project).ToList();
            List<InputChunkGroup> inputGroups = new List<InputChunkGroup>();
            foreach (var input in inputs)
            {
                var group = new InputChunkGroup() { Input = input };
                foreach (var chunkId in input.ChunkIds)
                {
                    TilingInputChunk chunk = TilingInputChunk.Find(pipeline.DynamoContext, chunkId);
                    bool anyIntersect = leaves.Any(leaf => leaf.GetBounds().Intersects(chunk.GetBounds()));
                    if (anyIntersect)
                    {
                        group.Chunks.Add(chunk);
                    }
                }
                if (group.Chunks.Count > 0)
                {
                    inputGroups.Add(group);
                }
            }

            var bakeClipper = new TileLocalMesh.TilingInput();
            foreach (var group in inputGroups)
            {
                // Reconstruct a mesh for each input using only the chunks that overlap with leaves that we are building
                var meshes = group.Chunks.Select(c =>
                {
                    Mesh m = null;
                    TemporaryFile.GetAndDelete(Path.GetExtension(c.MeshUrl), f =>
                    {
                        pipeline.Storage(c.MeshUrl).DownloadFile(c.MeshUrl, f);
                        m = Mesh.Load(f);
                    });
                    return m;
                });
                var mergedMesh = Mesh.Merge(meshes.ToArray());
                mergedMesh.Clean();
                SparseCloudImage image = null;
                string imgUrl = group.Chunks[0].ImageUrl;
                if (imgUrl != null)
                {
                    image = new SparseCloudImage(group.Input.ImageBands, group.Input.ImageWidth, group.Input.ImageHeight, imgUrl, ChunkInput.IMAGE_EXT, this.pipeline, ChunkInput.CHUNK_RESOLUTION);
                }
                bakeClipper.AddDataset(new TileLocalMesh.TilingInputDataset(mergedMesh, image));
            }
            bakeClipper.InitTextureBaker();

            ConcurrentBag<TilingNode> processed = new ConcurrentBag<TilingNode>();
            Serial.ForEach(leaves, leaf =>
            {              
                var m = bakeClipper.Clip(leaf.GetBounds());
                var pair = bakeClipper.BakeTexture(m, project.TileResolution);
                leaf.SaveMesh(pair, pipeline, 0);
                processed.Add(leaf);
                pipeline.CompletionQueue.Enqueue(new TileCompletedMessage(project.Name, leaf.Id));
                logger.Info(string.Format(leaf.Id + " generating from {0} chunks ({1}/{2})", inputGroups.SelectMany(g => g.Chunks).Count(), processed.Count(), leaves.Count));
            });
        }     
    }
}
