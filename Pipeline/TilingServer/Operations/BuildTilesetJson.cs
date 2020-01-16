using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using OPS.Geometry;
using OPS.Util;

namespace OPS.Pipeline.TilingServer
{
    public class BuildTilesetJsonMessage : PipelineMessage
    {
        public BuildTilesetJsonMessage() { }
        public BuildTilesetJsonMessage(string projectName) : base(projectName) { }
    }

    public class BuildTilesetJson : PipelineOperation
    {
        private readonly BuildTilesetJsonMessage message;

        public BuildTilesetJson(PipelineCore pipeline, BuildTilesetJsonMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public void Process()
        {
            LogInfo("building tile tree from database");

            var project = TilingProject.Find(pipeline, projectName);

            var tilingNodes = new Dictionary<string, TilingNode>();
            var root = TilingNode.BuildTreeFromDatabase(pipeline, project, project.SkirtMode != SkirtMode.None,
                                                        tilingNodes);

            // Only nodes with mesh image pairs will be marked as having content in the tile builder
            // The meshes and images aren't actually used so we don't need to load them
            foreach(var n in root.DepthFirstTraverse())
            {
                if (tilingNodes[n.Name].MeshUrl != null)
                {
                    n.AddComponent<MeshImagePair>();
                }
            }

            LogInfo("building tileset");
            var builder = new Tile3DBuilder(root);
            builder.BuildTileset(n => n.Name + TilingProject.ToExt(project.TilesetMeshFormat));

            LogInfo("saving tileset json");
            string jsonData = JsonConvert.SerializeObject(builder.Tileset, Formatting.None);
            TemporaryFile.GetAndDelete(".json", f =>
            {
                File.WriteAllText(f, jsonData);
                pipeline.SaveFile(f, pipeline.GetStorageUrl(project.TilesetDir, projectName, "tileset.json"));
            });

            //some GDS testcases are based on seeing tileset bounds size in log spew
            var rootSize = root.GetComponent<NodeBounds>().Bounds.Size();
            LogInfo("scene bounds (meters): {0:F3}x{1:F3}x{2:F3}", rootSize.X, rootSize.Y, rootSize.Z);

            LogInfo("saving tileset stats");
            var sb = new StringBuilder();
            root.DumpStats(msg =>
            {
                sb.Append(msg);
                sb.Append("\n");
                LogInfo(msg);
            });
            TemporaryFile.GetAndDelete(".txt", f =>
            {
                File.WriteAllText(f, sb.ToString());
                pipeline.SaveFile(f, pipeline.GetStorageUrl(project.TilesetDir, projectName, "stats.txt"));
            });

            pipeline.EnqueueToMaster(this.message);
        }
    }
}
