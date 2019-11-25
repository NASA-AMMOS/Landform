using System.IO;
using Newtonsoft.Json;
using OPS.Geometry;
using OPS.Util;

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
            LogInfo("building tile tree from database");

            var project = TilingProject.Find(pipeline, projectName);

            var root = TilingNode.BuildTreeFromDatabase(pipeline, project,
                                                        useBoundsWithSkirt: project.GetSkirtMode() != SkirtMode.None);

            // Only nodes with mesh image pairs will be marked as having content in the tile builder so add them
            // The meshes and images aren't actually used so we don't need to load them
            foreach(var n in root.DepthFirstTraverse())
            {
                n.AddComponent<MeshImagePair>();
            }

            LogInfo("building tileset");
            var builder = new Tile3DBuilder(root);
            builder.BuildTileset(n => n.Name + TilingProject.ToExt(project.TilesetMeshFormat));

            LogInfo("serializing tileset json");
            string jsonData = JsonConvert.SerializeObject(builder.Tileset, Formatting.None);

            LogInfo("uploading tileset json");
            TemporaryFile.GetAndDelete(".json", f =>
            {
                File.WriteAllText(f, jsonData);
                string url = pipeline.GetStorageUrl(project.TilesetDir, projectName, "tileset.json");
                pipeline.SaveFile(f, url);
            });
            pipeline.EnqueueToMaster(this.message);
        }
    }
}
