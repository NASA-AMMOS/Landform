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

    public class BuildBakedLeavesMessage : TilingQueueMessage
    {
        public List<string> TileIds { get; set; }

        public BuildBakedLeavesMessage() { }

        public BuildBakedLeavesMessage(string projectName, List<string> tileIds) : base(projectName)
        {
            TileIds = tileIds;
        }
    }

    public class BuildBakedLeaves : TileServerOperation
    {
        static ILog logger = LogManager.GetLogger(typeof(BuildBakedLeaves));

        private BuildBakedLeavesMessage message;

        public BuildBakedLeaves(BuildBakedLeavesMessage message, PipelineCore pipeline, TileServerCloud cloud)
            : base(message.ProjectName, pipeline, cloud, logger)
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
            LogInfo("started batch of " + message.TileIds.Count + " leaf tiles");
            var project = TilingProject.Find(pipeline.DynamoContext, message.ProjectName);

            List<TilingNode> leaves = new List<TilingNode>();
            foreach(var id in message.TileIds)
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
            leaves = leaves.Where(n => n.MeshUrl == null).ToList();
            if(leaves.Count == 0)
            {
                LogInfo("all leaves in job already generated");
                return;
            }
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
            bool hasImages = false;
            var bakeClipper = new MultiMeshClipper();
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
                leaf.SaveMesh(pair, pipeline, 0);
                processed.Add(leaf);
                cloud.MasterQueue.Enqueue(new TileCompletedMessage(project.Name, leaf.Id));
                LogInfo(string.Format("generating leaf {0} from {1} chunks ({2}/{3})",
                                      leaf.Id, inputGroups.SelectMany(g => g.Chunks).Count(),
                                      processed.Count(), leaves.Count));
            });
            LogInfo("batch completed, generated " + processed.Count() + " leaf tiles");
        }
    }
}
