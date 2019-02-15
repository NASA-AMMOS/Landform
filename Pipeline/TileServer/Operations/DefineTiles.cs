using log4net;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OPS.Cloud;
using OPS.Util;
using OPS.Geometry;
using OPS.Imaging;

namespace OPS.Pipeline.TileServer
{
    public class DefineTilesMessage : QueueMessage
    {
        public DefineTilesMessage() { }
        public DefineTilesMessage(string projectName) : base(projectName) { }
    }

    public class DefineTiles : CloudPipelineOperation
    {
        private readonly DefineTilesMessage message;

        //TODO it may be possible to re-use this code in ProjectCache
        private class TileDependencyMapping
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

        public DefineTiles(CloudPipeline pipeline, DefineTilesMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public void Process()
        {
            LogInfo("started");
            var project = TilingProject.Find(pipeline, projectName);
            if(project == null)
            {
                LogError("project not found");
                return;
            }

            if (project.TilesDefined)
            {
                LogInfo("tiles already defined");
                pipeline.MasterQueue.Enqueue(message);
                return;
            }

            SceneNode root = null;
            if (project.GetTilingScheme() == TilingScheme.UserDefined)
            {
                // Build a tree based on existing tile ids
                var inputs = TilingInput.Find(pipeline, project).ToList();
                LogInfo("user-defined tiling scheme, " + inputs.Count + " inputs");
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
                var inputs = TilingInput.Find(pipeline, project).ToList();
                LogInfo(inputs.Count + " inputs");
                var multiClipper = new MultiMeshClipper();
                foreach (var input in inputs)
                {
                    var pair = DownloadInput(input);
                    LogInfo("building acceleration structures");
                    multiClipper.AddInput(new MultiMeshClipperInput(pair.Mesh, pair.Image));
                }
                var tilingScheme = project.GetTilingScheme();
                ITilingScheme scheme;
                if (tilingScheme == TilingScheme.Bin)
                {
                    scheme = new BinaryTreeTilingScheme();
                }
                else if (tilingScheme == TilingScheme.QuadX ||
                         tilingScheme == TilingScheme.QuadY ||
                         tilingScheme == TilingScheme.QuadZ)
                {
                    scheme = new QuadTreeTilingScheme(tilingScheme);
                }
                else if (project.GetTilingScheme() == TilingScheme.Oct)
                {
                    scheme = new OctreeTilingScheme();
                }
                else
                {
                    throw new Exception("unknown tiling scheme");
                }
                ITileSplitCriteria splitCriteria = new FaceSplitCriteria(project.FacesPerTile);

                LogInfo("computing tile tree");
                root = TileLocalMesh.BuildBoundsTree(multiClipper, scheme, splitCriteria);
            }

            var dependencies = new TileDependencyMapping();
            int nn = 0, n = 0;
            foreach (var node in root.DepthFirstTraverse())
            {
                nn++;
                if (!node.IsLeaf)
                {
                    foreach (var d in node.FindNodesRequiredForParent(root))
                    {
                        dependencies.AddDependency(node.Name, d.Name);
                    }
                }
            }

            LogInfo("saving tile tree, " + nn + " nodes");
            List<string> ids = new List<string>();
            foreach (var node in root.DepthFirstTraverse())
            {
                ids.Add(node.Name);
                string parentId = node.Parent == null ? null : node.Parent.Name;
                var tilingNode = TilingNode.Create(pipeline, node.Name, projectName,
                                                   null /* meshUrl */, null /* imageUrl */,
                                                   parentId,
                                                   dependencies.DependsOn(node.Name),
                                                   dependencies.DependedOnBy(node.Name),
                                                   node.GetComponent<NodeBounds>().Bounds);
                if (node.IsLeaf && node.HasComponent<MeshImagePair>())
                {
                    tilingNode.SaveMesh(node.GetComponent<MeshImagePair>(), pipeline, 0,
                                        project.ExportMeshFormat, project.ExportImageFormat);
                }
                Thread.Sleep(10); //throttle to reduce chance of exponential backoff
                if (++n % 500 == 0)
                {
                    LogInfo("created " + n + " nodes");
                }
            }                            
            project.SaveNodeIds(ids, pipeline);
            project.TilesDefined = true;
            project.Save(pipeline);
            pipeline.MasterQueue.Enqueue(message);
            LogInfo("complete");
        }

        private MeshImagePair DownloadInput(TilingInput input)
        {
            Mesh mesh = null;
            pipeline.GetFile(input.MeshUrl, f =>
            {
                mesh = Mesh.Load(f);
                mesh.RemoveInvalidFaces();
                mesh.Clean();
            });
            Image image = null;
            if (input.ImageUrl != null)
            {
                pipeline.GetFile(input.ImageUrl, f => image = Image.Load(f));
            }
            return new MeshImagePair(mesh, image);
        }

    }
}
