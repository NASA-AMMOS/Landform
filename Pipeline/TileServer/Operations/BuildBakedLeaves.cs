using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Cloud;
using OPS.Imaging;
using OPS.Geometry;
using Microsoft.Xna.Framework;
using System.Collections.Concurrent;

namespace OPS.Pipeline.TileServer
{

    public class BuildBakedLeavesMessage : QueueMessage
    {
        public List<string> TileIds;
        public BuildBakedLeavesMessage() { }
        public BuildBakedLeavesMessage(string projectName) : base(projectName) { }
    }

    public class BuildBakedLeaves : CloudPipelineOperation
    {
        private readonly BuildBakedLeavesMessage message;

        public BuildBakedLeaves(CloudPipeline pipeline, BuildBakedLeavesMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        class InputChunkGroup
        {
            public TilingInput Input;
            public List<TilingInputChunk> Chunks = new List<TilingInputChunk>();
        }

        public void Process()
        {
            pipeline.LogInfo("started batch of " + message.TileIds.Count + " leaf tiles");
            var project = TilingProject.Find(pipeline, projectName);

            List<TilingNode> leaves = new List<TilingNode>();
            foreach(var id in message.TileIds)
            {
                leaves.Add(TilingNode.Find(pipeline, projectName, id));
            }
            // Send completion messages for leaves that are already done
            foreach (var n in leaves)
            {
                if (n.MeshUrl != null)
                {
                    pipeline.LogInfo("leaf " + n.Id + " already complete, skipping");
                    pipeline.MasterQueue.Enqueue(new TileCompletedMessage(projectName) { TileId = n.Id });
                }
            }
            // Filter any completed leaves
            leaves = leaves.Where(n => n.MeshUrl == null).ToList();
            if (leaves.Count == 0)
            {
                pipeline.LogInfo("all leaves in job already generated");
                return;
            }

            // Get a list of all chunks that overlap with a leaf tile
            var inputs = TilingInput.Find(pipeline, project).ToList();
            List<InputChunkGroup> inputGroups = new List<InputChunkGroup>();
            foreach (var input in inputs)
            {
                var group = new InputChunkGroup() { Input = input };
                IEnumerable<string> chunks = null;
                lock (input.ChunkIds)
                {
                    chunks = input.ChunkIds.ToArray();
                }
                foreach (var chunkId in chunks)
                {
                    TilingInputChunk chunk = TilingInputChunk.Find(pipeline, chunkId);
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

            // Reconstruct a mesh for each input using only the chunks that overlap with leaves that we are building
            bool hasImages = false;
            var bakeClipper = new MultiMeshClipper();
            foreach (var group in inputGroups)
            {
                var meshes = group.Chunks.Select(c =>
                {
                    Mesh m = null;
                    pipeline.GetFile(c.MeshUrl, f => m = Mesh.Load(f));
                    return m;
                });
                var mergedMesh = Mesh.Merge(meshes.ToArray());
                mergedMesh.Clean();
                SparseCloudImage image = null;
                string imgUrl = group.Chunks[0].ImageUrl;
                if (imgUrl != null)
                {
                    hasImages = true;
                    image = new SparseCloudImage(group.Input.ImageBands,
                                                 group.Input.ImageWidth, group.Input.ImageHeight,
                                                 imgUrl, ChunkInput.IMAGE_EXT,
                                                 pipeline, ChunkInput.CHUNK_RESOLUTION);
                }
                bakeClipper.AddInput(new MultiMeshClipperInput(mergedMesh, image));
            }
            bakeClipper.InitTextureBaker();

            ConcurrentBag<TilingNode> processed = new ConcurrentBag<TilingNode>();
            Serial.ForEach(leaves, leaf =>
            {              
                var m = bakeClipper.Clip(leaf.GetBounds());
                var pair = new MeshImagePair(m, null);
                if(hasImages)
                {
                    pair = bakeClipper.BakeTexture(m, project.TileResolution);
                }
                leaf.SaveMesh(pair, pipeline, 0, project.ExportMeshFormat, project.ExportImageFormat,
                              project.GetSkirtMode());
                processed.Add(leaf);
                pipeline.MasterQueue.Enqueue(new TileCompletedMessage(projectName) { TileId = leaf.Id });
                pipeline.LogInfo("generating leaf {0} from {1} chunks ({2}/{3})",
                                 leaf.Id, inputGroups.SelectMany(g => g.Chunks).Count(), processed.Count(), leaves.Count);
            });

            pipeline.LogInfo("batch completed, generated " + processed.Count() + " leaf tiles");
        }
    }
}
