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

namespace OPS.Pipeline
{


    [Verb("chunkinputmesh", HelpText = "Subdivides an input mesh into managable sized peices")]
    public class ChunkInputDatasetOptions
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

    public class ChunkInputDataset
    {
        ChunkInputDatasetOptions options;
        public ChunkInputDataset(ChunkInputDatasetOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            Mesh mesh = Mesh.Load(options.MeshFileapth);
            Image image = options.ImageFileapth == null ? null : Image.Load(options.ImageFileapth);

            ITileSplitCriteria splitCriteria = new FaceLimitSplitCriteria(options.FacesPerChunk);
            ITilingScheme tilingScheme = new BinaryTreeTilingScheme();
            MeshOperator op = new MeshOperator(mesh, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
            
            Queue<BoundingBox> boundsToProcess = new Queue<BoundingBox>();
            boundsToProcess.Enqueue(op.Bounds);

            // Subdivide and define bounds of chunks
            List<TilingChunkRecord> chunkRecords = new List<TilingChunkRecord>();
            while(boundsToProcess.Count != 0 )
            {
                BoundingBox bounds = boundsToProcess.Dequeue();
                if(splitCriteria.ShouldSplit(op, bounds))
                {
                    foreach(var childBounds in tilingScheme.Split(op, bounds))
                    {
                        boundsToProcess.Enqueue(childBounds);
                    }
                }
                else
                {
                    var record = new TilingChunkRecord(Guid.NewGuid().ToString() + ".ply", image == null ? null : Guid.NewGuid().ToString() + ".tif", bounds);
                    chunkRecords.Add(record);
                }
            }
            // Cut out each chunk and save it
            Parallel.ForEach(chunkRecords, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount } , record =>
            {
                Mesh clippedMesh = op.Clip(record.Bounds);
                clippedMesh.Save(Path.Combine(options.OutputDir, record.MeshFilename));
            
            });
            // Write chunks to "database"
            PretendTilingServerDatabase.Instance.ChunkTable.AddRange(chunkRecords);
            return 0;
        }
    }
}
