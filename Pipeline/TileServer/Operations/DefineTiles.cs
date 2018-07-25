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
using Newtonsoft.Json;

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

        StartWorker pipeline;
        DefineTilesMessage message;

        class TileDependencyMapping
        {
            Dictionary<string, HashSet<string>> dependsOn = new Dictionary<string, HashSet<string>>();
            Dictionary<string, HashSet<string>> dependedOnBy = new Dictionary<string, HashSet<string>>();

            public HashSet<string> RequestedTiles = new HashSet<string>();

            public List<string> DependsOn(string id)
            {
                if (!dependsOn.ContainsKey(id))
                {
                    return new List<string>();
                }
                return dependsOn[id].ToList();
            }

            public List<string> DependedOnBy(string id)
            {
                if (!dependedOnBy.ContainsKey(id))
                {
                    return new List<string>();
                }
                return dependedOnBy[id].ToList();
            }

            public void AddDependency(string node, string dependency)
            {
                if (!dependsOn.ContainsKey(node))
                {
                    dependsOn.Add(node, new HashSet<string>());
                }
                dependsOn[node].Add(dependency);
                if (!dependedOnBy.ContainsKey(dependency))
                {
                    dependedOnBy.Add(dependency, new HashSet<string>());
                }
                dependedOnBy[dependency].Add(node);
            }
        }

        public DefineTiles(DefineTilesMessage message, StartWorker pipeline)
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
                pipeline.CompeltionQueue.Enqueue(this.message);
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
                        mesh.RemoveInvalidFaces();
                        mesh.Clean();
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
                // TODO: Add image size criteria, count up total area of texture space used by mesh uvs and multiply by factor to account for unsued atlas space as an estimate
                // This won't be prefect so leaf tile generator will still need to be able to split leaves to create more children if needed
                ITileSplitCriteria splitCriteria = new FaceLimitSplitCriteria(project.FacesPerTile);

                logger.Info("Computing tile tree");
                SceneNode root = TileLocalMesh.BuildBoundsTree(tilingInput, scheme, splitCriteria);

                // Compute tile dependencies
                var dependencies = new TileDependencyMapping();
                foreach (var node in root.DepthFirstTraverse())
                {
                    if (!node.IsLeaf)
                    {
                        foreach (var d in node.FindNodesRequiredForParent(root))
                        {
                            dependencies.AddDependency(node.Name, d.Name);
                        }
                    }
                }

                logger.Info("Saving tile tree");
                foreach (var node in root.DepthFirstTraverse())
                {
                    string parentId = node.Parent == null ? null : node.Parent.Name;
                    List<string> childIds = node.Children.Select(c => c.Name).ToList();
                    TilingNode.Create(pipeline.DynamoContext, node.Name, project, null, null, parentId, childIds, dependencies.DependsOn(node.Name), dependencies.DependedOnBy(node.Name), node.GetComponent<NodeBounds>().Bounds);
                }
                project.TilesDefined = true;
                project.Save(pipeline.DynamoContext);
                pipeline.CompeltionQueue.Enqueue(this.message);
            }
        }

    }
}
