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

        static ILog logger = LogManager.GetLogger(typeof(DefineTiles));

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
            SceneNode root = TilingNode.BuildTreeFromDatabase(pipeline.DynamoContext, project);

            List<ChunkData> chunks = new List<ChunkData>();
            DefineChunks(root, op, new FaceLimitSplitCriteria(FacesPerChunk), chunks);


            Serial.ForEach(chunks, chunkData =>
            {
                if(image == null)
                {
                    Mesh m = op.Clip(chunkData.Bounds);

                } else
                {
                    var clipper = new TexturedMeshClipper();

                }
            });
            //Mesh clippedMesh = op.Clip(record.Bounds, true);


            input.Chunked = true;
            input.Save(pipeline.DynamoContext);
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
