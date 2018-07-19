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

        const int FacesPerChunk = 250000;
        PipelineCore pipeline;
        ChunkInputMessage message;

        public ChunkInput(ChunkInputMessage message, PipelineCore pipeline)
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
                return;
            }

            logger.Info("Downloading: " + input.MeshUrl);
            Mesh mesh = null;
            TemporaryFile.GetAndDelete(Path.GetExtension(input.MeshUrl), f =>
            {
                pipeline.Storage.DownloadFile(input.MeshUrl, f);
                mesh = Mesh.Load(f);
            });
            Image image = null;
            if (input.ImageUrl != null)
            {
                logger.Info("Downloading: " + input.ImageUrl);
                TemporaryFile.GetAndDelete(Path.GetExtension(input.ImageUrl), f =>
                {
                    pipeline.Storage.DownloadFile(input.ImageUrl, f);
                    image = Image.Load(f);
                });
            }
            logger.Info("Building acceleration structures");
            MeshOperator op = new MeshOperator(mesh, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
            // TODO: migrate toward using sparse image so we don't need to know tile definitions
            logger.Info("Reading tile definitions");
            SceneNode root = TilingNode.BuildTreeFromDatabase(pipeline.DynamoContext, project);

            List<ChunkData> chunks = new List<ChunkData>();
            DefineChunks(root, op, new FaceLimitSplitCriteria(FacesPerChunk), chunks);

            TexturedMeshClipper clipper = new TexturedMeshClipper();
            clipper.AddMeshImagePair(op, image);

            ConcurrentBag<string> chunkIds = new ConcurrentBag<string>();
            Parallel.ForEach(chunks, (chunkData, pls, i) =>
            {
                Mesh m = null;
                string meshExt = ".ply";
                string imageExt = ".tif";
                string id = Guid.NewGuid().ToString();
                string filenameBase = Path.Combine(TemporaryFile.TemporaryDirectory, id);
                string meshFilename = filenameBase + meshExt;
                string imageFilename = null;

                string meshUrl = new Uri(Path.Combine(TileServerCloud.ChunkUrlBase, id + meshExt)).ToString();
                string imageUrl = null;
                if(image == null)
                {
                    m = op.Clip(chunkData.Bounds);
                    m.Clean();
                }
                else
                {
                    var pair = clipper.Clip(chunkData.Bounds);
                    m = pair.Mesh;
                    imageFilename = filenameBase + imageExt;
                    // TODO: retain originial detail here
                    pair.Image.Save<byte>(imageFilename);
                    imageUrl = new Uri(Path.Combine(TileServerCloud.ChunkUrlBase, id + imageExt)).ToString();
                    pipeline.Storage.UploadFile(imageFilename, imageUrl);
                }
                m.Save(meshFilename, Path.GetFileName(imageFilename));
                pipeline.Storage.UploadFile(meshFilename, meshUrl);
                TilingInputChunk record = TilingInputChunk.Create(pipeline.DynamoContext, id, project, meshUrl, imageUrl, m.Bounds());
                chunkIds.Add(id);
                if (File.Exists(meshFilename))
                {
                    File.Delete(meshFilename);
                }
                if (File.Exists(imageFilename))
                {
                    File.Delete(imageFilename);
                }
                logger.Info(string.Format("Chunk: {0}/{1}", chunkIds.Count(), chunks.Count));
            });
            input.ChunkIds = chunkIds.ToList();
            input.Chunked = true;
            input.Save(pipeline.DynamoContext);
            logger.Info("Done");
        }

        void DefineChunks(SceneNode node, MeshOperator op, ITileSplitCriteria splitCrieria, List<ChunkData> chunks)
        {
            var bounds = node.GetComponent<NodeBounds>().Bounds;
            if (node.IsLeaf || !splitCrieria.ShouldSplit(op, bounds))
            {
                // We are reached target detail or can't split anymore
                chunks.Add(new ChunkData(node.Name, bounds));
                return;
            }
            // Otherwise recurse
            foreach (var c in node.Children)
            {
                DefineChunks(c, op, splitCrieria, chunks);
            }            
        }
    }
}
