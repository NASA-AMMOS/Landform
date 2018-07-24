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
            var inputs = TilingInput.Find(pipeline.DynamoContext, project).ToList();

            HashSet<string> ids = new HashSet<string>(this.message.TileIds);
            var leaves = TilingNode.Find(pipeline.DynamoContext, project).ToList().Where(n => ids.Contains(n.Id)).ToList();

            // Send completion messages for leaves that are already done
            foreach (var n in leaves)
            {
                if (n.MeshUrl != null)
                {
                    logger.Info(n.Id + " skipping");
                    pipeline.CompeltionQueue(project).Enqueue(new TileCompletedMessage(project.Name, n.Id));
                }
            }
            leaves = leaves.Where(n => n.MeshUrl == null).ToList();

            //logger.Info("Find overlapping chunks");
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
            //logger.Info("Downloading chunks: " + inputGroups.SelectMany(g=>g.Chunks).Count());

            // TODO: replace bakeclipper with textured mesh clipper
            var bakeClipper = new TileLocalMesh.TilingInput();
            foreach (var input in inputGroups)
            {
                foreach (var chunk in input.Chunks)
                {
                    Mesh m = null;
                    Image img = null;
                    TemporaryFile.GetAndDelete(Path.GetExtension(chunk.MeshUrl), f =>
                    {
                        pipeline.Storage.DownloadFile(chunk.MeshUrl, f);
                        m = Mesh.Load(f);

                    });
                    if (chunk.ImageUrl != null)
                    {
                        TemporaryFile.GetAndDelete(Path.GetExtension(chunk.ImageUrl), f =>
                        {
                            pipeline.Storage.DownloadFile(chunk.ImageUrl, f);
                            img = Image.Load(f);

                        });
                    }
                    //clipper.AddMeshImagePair(m, img);
                    bakeClipper.AddDataset(new TileLocalMesh.TilingInputDataset(m, img));
                }
            }
            bakeClipper.InitTextureBaker();
            //logger.Info("Make leaves");

            ConcurrentBag<TilingNode> processed = new ConcurrentBag<TilingNode>();
            Serial.ForEach(leaves, leaf =>
            {              
                var m = bakeClipper.Clip(leaf.GetBounds());
                var pair = bakeClipper.BakeTexture(m, project.TileResolution);
                leaf.SaveMesh(pair, pipeline, 0);
                processed.Add(leaf);
                pipeline.CompeltionQueue(project).Enqueue(new TileCompletedMessage(project.Name, leaf.Id));
                logger.Info(string.Format(leaf.Id + " generating from {0} chunks ({1}/{2})", inputGroups.SelectMany(g => g.Chunks).Count(), processed.Count(), leaves.Count));
            });
        }     
    }
}
