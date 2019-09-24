using log4net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Util;
using OPS.Geometry;

//TODO: refactor so that local codepath does not have cloud dependencies
//https://github.jpl.nasa.gov/OnSight/Landform/issues/596
using QueueMessage = OPS.Cloud.QueueMessage;

namespace OPS.Pipeline.TilingServer
{
    public class BuildTilesetJsonMessage : QueueMessage
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
            LogInfo("started");
            var project = TilingProject.Find(pipeline, projectName);
            LogInfo("building json");
            var root = TilingNode.BuildTreeFromDatabase(pipeline, project,
                                                        useBoundsWithSkirt: project.GetSkirtMode() != SkirtMode.None);
            // Only nodes with mesh image pairs will be marked as having content in the tile builder so add them
            // The meshes and images aren't actually used so we don't need to load them
            foreach(var n in root.DepthFirstTraverse())
            {
                n.AddComponent<MeshImagePair>();
            }
            var builder = new Tile3DBuilder(root);
            builder.BuildTileset(n => n.Name + TilingProject.ToExt(project.TilesetMeshFormat));
            string jsonData = JsonConvert.SerializeObject(builder.Tileset, Formatting.None);
            TemporaryFile.GetAndDelete(".json", f =>
            {
                File.WriteAllText(f, jsonData);
                string url = pipeline.GetStorageUrl(project.TilesetDir, projectName, "tileset.json");
                pipeline.SaveFile(f, url);
            });
            pipeline.EnqueueToMaster(this.message);
            LogInfo("completed");
        }
    }
}
