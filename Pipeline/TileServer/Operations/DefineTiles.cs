using log4net;
using OPS.Geometry;
using OPS.Plumbing;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Imaging;

namespace OPS.Pipeline.TileServer
{
    public class DefineTilesMessage : TilingQueueMessage
    {
        public DefineTilesMessage() { }

        public DefineTilesMessage(string projectName) : base(projectName)
        {
        }
    }

    public class DefineTiles
    {
        static ILog logger = LogManager.GetLogger(typeof(DefineTiles));

        PipelineCore pipeline;
        DefineTilesMessage message;

        public DefineTiles(DefineTilesMessage message, PipelineCore pipeline)
        {
            this.pipeline = pipeline;
            this.message = message;
        }

        public void Process()
        {
            logger.Info("Processing message");
            var project = TilingProject.Find(pipeline.DynamoContext, message.ProjectName);
            if(project.TilesDefined)
            {
                logger.Info("Tiles have already been defined for this project");
                return;
            }
            if(project.GetTilingScheme() == TilingScheme.UserDefined)
            {
                // Build a tree based on existing tile ids
                throw new NotImplementedException("");
            }
            else
            {
                // Buid a tree using input datasets
                var inputs = TilingInput.Find(pipeline.DynamoContext, project).ToList();
                // TODO: refactor reused code between this and TileLocalMesh
                var tilingInput = new TileLocalMesh.TilingInput();
                foreach (var input in inputs)
                {
                    logger.Info("Downloading: " + input.MeshUrl);
                    Mesh mesh = null;
                    TemporaryFile.GetAndDelete(Path.GetExtension(input.MeshUrl), f =>
                    {
                        pipeline.Storage.DownloadFile(input.MeshUrl, f);
                        mesh = Mesh.Load(f);
                    });
                    Image image = null;
                    if (input.ImageUrl != null)
                    {
                        logger.Info("Downloading: " + input.ImageUrl);
                        TemporaryFile.GetAndDelete(Path.GetExtension(input.ImageUrl), f =>
                        {
                            pipeline.Storage.DownloadFile(input.ImageUrl, f);
                            image = Image.Load(f);
                        });
                    }
                    logger.Info("Building acceleration structures");
                    tilingInput.AddDataset(new TileLocalMesh.TilingInputDataset(mesh, image));
                }
                ITilingScheme scheme;
                if (project.GetTilingScheme() == TilingScheme.Bin)
                {
                    scheme = new BinaryTreeTilingScheme();
                }
                else if (project.GetTilingScheme() == TilingScheme.Quad)
                {
                    scheme = new QuadTreeTilingScheme(project.GetSkirtMode());
                    
                }
                else if (project.GetTilingScheme() == TilingScheme.Oct)
                {
                    scheme = new OctreeTilingScheme();
                }
                else
                {
                    throw new Exception("Unknonw tiling scheme");
                }
                // TODO: Add image size criteria
                ITileSplitCriteria splitCriteria = new FaceLimitSplitCriteria(project.FacesPerTile);

                logger.Info("Computing tile tree");
                SceneNode root = TileLocalMesh.BuildBoundsTree(tilingInput, scheme, splitCriteria);
                logger.Info("Saving tile tree");
                foreach (var node in root.DepthFirstTraverse())
                {
                    string parentId = node.Parent == null ? null : node.Parent.Name;
                    List<string> childIds = node.Children.Select(c => c.Name).ToList();
                    TilingNode.Create(pipeline.DynamoContext, node.Name, project, null, null, parentId, childIds, node.GetComponent<NodeBounds>().Bounds);
                }
                project.TilesDefined = true;
                project.Save(pipeline.DynamoContext);
            }
        }

    }
}
