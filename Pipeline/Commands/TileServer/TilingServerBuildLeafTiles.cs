using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Imaging;
using log4net;
using CommandLine;
using OPS.Util;
using System.IO;
using RTree;

namespace OPS.Pipeline
{

    [Verb("tilingserverbuildleaftiles", HelpText = "Runs a simulated tiling server workflow locally")]
    public class TilingServerBuildLeafTilesOptions
    {
        [Value(0, Required = true, HelpText = "")]
        public string OutputDirectory { get; set; }

        [Value(1, Required = true, HelpText = "")]
        public IEnumerable<string> TileIds { get; set; }
    }

    public class TilingServerBuildLeafTiles
    {
        static ILog logger = LogManager.GetLogger(typeof(TilingServerBuildLeafTiles));
        TilingServerBuildLeafTilesOptions options;

        public TilingServerBuildLeafTiles(TilingServerBuildLeafTilesOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            PathHelper.EnsureExists(Path.Combine(options.OutputDirectory));
            logger.Info("Building " + options.TileIds.Count() + " leaf tiles");
            var database = PretendTilingServerDatabase.Instance;
            RTree<TilingChunkRecord> chunkTree = new RTree<TilingChunkRecord>(10, 5);
            foreach(var chunk in database.ChunkTable)
            {
                chunkTree.Add(chunk.Bounds.ToRectangle(), chunk);
            }
            HashSet<string> ids = new HashSet<string>();
            foreach (var i in options.TileIds)
            {
                ids.Add(i);
            }
            HashSet<TilingChunkRecord> requiredChunks = new HashSet<TilingChunkRecord>();
            foreach(var leaf in database.NodeTable)
            {
                if (ids.Contains(leaf.Id))
                {
                    var chunks = chunkTree.Intersects(leaf.Bounds.ToRectangle());
                    foreach (var c in chunks)
                    {
                        if (!requiredChunks.Contains(c))
                        {
                            requiredChunks.Add(c);
                        }
                    }
                }                
            }

            
            // We should clip the chunk from each dataset independently and then pack their textures together instead of using bake texture
            logger.Info("Chunks required: " + requiredChunks.Count);
            var tilingInput = new TileLocalMesh.TilingInput();
            foreach(var c in requiredChunks)
            {
                var dataset = new TileLocalMesh.TilingInputDataset(c.MeshFilename, c.ImageFilename);
                tilingInput.AddDataset(dataset);
            }
            tilingInput.InitTextureBaker();
            Parallel.ForEach(database.NodeTable, leaf =>
            {
                if (options.TileIds.Contains(leaf.Id))
                {
                    logger.Info("Building Leaf " + leaf.Id);
                    var clipped = tilingInput.Clip(leaf.Bounds);
                    var pair = tilingInput.BakeTexture(clipped, 256);
                    SceneNode node = new SceneNode(leaf.Id);
                    node.AddComponent(pair);
                    node.SaveMesh(options.OutputDirectory, "ply", "tif");
                }
            });
            return 0;
        }
    }
}
