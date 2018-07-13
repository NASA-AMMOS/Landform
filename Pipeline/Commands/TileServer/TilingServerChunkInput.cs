using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using CommandLine;
using OPS.Util;
using OPS.Imaging;
using OPS.Geometry;
using System.IO;
using log4net;

namespace OPS.Pipeline
{


    [Verb("tilingserverchunkinput", HelpText = "Subdivides an input mesh into managable sized peices")]
    public class TilingServerChunkInputOptions
    {
        [Value(0, Required = true, HelpText = "Output directory")]
        public string OutputDir { get; set; }

        [Value(1, Required = true, HelpText = "Input dataset database id")]
        public int InputId { get; set; }

        [Option(HelpText = "Target number of faces per chunk ", Default = 250000)]
        public int FacesPerChunk { get; set; }
    }

    public class TilingServerChunkInput
    {
        static ILog logger = LogManager.GetLogger(typeof(TilingServerChunkInput));


        TilingServerChunkInputOptions options;
        public TilingServerChunkInput(TilingServerChunkInputOptions options)
        {
            this.options = options;
        }


        class ChunkData
        {
            public SceneNode Node;
            public List<Triangle> Triangles = new List<Triangle>();

            public ChunkData() { }
            public ChunkData(SceneNode node, List<Triangle> triangles)
            {
                this.Node = node;
                this.Triangles = triangles;
            }

            public ChunkData(SceneNode node)
            {
                this.Node = node;
            }
        }

        public int Run()
        {
            var database = PretendTilingServerDatabase.Instance;
            var input = database.InputTable.Where(r => r.Id == options.InputId).First();
            logger.Info("Load Mesh");
            Mesh mesh = Mesh.Load(input.MeshFilename);
            logger.Info("Load Image");
            Image image = input.ImageFilename == null ? null : Image.Load(input.ImageFilename);

            logger.Info("Determine Chunk Bounds");
            // TODO: find a better centralized home for this method
            SceneNode root = TilingServerWorkflow.BuildTreeFromNodeRecords();
            Queue<ChunkData> processing = new Queue<ChunkData>();
            processing.Enqueue(new ChunkData(root, mesh.Triangles()));

            // TODO: would MeshOperator be faster than the iteration we are doing here?
            // MeshOperator op = new MeshOperator(mesh, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
            // Mesh clippedMesh = op.Clip(record.Bounds, true);
            List<ChunkData> finalChunks = new List<ChunkData>();
            while(processing.Count != 0)
            {
                var cur = processing.Dequeue();
                if(cur.Triangles.Count <= options.FacesPerChunk || cur.Node.IsLeaf)
                {
                    finalChunks.Add(cur);
                    continue;
                }
                // Split
                foreach (var c in cur.Node.Children)
                {
                    var childData = new ChunkData(c);
                    var bounds = c.GetComponent<NodeBounds>().Bounds;
                    bounds = BoundingBoxExtensions.Scale(bounds, 1.1f);
                    foreach (var t in cur.Triangles)
                    {
                        if(t.Intersects(bounds))
                        {
                            childData.Triangles.Add(t);
                        }
                    }
                    processing.Enqueue(childData);
                }
                cur.Triangles = null;
            }
            logger.Info("Create Chunks");
            TexturedMeshClipper texturedClipper = new TexturedMeshClipper();
            Parallel.ForEach(finalChunks, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount }, (chunk, pls, i) =>
            {
                logger.Info("Creating chunk: " + i + "/" + finalChunks.Count);
                string guid = Guid.NewGuid().ToString();
                var record = new TilingChunkRecord();
                record.InputRecordId = options.InputId;
                record.MeshFilename = Path.Combine(options.OutputDir, guid + ".ply");
                Mesh m = new Mesh(chunk.Triangles, mesh.HasNormals, mesh.HasUVs, mesh.HasColors);
                record.Bounds = m.Bounds();
                if (image != null)
                {
                    var tmp = texturedClipper.ClipTexture(m, image);
                    m = tmp.Mesh;
                    record.ImageFilename = Path.Combine(options.OutputDir, guid + ".tif");
                    // TODO: This should match the bit depth of the original input image - or maybe it doesnt matter if final tiles are byte
                    tmp.Image.Save<byte>(record.ImageFilename);
                }
                m.Save(record.MeshFilename, record.ImageFilename);
                PretendTilingServerDatabase.Instance.ChunkTable.Add(record);
            });        
            return 0;
        }
    }
}
