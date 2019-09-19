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
using OPS.Util;
using OPS.Geometry;
using OPS.Imaging;
using Microsoft.Xna.Framework;

//TODO: refactor so that local codepath does not have cloud dependencies
//https://github.jpl.nasa.gov/OnSight/Landform/issues/596
using QueueMessage = OPS.Cloud.QueueMessage;

namespace OPS.Pipeline.TilingServer
{
    public class DefineTilesMessage : QueueMessage
    {
        public DefineTilesMessage() { }
        public DefineTilesMessage(string projectName) : base(projectName) { }
    }

    public class DefineTiles : PipelineOperation
    {
        private readonly DefineTilesMessage message;

        //TODO it may be possible to re-use this code in ProjectCache
        //https://github.jpl.nasa.gov/OnSight/Landform/issues/428
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

        public DefineTiles(PipelineCore pipeline, DefineTilesMessage message) : base(pipeline, message)
        {
            this.message = message;
        }

        public void Process()
        {
            pipeline.LogInfo("started");
            var project = TilingProject.Find(pipeline, projectName);
            if(project == null)
            {
                pipeline.LogError("project not found");
                return;
            }

            if (project.TilesDefined)
            {
                pipeline.LogInfo("tiles already defined");
                pipeline.EnqueueToMaster(message);
                return;
            }

            SceneNode root = null;
            if (project.GetTilingScheme() == TilingScheme.UserDefined)
            {
                // Build a tree based on existing tile ids
                var inputs = TilingInput.Find(pipeline, project).ToList();
                pipeline.LogInfo("user-defined tiling scheme, " + inputs.Count + " inputs");
                ConcurrentBag<SceneNode> nodes = new ConcurrentBag<SceneNode>();
                CoreLimitedParallel.ForEach(inputs, input =>
                {
                    MeshImagePair pair = DownloadInput(input);                  
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
                pipeline.LogInfo(inputs.Count + " inputs");

                List<OPS.Pipeline.MeshImagePair> pairs = new List<MeshImagePair>();
                foreach (var input in inputs)
                {
                    pairs.Add(DownloadInput(input));
                }

                root = BuildTileTreeFromInputs(pipeline, project.GetTilingScheme(), project.FacesPerTile, pairs);
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

            pipeline.LogInfo("saving tile tree, " + nn + " nodes");
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
                    pipeline.LogInfo("created " + n + " nodes");
                }
            }
            project.SaveNodeIds(ids, pipeline);
            project.TilesDefined = true;
            project.Save(pipeline);
            pipeline.EnqueueToMaster(message);
            pipeline.LogInfo("complete");
        }

        
        public static SceneNode BuildTileTreeFromInputs(PipelineCore pipeline, TilingScheme tilingScheme,
                                                        int facesPerTile, List<MeshImagePair> pairs,
                                                        SplitByTextureOpts texOpts = null )
        {
            SceneNode root;
            pipeline.LogInfo("building acceleration structures");
            var multiClipper = new MultiMeshClipper();
            foreach (var pair in pairs)
            {
                multiClipper.AddInput(new MultiMeshClipperInput(pair.Mesh, pair.Image));
            }

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
            else if (tilingScheme == TilingScheme.Oct)
            {
                scheme = new OctreeTilingScheme();
            }
            else
            {
                throw new Exception("unknown tiling scheme");
            }

            pipeline.LogInfo("computing tile tree");

            List<ITileSplitCriteria> splitCriteria =
                new List<ITileSplitCriteria> { new FaceSplitCriteria(facesPerTile) };

            if (texOpts != null)
                splitCriteria.Add(new TextureSplitCriteria(texOpts));

            root = BuildBoundsTree(multiClipper, scheme, splitCriteria.ToArray());
            return root;
        }

        //each node name is of the form ABCDE... where
        //A is the index of a child of the root
        //B is the index of a child of the node corresponding to A, etc
        //thus each node name encodes a full path from the root to the node
        //and the collection of all leaf names encodes the full tree topology
        public static SceneNode BuildBoundsTree(MultiMeshClipper multiClipper, ITilingScheme tilingScheme,
                                                ITileSplitCriteria[] splitCriteria)
        {
            SceneNode root = new SceneNode("");
            root.AddComponent(new NodeBounds(multiClipper.TotalBounds));
            Queue<SceneNode> queue = new Queue<SceneNode>();
            queue.Enqueue(root);

            int cores = CoreLimitedParallel.GetAvailableCores();
            while (queue.Count > 0 )
            {
                List<SceneNode> toProcess = new List<SceneNode>();
                for(int idx=0; idx < cores && queue.Count() > 0; idx++)
                {                
                    toProcess.Add(queue.Dequeue());
                }

                CoreLimitedParallel.ForEach(toProcess, cur =>
                {
                    var curBounds = cur.GetComponent<NodeBounds>().Bounds;
                    if (splitCriteria.Any(splitCrit => multiClipper.ShouldSplit(splitCrit, curBounds)))
                    {
                        var childBounds = tilingScheme.Split(null, curBounds);
                        childBounds = multiClipper.FilterEmptyBounds(childBounds);

                        //For quad trees, expand bounds in the non-split dimension
                        //Otherwise, we clip high peaks/low valleys in the decimated mesh
                        //that exceed the bounds of the original mesh
                        //childBounds = childBounds.Select(box => tilingScheme.ExpandBounds(box, null));
                        //disabled - see https://github.jpl.nasa.gov/OnSight/Landform/pull/656

                        int counter = 0; //note this is always exactly one decimal digit
                        foreach (var childBound in childBounds)
                        {
                            SceneNode child = new SceneNode(cur.Name + counter, cur.Transform);
                            child.AddComponent(new NodeBounds(childBound));
                            lock (queue)
                            {
                                queue.Enqueue(child);
                            }
                            counter++;
                        }
                    }
                });
            }
            root.Name = "root";
            return root;
        }

        private MeshImagePair DownloadInput(TilingInput input)
        {
            Image image = null;
            if (input.ImageUrl != null)
            {
                if (image.Width < ChunkInput.CHUNK_RESOLUTION && image.Height < ChunkInput.CHUNK_RESOLUTION)
                {
                    image = pipeline.LoadImage(input.ImageUrl);
                }
                else
                {
                    image = new SparsePipelineImage(pipeline, input.ImageUrl, ChunkInput.CHUNK_RESOLUTION);
                }
            }
            Mesh mesh = null;
            pipeline.GetFile(input.MeshUrl, f =>
            {
                mesh = Mesh.Load(f);
                if (!mesh.HasNormals)
                {
                    mesh.GenerateVertexNormals();
                }
                mesh.RemoveInvalidFaces();
                mesh.Clean();
            });

            return new MeshImagePair(mesh, image);
        }
    }
}
