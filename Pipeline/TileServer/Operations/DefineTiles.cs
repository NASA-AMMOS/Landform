using log4net;
using OPS.Geometry;
using OPS.Plumbing;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
            if(project == null)
            {
                logger.Info("No project found with name: " + message.ProjectName);
                return;
            }
            if(project.TilesDefined)
            {
                logger.Info("Tiles have already been defined for this project");
                pipeline.CompletionQueue.Enqueue(message);
                return;
            }

            SceneNode root = null;
            if (project.GetTilingScheme() == TilingScheme.UserDefined)
            {
                // Build a tree based on existing tile ids
                var inputs = TilingInput.Find(pipeline.DynamoContext, project.Name).ToList();
                ConcurrentBag<SceneNode> nodes = new ConcurrentBag<SceneNode>();
                Parallel.ForEach(inputs, new ParallelOptions() { MaxDegreeOfParallelism = 8 }, input =>
                {
                    var pair = DownloadInput(input);
                    if(!pair.Mesh.HasNormals)
                    {
                        pair.Mesh.GenerateVertexNormals();
                    }
                    pair.Mesh.RemoveInvalidFaces();
                    pair.Mesh.Clean();
                    var node = new SceneNode(input.TileId);
                    node.AddComponent(pair);
                    nodes.Add(node);
                });
                root = SceneNodeTilingExtensions.ConnectNodesByName(nodes.ToList());
                SceneNodeTilingExtensions.ComputeBounds(root);
            }
            else
            {
                // Buid a tree using input datasets
                var inputs = TilingInput.Find(pipeline.DynamoContext, project.Name).ToList();
                var tilingInput = new TileLocalMesh.TilingInput();
                foreach (var input in inputs)
                {
                    var pair = DownloadInput(input);
                    logger.Info("Building acceleration structures");
                    tilingInput.AddDataset(new TileLocalMesh.TilingInputDataset(pair.Mesh, pair.Image));
                }
                var projectScheme = project.GetTilingScheme();
                ITilingScheme scheme;
                if (projectScheme == TilingScheme.Bin)
                {
                    scheme = new BinaryTreeTilingScheme();
                }
                else if (projectScheme == TilingScheme.QuadX || projectScheme == TilingScheme.QuadY || projectScheme == TilingScheme.QuadZ)
                {
                    scheme = new QuadTreeTilingScheme(projectScheme);
                }
                else if (project.GetTilingScheme() == TilingScheme.Oct)
                {
                    scheme = new OctreeTilingScheme();
                }
                else
                {
                    throw new Exception("Unknonw tiling scheme");
                }
                ITileSplitCriteria splitCriteria = new FaceLimitSplitCriteria(project.FacesPerTile);

                logger.Info("Computing tile tree");
                root = TileLocalMesh.BuildBoundsTree(tilingInput, scheme, splitCriteria);
            }
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
                var tilingNode = TilingNode.Create(pipeline.DynamoContext, node.Name, project, null, null, parentId, childIds, dependencies.DependsOn(node.Name), dependencies.DependedOnBy(node.Name), node.GetComponent<NodeBounds>().Bounds);
                if(node.IsLeaf && node.HasComponent<MeshImagePair>())
                {
                    tilingNode.SaveMesh(node.GetComponent<MeshImagePair>(), pipeline, 0);
                }
            }                            
            project.TilesDefined = true;
            project.Save(pipeline.DynamoContext);
            pipeline.CompletionQueue.Enqueue(message);
        }

        MeshImagePair DownloadInput(TilingInput input)
        {
            logger.Info("Downloading: " + input.MeshUrl);
            Mesh mesh = null;
            TemporaryFile.GetAndDelete(Path.GetExtension(input.MeshUrl), f =>
            {
                pipeline.Storage(input.MeshUrl).DownloadFile(input.MeshUrl, f);
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
                    pipeline.Storage(input.ImageUrl).DownloadFile(input.ImageUrl, f);
                    image = Image.Load(f);
                });
            }
            return new MeshImagePair(mesh, image);
        }

    }
}
