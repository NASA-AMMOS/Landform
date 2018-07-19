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

        PipelineCore pipeline;
        BuildLeavesMessage message;

        public BuildLeaves(BuildLeavesMessage message, PipelineCore pipeline)
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
            logger.Info("Processing message");
            var project = TilingProject.Find(pipeline.DynamoContext, this.message.ProjectName);
            var inputs = TilingInput.Find(pipeline.DynamoContext, project).ToList();

            logger.Info("Get leaves");
            HashSet<string> ids = new HashSet<string>(this.message.TileIds);
            var leaves = TilingNode.Find(pipeline.DynamoContext, project).ToList().Where(n => ids.Contains(n.Id) && n.MeshUrl == null).ToList();

            logger.Info("Find overlapping chunks");
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
            logger.Info("Downloading chunks: " + inputGroups.SelectMany(g=>g.Chunks).Count());

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
            logger.Info("Make leaves");

            ConcurrentBag<TilingNode> processed = new ConcurrentBag<TilingNode>();
            Parallel.ForEach(leaves, leaf =>
            {
                string meshExt = ".ply";
                string imageExt = ".png";
                string id = Guid.NewGuid().ToString();
                string filenameBase = Path.Combine(TemporaryFile.TemporaryDirectory, id);
                string meshFilename = filenameBase + meshExt;
                string imageFilename = filenameBase + imageExt;

                string meshUrl = new Uri(Path.Combine(TileServerCloud.TileUrlBase, id + meshExt)).ToString();
                string imageUrl = new Uri(Path.Combine(TileServerCloud.TileUrlBase, id + imageExt)).ToString();

                var m = bakeClipper.Clip(leaf.GetBounds());
                var pair = bakeClipper.BakeTexture(m, project.TileResolution);
                m = pair.Mesh;

                // TODO: retain originial detail here
                pair.Image.Save<byte>(imageFilename);
                pipeline.Storage.UploadFile(imageFilename, imageUrl);

                m.Save(meshFilename, Path.GetFileName(imageFilename));
                pipeline.Storage.UploadFile(meshFilename, meshUrl);
                leaf.MeshUrl = meshUrl;
                leaf.ImageUrl = imageUrl;
                leaf.Save(pipeline.DynamoContext);
                // TODO: write and save gltf file (apply skirts if needed)
                if (File.Exists(meshFilename))
                {
                    File.Delete(meshFilename);
                }
                if (File.Exists(imageFilename))
                {
                    File.Delete(imageFilename);
                }
                processed.Add(leaf);
                logger.Info(string.Format("Tile: {0}/{1}", processed.Count(), leaves.Count));
            });

            logger.Info("Done");
        }
     
    }
}
