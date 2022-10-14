using System.Collections.Generic;
using JPLOPS.Geometry;

namespace JPLOPS.Pipeline.TilingServer
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
            var project = TilingProject.Find(pipeline, projectName);

            LogInfo("building tile tree from database");

            var tilingNodes = new Dictionary<string, TilingNode>();

            bool useBoundsWithSkirt = project.SkirtMode != SkirtMode.None;
            var root = TilingNode.BuildTreeFromDatabase(pipeline, project, useBoundsWithSkirt, tilingNodes);

            // Only nodes with mesh image pairs will be marked as having content in the tile builder
            // The meshes and images aren't actually used so we don't need to load them
            foreach(var n in root.DepthFirstTraverse())
            {
                if (tilingNodes[n.Name].MeshUrl != null)
                {
                    n.AddComponent<MeshImagePair>();
                }
            }

            string tsMeshExt = TilingProject.ToExt(project.TilesetMeshFormat);
            Tile3DBuilder.BuildAndSaveTileset(pipeline, root, project.TilesetDir, projectName,
                                              node => node.Name + tsMeshExt, project.RootTransform,
                                              info: msg => LogInfo(msg));

            pipeline.EnqueueToMaster(this.message);
        }
    }
}
