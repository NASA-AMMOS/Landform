using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
using Microsoft.Xna.Framework;

namespace OPS.Pipeline.TileServer
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


        class LeafContext
        {
            public List<TilingChunkRecord> Chunks = new List<TilingChunkRecord>();
            public NodeRecord Node;

            public LeafContext(NodeRecord node)
            {
                this.Node = node;
            }

            
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
            List<LeafContext> contexts = database.NodeTable.Where(leaf => ids.Contains(leaf.Id)).Select(leaf => new LeafContext(leaf)).ToList();

            foreach(var leafContext in contexts)
            {
                var chunks = chunkTree.Intersects(leafContext.Node.Bounds.ToRectangle());
                var inputGroups = chunks.GroupBy(c => c.InputRecordId);
                foreach (var inputGroup in inputGroups)
                {
                    foreach (var c in inputGroup)
                    {
                        // If leaf is entirely contained within chunk
                        if (c.Bounds.FuzzyContains(leafContext.Node.Bounds))
                        {
                            // Record that we need to load this chunk     
                            if (!requiredChunks.Contains(c))
                            {
                                requiredChunks.Add(c);
                            }
                            // Add this chunk to our input and break because we only want one chunk per input
                            leafContext.Chunks.Add(c);
                            break;
                        }
                    }
                }
            }
            logger.Info("Chunks required: " + requiredChunks.Count);
            ConcurrentDictionary<TilingChunkRecord, TileLocalMesh.TilingInputDataset> chunkLookup = new ConcurrentDictionary<TilingChunkRecord, TileLocalMesh.TilingInputDataset>();
            Parallel.ForEach(requiredChunks, c =>
            {
                var dataset = new TileLocalMesh.TilingInputDataset(c.MeshFilename, c.ImageFilename);
                chunkLookup.TryAdd(c, dataset);
            });
            
            // We should clip the chunk from each dataset independently and then pack their textures together instead of using bake texture
            Serial.ForEach(contexts, leafContext =>
            {
                logger.Info("Building Leaf " + leafContext.Node.Id);
                var clipped = chunkLookup[leafContext.Chunks.First()].MeshOperator.Clip(leafContext.Node.Bounds);
                var pair = new MeshImagePair(clipped);
                //var pair = tilingInput.BakeTexture(clipped, 256);
                SceneNode node = new SceneNode(leafContext.Node.Id);
                node.AddComponent(pair);
                node.SaveMesh(options.OutputDirectory, "ply", "tif");
                
            });
            return 0;
        }
    }
}
