using log4net;
using Newtonsoft.Json;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Pipeline.TileServer
{
    public class BuildTilesetJsonMessage : TilingQueueMessage
    {
        public BuildTilesetJsonMessage() { }

        public BuildTilesetJsonMessage(string projectName) : base(projectName) { }
    }

    public class BuildTilesetJson
    {

        static ILog logger = LogManager.GetLogger(typeof(BuildTilesetJson));

        StartWorker pipeline;
        BuildTilesetJsonMessage message;

        public BuildTilesetJson(BuildTilesetJsonMessage message, StartWorker pipeline)
        {
            this.pipeline = pipeline;
            this.message = message;
        }


        public void Process()
        {
            var project = TilingProject.Find(pipeline.DynamoContext, message.ProjectName);
            logger.Info("Building json: " + project.Name);
            var root = TilingNode.BuildTreeFromDatabase(pipeline.DynamoContext, project.Name);
            // Only nodes with mesh image pairs will be marked as having content in the tile builder so add them
            // The meshes and images aren't actually used so we don't need to load them
            foreach(var n in root.DepthFirstTraverse())
            {
                n.AddComponent<MeshImagePair>();
            }
            var builder = new Tile3DBuilder(root);
            builder.BuildTileset(n =>
            {
                return n.Name + ".b3dm";
            });
            string jsonData = JsonConvert.SerializeObject(builder.Tileset, Formatting.None);
            TemporaryFile.GetAndDelete(".json", f =>
            {
                File.WriteAllText(f, jsonData);
                string url = TileServerConfig.Instance.WWWUrl(project.Name, "tileset.json");
                pipeline.Storage(url).UploadFile(f, url);
            });
            pipeline.CompletionQueue.Enqueue(this.message);
        }
    }
}
