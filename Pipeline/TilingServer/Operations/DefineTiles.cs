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
                pipeline.EnqueueToMaster(message);
                return;
            }

            DownloadInputsAndBuildTree(project);

            pipeline.EnqueueToMaster(message);

            LogInfo("complete");
        }

        public void DownloadInputsAndBuildTree(TilingProject project, bool progress = true,
                                               bool skipSavingInternalTileMeshesForUserDefinedNodes = false)
        {
            int numUserDefinedNodes = 0;
            SceneNode root = null;
            if (project.GetTilingScheme() == TilingScheme.UserDefined)
            {
                // Build a tree based on existing tile ids
                var inputs = TilingInput.Find(pipeline, project).ToList();
                LogInfo("user-defined tiling scheme, {0} inputs", inputs.Count);
                var nodes = new List<SceneNode>();
                CoreLimitedParallel.ForEach(inputs, input =>
                {
                    MeshImagePair pair = DownloadInput(input);                  
                    var node = new SceneNode(input.TileId);
                    node.AddComponent(pair);
                    lock (nodes)
                    {
                        nodes.Add(node);   
                    }
                });
                numUserDefinedNodes = nodes.Count;
                LogInfo("loaded inputs, connecting {0} user defined nodes by name and adding mising parent nodes",
                        numUserDefinedNodes);
                root = SceneNodeTilingExtensions.ConnectNodesByName(nodes);
                LogInfo("computing bounds");
                SceneNodeTilingExtensions.ComputeBounds(root);
            }
            else
            {
                // Buid a tree using input datasets
                var inputs = TilingInput.Find(pipeline, project).ToList();
                LogInfo("tiling scheme {0}, {1} inputs", project.TilingScheme, inputs.Count);

                List<OPS.Pipeline.MeshImagePair> pairs = new List<MeshImagePair>();
                foreach (var input in inputs)
                {
                    pairs.Add(DownloadInput(input));
                }

                LogInfo("loaded inputs, building tree");
                root = BuildTileTreeFromInputs(pipeline, project.GetTilingScheme(), project.FacesPerTile, pairs, null,
                                               logPrefix);
            }

            LogInfo("computing node dependencies");
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

            LogInfo("saving tile tree{0}, {1} nodes",
                    numUserDefinedNodes > 0 ? " and converting user defined nodes" : "", nn);

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

                if (node.HasComponent<MeshImagePair>()) //user defined tile
                {
                    //save user supplied tile meshes to project storage
                    //this both saves them in our internal formats, typically ply / png
                    //as well as the final output tileset format, typically b3dm / jpg

                    var mp = node.GetComponent<MeshImagePair>();

                    //when we're called from LocalBuildTileset we just read the leaf tiles from InternalTileDir
                    //so no need to re-save them right back there
                    //though we do want to save corresponding b3dm files to TilesetDir
                    bool saveInternal = !skipSavingInternalTileMeshesForUserDefinedNodes;
                    if (!saveInternal && !string.IsNullOrEmpty(project.InternalTileDir))
                    {
                        if (mp.Mesh != null)
                        {
                            string file = node.Name + TilingProject.ToExt(project.InternalMeshFormat);
                            tilingNode.MeshUrl = pipeline.GetStorageUrl(project.InternalTileDir, project.Name, file);
                        }

                        if (mp.Image != null)
                        {
                            string file = node.Name + TilingProject.ToExt(project.InternalImageFormat);
                            tilingNode.ImageUrl = pipeline.GetStorageUrl(project.InternalTileDir, project.Name, file);
                        }

                        if (mp.Mesh != null || mp.Image != null)
                        {
                            tilingNode.Save(pipeline);
                        }
                    }

                    //geometric error is zero for user defined leaves
                    if (node.Transform.ChildCount == 0)
                    {
                        tilingNode.GeometricError = 0;
                        node.AddComponent<NodeGeometricError>(new NodeGeometricError(0));
                    }
                    //for user defined parent nodes geometric error will be computed in BuildParent

                    //will save tilingNode back to database including geometric error
                    tilingNode.SaveMesh(mp, pipeline, project, saveInternal);
                }

                if (pipeline is CloudPipeline)
                {
                    Thread.Sleep(10); //throttle to reduce chance of exponential backoff
                }

                if (progress && ++n % 500 == 0)
                {
                    LogInfo("created {0} nodes", n);
                }
            }

            project.SaveNodeIds(ids, pipeline);
            project.TilesDefined = true;
            project.Save(pipeline);
        }

        public static SceneNode BuildTileTreeFromInputs(PipelineCore pipeline, TilingScheme tilingScheme,
                                                        int facesPerTile, List<MeshImagePair> pairs,
                                                        SplitByTextureOpts texOpts = null, string logPrefix = null)
        {
            if (logPrefix == null)
            {
                logPrefix = "";
            }
            else if (!logPrefix.EndsWith(" "))
            {
                logPrefix += " ";
            }

            pipeline.LogInfo("{0}build tile tree: building mesh clipper", logPrefix);
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

            var splitCriteria = new List<ITileSplitCriteria> { new FaceSplitCriteria(facesPerTile) };

            if (texOpts != null)
            {
                splitCriteria.Add(new TextureSplitCriteria(texOpts));
            }

            pipeline.LogInfo("{0}build tile tree: building bounds tree", logPrefix);
            return BuildBoundsTree(multiClipper, scheme, splitCriteria.ToArray());
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
                if (input.ImageWidth < ChunkInput.CHUNK_RESOLUTION && input.ImageHeight < ChunkInput.CHUNK_RESOLUTION)
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
