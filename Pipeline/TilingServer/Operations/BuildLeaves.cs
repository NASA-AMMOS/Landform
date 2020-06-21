using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using Microsoft.Xna.Framework;
using System.Collections.Concurrent;

namespace OPS.Pipeline.TilingServer
{

    public class BuildLeavesMessage : PipelineMessage
    {
        public List<string> TileIds;
        public BuildLeavesMessage() { }
        public BuildLeavesMessage(string projectName) : base(projectName) { }
    }

    public class BuildLeaves : PipelineOperation
    {
        private readonly BuildLeavesMessage message;

        public BuildLeaves(PipelineCore pipeline, BuildLeavesMessage message) : base(pipeline, message)
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
            LogLess("starting batch of {0} leaf tiles", message.TileIds.Count);

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
                    LogLess("leaf {0} already complete, skipping", n.Id);
                    pipeline.EnqueueToMaster(new TileCompletedMessage(projectName) { TileId = n.Id });
                }
            }

            // Filter any completed leaves
            leaves = leaves.Where(n => n.MeshUrl == null).ToList();
            if (leaves.Count == 0)
            {
                LogLess("all leaves in job already generated");
                return;
            }

            // Get a list of all chunks that overlap with a leaf tile
            LogLess("collecting input chunks per leaf");
            List<InputChunkGroup> inputGroups = new List<InputChunkGroup>();
            foreach (var inputName in project.LoadInputNames(pipeline))
            {
                var input = TilingInput.Find(pipeline, projectName, inputName);
                if (input == null)
                {
                    throw new Exception("tiling input not found: " + inputName);
                }
                var group = new InputChunkGroup() { Input = input };
                IEnumerable<string> chunks = null;
                lock (input.ChunkIds)
                {
                    chunks = input.ChunkIds.ToArray();
                }
                foreach (var chunkId in chunks)
                {
                    TilingInputChunk chunk = TilingInputChunk.Find(pipeline, chunkId);
                    bool anyIntersect = leaves.Any(leaf => leaf.GetBoundsChecked().Intersects(chunk.GetBounds()));
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
            LogLess("building acceleration datastructures");
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
                SparsePipelineImage image = null;
                string chunkBaseUrl = group.Chunks[0].ImageUrl;
                if (chunkBaseUrl != null)
                {
                    hasImages = true;
                    TilingInput ti = group.Input;
                    image = new SparsePipelineImage(pipeline, ti.ImageBands, ti.ImageWidth, ti.ImageHeight,
                                                    chunkBaseUrl, ChunkInput.IMAGE_EXT, ChunkInput.CHUNK_RESOLUTION);
                }
                bakeClipper.AddInput(mergedMesh, image);
            }
            bakeClipper.InitTextureBaker();

            LogLess("baking leaves");
            int nl = 0;
            Serial.ForEach(leaves, leaf =>
            {              
                LogLess("baking leaf {0} from {1} chunks ({2}/{3})",
                        leaf.Id, inputGroups.SelectMany(g => g.Chunks).Count(), ++nl, leaves.Count);

                var m = bakeClipper.Clip(leaf.GetBoundsChecked());

                var pair = new MeshImagePair(m, null);
                if (hasImages)
                {
                    pair = bakeClipper.BakeTexture(m, project.TileResolution, msg => LogLess(msg));
                }

                if (pair.Mesh != null)
                {
                    LogLess("saving leaf tile mesh");
                    leaf.SaveMesh(pair, pipeline, project);
                    leaf.Save(pipeline);
                }
                else
                {
                    LogError("failed to bake leaf");
                }

                pipeline.EnqueueToMaster(new TileCompletedMessage(projectName) { TileId = leaf.Id });
            });

            LogLess("batch completed, generated {0} leaf tiles", nl);
        }
    }
}
