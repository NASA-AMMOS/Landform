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

        [Value(1, Required = true, HelpText = "Filename of mesh")]
        public string MeshFileapth { get; set; }

        [Value(2, Required = false, HelpText = "Filename of image")]
        public string ImageFileapth { get; set; }

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

        public int Run()
        {
            logger.Info("Load Mesh");
            Mesh mesh = Mesh.Load(options.MeshFileapth);
            logger.Info("Load Image");
            Image image = options.ImageFileapth == null ? null : Image.Load(options.ImageFileapth);

            ITileSplitCriteria splitCriteria = new FaceLimitSplitCriteria(options.FacesPerChunk);
            ITilingScheme tilingScheme = new BinaryTreeTilingScheme();

            // TODO: This could be faster by just creating a triangle list and partitioning later on
            logger.Info("Build Mesh Operator");
            MeshOperator op = new MeshOperator(mesh, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);

            logger.Info("Determine Chunk Bounds");
            Queue<BoundingBox> boundsToProcess = new Queue<BoundingBox>();
            boundsToProcess.Enqueue(op.Bounds);
            // Subdivide and define bounds of chunks
            List<TilingChunkRecord> chunkRecords = new List<TilingChunkRecord>();
            while(boundsToProcess.Count != 0 )
            {
                BoundingBox bounds = boundsToProcess.Dequeue();
                if(op.Empty(bounds))
                {
                    continue;
                }
                if(splitCriteria.ShouldSplit(op, bounds))
                {
                    foreach(var childBounds in tilingScheme.Split(op, bounds))
                    {
                        boundsToProcess.Enqueue(childBounds);
                    }
                }
                else
                {
                    string guid = Guid.NewGuid().ToString();
                    string meshChunkFile = Path.Combine(options.OutputDir, guid + ".ply");
                    string imageChunkFile = null;
                    if (image != null)
                    {
                        imageChunkFile = Path.Combine(options.OutputDir, guid + ".tif");
                    }
                    var record = new TilingChunkRecord(meshChunkFile, imageChunkFile, bounds);
                    chunkRecords.Add(record);
                }
            }
            TexturedMeshClipper texturedClipper = new TexturedMeshClipper();

            logger.Info("Create Chunks");
            // Cut out each chunk and save it
            Parallel.ForEach(chunkRecords, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount } , (record,pls, i) =>
            {
                logger.Info("Creating chunk: " + i + "/" + chunkRecords.Count);
                Mesh clippedMesh = op.Clip(record.Bounds);
                clippedMesh.Clean();
                if(image != null)
                {
                    var tmp = texturedClipper.ClipTexture(clippedMesh, image);
                    clippedMesh = tmp.Mesh;
                    // TODO: This should match the bit depth of the original input image - or maybe it doesnt matter if final tiles are byte
                    tmp.Image.Save<byte>(record.ImageFilename);
                }
                clippedMesh.Save(record.MeshFilename, record.ImageFilename);
            });
            // Write chunks to "database"
            foreach (var r in chunkRecords)
            {
                PretendTilingServerDatabase.Instance.ChunkTable.Add(r);
            }
            return 0;
        }
    }
}
