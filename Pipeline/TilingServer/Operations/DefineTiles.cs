using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Util;

namespace OPS.Pipeline.TilingServer
{
    public class DefineTilesMessage : PipelineMessage
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
            var project = TilingProject.Find(pipeline, projectName);
            if (project == null)
            {
                throw new Exception("project not found");
            }

            if (project.TilesDefined)
            {
                LogInfo("tiles already defined");
                pipeline.EnqueueToMaster(message);
                return;
            }

            DownloadInputsAndBuildTree(project);

            pipeline.EnqueueToMaster(message);
        }

        public void DownloadInputsAndBuildTree(TilingProject project, bool progress = true,
                                               bool skipSavingInternalTileMeshesForUserDefinedNodes = false)
        {
            void spew(string what, int n, int chunk)
            {
                if (n % chunk == 0)
                {
                    var msg = string.Format("{0} {1} nodes", what, n);
                    if (progress)
                    {
                        LogLess(msg);
                    }
                    else
                    {
                        SendStatusToMaster(msg);
                    }
                }
            }

            List<TilingInput> loadInputs()
            {
                var inputNames = project.LoadInputNames(pipeline);
                LogInfo("{0} tiling inputs", inputNames.Count);

                var inputs = new List<TilingInput>();
                foreach (var inputName in inputNames)
                {
                    var input = TilingInput.Find(pipeline, project.Name, inputName);
                    if (input == null)
                    {
                        throw new Exception("tiling input not found: " + inputName);
                    }
                    inputs.Add(input);
                }
                return inputs;
            }

            SceneNode root = null;

            var tilingScheme = project.GetTilingScheme();
            bool userSupplied = tilingScheme == TilingScheme.UserDefined || tilingScheme == TilingScheme.Flat;

            var idToSceneNode = new Dictionary<string, SceneNode>();
            var idToTilingNode = new ConcurrentDictionary<string, TilingNode>();

            int numUserTiles = 0;
            if (userSupplied) // build a tree based on user supplied leaf tiles
            {
                // (user may or may not also have supplied some parent tiles)

                LogInfo("user defined tiling scheme");

                var inputs = loadInputs();
                foreach (var input in inputs)
                {
                    var id = input.TileId;
                    idToSceneNode[id] = new SceneNode(id);
                }

                numUserTiles = idToSceneNode.Count;

                LogInfo("connecting {0} user defined nodes by name and adding missing parent nodes", numUserTiles);

                if (tilingScheme == TilingScheme.UserDefined)
                {
                    root = SceneNodeTilingExtensions.ConnectNodesByName(idToSceneNode.Values);
                }
                else
                {
                    root = SceneNodeTilingExtensions.ConnectNodesToRoot(idToSceneNode.Values);
                }

                int n = 0;
                LogInfo("converting {0} user defined tiles", numUserTiles);
                CoreLimitedParallel.ForEach(inputs, input =>
                {
                    var id = input.TileId;
                    var sceneNode = idToSceneNode[id];

                    string parentId = sceneNode.Parent == null ? null : sceneNode.Parent.Name;
                    bool isLeaf = sceneNode.IsLeaf;
                    var tilingNode = TilingNode.Create(pipeline, id, project.Name, parentId, isLeaf, save: false);
                    idToTilingNode[id] = tilingNode;

                    //geometric error is zero for user defined leaves
                    if (sceneNode.IsLeaf)
                    {
                        sceneNode.AddComponent(new NodeGeometricError(0)); //will be propagated to tilingNode below
                    }

                    sceneNode.AddComponent<NodeBounds>();

                    tilingNode.MeshUrl = input.MeshUrl;
                    tilingNode.ImageUrl = input.ImageUrl;
                    tilingNode.IndexUrl = input.IndexUrl;

                    //don't add pair to sceneNode, would be a memory leak
                    var pair = tilingNode.LoadMeshImagePair(pipeline, cleanMesh: true);
                    if (pair != null)
                    {
                        sceneNode.GetComponent<NodeBounds>().Bounds = pair.Mesh.Bounds();
                        bool saveInternal = !skipSavingInternalTileMeshesForUserDefinedNodes;
                        tilingNode.SaveMesh(pair, pipeline, project, saveInternal);
                    }
                    else
                    {
                        pipeline.LogWarn("failed to load mesh for user defined tile {0}", id);
                    }

                    Interlocked.Increment(ref n);
                    spew("converted", n, 50);
                });
                LogInfo("computing tile tree bounds");
                SceneNodeTilingExtensions.ComputeBounds(root, useExistingLeafBounds: true);
            }
            else // automatically build all leaves and parents from one or more input meshes
            {
                LogInfo("tiling scheme {0}", project.TilingScheme);
                var inputs = loadInputs();
                var pairs = new List<MeshImagePair>();
                foreach (var input in inputs)
                {
                    pairs.Add(DownloadInput(input));
                }
                LogInfo("loaded {0} input meshes, building tree", inputs.Count);
                root = BuildTileTreeFromInputs(pipeline, tilingScheme, project.FacesPerTile, pairs, null, logPrefix);
            }

            LogInfo("computing tiling node dependencies");
            var dependencies = new TileDependencyMapping();
            foreach (var sceneNode in root.DepthFirstTraverse())
            {
                var id = sceneNode.Name;
                idToSceneNode[id] = sceneNode;
                if (!sceneNode.IsLeaf)
                {
                    foreach (var d in sceneNode.FindNodesRequiredForParent(root))
                    {
                        dependencies.AddDependency(id, d.Name);
                    }
                }
            }

            LogInfo("saving {0} tiling nodes to database", idToSceneNode.Count);

            var ids = new List<string>();
            int numSaved = 0;
            foreach (var sceneNode in root.DepthFirstTraverse())
            {
                var id = sceneNode.Name;
                ids.Add(id);

                TilingNode tilingNode = null;
                if (!idToTilingNode.ContainsKey(id))
                {
                    string parentId = sceneNode.Parent == null ? null : sceneNode.Parent.Name;
                    bool isLeaf = sceneNode.IsLeaf;
                    tilingNode = TilingNode.Create(pipeline, id, projectName, parentId, isLeaf, save: false);
                }
                else
                {
                    tilingNode = idToTilingNode[id];
                }

                tilingNode.SetDependsOn(dependencies.DependsOn(id));
                tilingNode.SetDependedOnBy(dependencies.DependedOnBy(id));
                if (sceneNode.HasComponent<NodeBounds>())
                {
                    tilingNode.SetBounds(sceneNode.GetComponent<NodeBounds>().Bounds);
                }

                if (sceneNode.HasComponent<NodeGeometricError>())
                {
                    tilingNode.GeometricError = sceneNode.GetComponent<NodeGeometricError>().Error;
                }
                else if (sceneNode.IsLeaf)
                {
                    tilingNode.GeometricError = 0;
                }

                tilingNode.Save(pipeline);

                if (pipeline is CloudPipeline)
                {
                    Thread.Sleep(10); //throttle to reduce chance of exponential backoff
                }

                spew("saved", ++numSaved, 500);
            }

            LogInfo("saving node IDs and project");
            project.SaveNodeIds(ids, pipeline);
            project.TilesDefined = true;
            project.Save(pipeline);
        }

        private static void EnsureLogPrefix(ref string logPrefix)
        {
            if (logPrefix == null)
            {
                logPrefix = "";
            }
            else if (!logPrefix.EndsWith(" "))
            {
                logPrefix += " ";
            }
        }

        public static SceneNode BuildTileTreeFromLODs(PipelineCore pipeline, TilingScheme tilingScheme,
                                                        List<Mesh> LODs, string logPrefix = null)
        {
            EnsureLogPrefix(ref logPrefix);

            ITilingScheme scheme = GetTilingScheme(tilingScheme);

            SceneNode root = BuildFixedLevelsBoundsTree(LODs, scheme, out int emptyTileCount);

            if(emptyTileCount > 0)
            {
                pipeline.LogInfo("{0} tiles were empty", emptyTileCount);
            }

            return root;
        }

        public static SceneNode BuildSingleLevelBoundsTree(List<Mesh> inputs)
        {
            if( inputs.Count < 1)
            {
                throw new InvalidDataException("expecting at least one mesh for node tree");
            }

            Mesh combined = Mesh.Merge(inputs.ToArray(),clean:false,normalize:false);

            //add root geometry so none will be created from children
            SceneNode root = new SceneNode("");
            root.Name = "";
            root.AddComponent(new NodeBounds(combined.Bounds()));
            root.AddComponent<MeshImagePair>(new MeshImagePair(inputs[0], null)); //TODO: use something lighter than first tile
            root.AddComponent<NodeGeometricError>(new NodeGeometricError(100));

            int counter = 1;
            foreach(var input in inputs)
            {
                SceneNode leaf = CreateChildNode(root, ref counter, input.Bounds());
                leaf.AddComponent<MeshImagePair>(new MeshImagePair(input, null));
                leaf.AddComponent<NodeGeometricError>(new NodeGeometricError(0));
            }

            root.Name = "root";
            return root;

        }
        /// <summary>
        /// if you have pre-defined LODs this function creates a tiletree that has a fixed depth matching
        /// the number of LODs you are expecting. it does not use any mesh or texture based split criteria
        /// </summary>
        /// <param name="LODPairs">expected sorted by decreasing quality (best first)</param>
        /// <returns></returns>
        public static SceneNode BuildFixedLevelsBoundsTree(List<Mesh> LODs, ITilingScheme scheme, out int emptyTileCount)
        {
            if (LODs.Count < 2)
            {
                throw new InvalidDataException("expecting > 1 LOD, received " + LODs.Count);
            }

            if (LODs.ElementAt(0).Vertices.Count < LODs.ElementAt(1).Vertices.Count)
            {
                throw new InvalidDataException("expecting LOD 0 to be higher number of verts than LOD 1");
            }

            // get maximum bounds across all the lod levels (it is possible LODs might have 
            // different bounds if decimation stretches or shrinks triangles
            BoundingBox rootBounds = LODs.First().Bounds();
            foreach (var lodMesh in LODs.Skip(1))
            {
                rootBounds = BoundingBox.CreateMerged(rootBounds, lodMesh.Bounds());
            }

            //add root
            SceneNode root = new SceneNode("");
            root.Name = "";
            root.AddComponent(new NodeBounds(rootBounds));

            //coarse to fine (coarsest is root)
            emptyTileCount = 0;
            List<SceneNode> previousLevelNodes = new List<SceneNode> { root };
            foreach (var lodMesh in LODs.Reverse<Mesh>().Skip(1))
            {
                MeshOperator meshOp = new MeshOperator(lodMesh, buildFaceTree: true, buildVertexTree: false, buildUVFaceTree: false);
                List<SceneNode> currentLevelNodes = new List<SceneNode>();
                foreach (var parentNode in previousLevelNodes)
                {                    
                    var parentBounds = parentNode.GetComponent<NodeBounds>().Bounds;

                    var childrenBounds = scheme.Split(null, parentBounds);
                    int counter = 0; //note this is always exactly one decimal digit
                    foreach (var childBounds in childrenBounds)
                    {
                        if (!meshOp.Empty(childBounds))
                        {
                            currentLevelNodes.Add(CreateChildNode(parentNode, ref counter, childBounds));                           
                        }
                        else
                        {
                            emptyTileCount++;
                        }
                    }
                }

                previousLevelNodes = currentLevelNodes;
            }

            root.Name = "root";
            return root;
        }

        public static SceneNode BuildTileTreeFromInputs(PipelineCore pipeline, TilingScheme tilingScheme,
                                                        int facesPerTile, List<MeshImagePair> pairs,
                                                        SplitByTextureOpts texOpts = null, string logPrefix = null,
                                                        double surfaceExtent = -1)
        {
            EnsureLogPrefix(ref logPrefix);

            pipeline.LogInfo("{0}build tile tree: building mesh clipper", logPrefix);
            var multiClipper = new MultiMeshClipper();
            foreach (var pair in pairs)
            {
                multiClipper.AddInput(new MultiMeshClipperInput(pair.Mesh, pair.Image));
            }

            ITilingScheme scheme = GetTilingScheme(tilingScheme);

            var splitCriteria = new List<ITileSplitCriteria> { new FaceSplitCriteria(facesPerTile) };

            Action<string> info = null;
            if (texOpts != null)
            {
                if (texOpts.useApproximateTileSplit)
                {
                    splitCriteria.Add(new TextureSplitCriteriaApproximate(texOpts));
                }
                else
                {
                    splitCriteria.Add(new TextureSplitCriteriaBackproject(texOpts));
                    info = msg => pipeline.LogInfo(msg);
                }
               
            }

            pipeline.LogInfo("{0}build tile tree: building bounds tree, max {1} faces per tile, {2} split criteria, " +
                             "texture split {3}", logPrefix, Fmt.KMG(facesPerTile), splitCriteria.Count,
                             splitCriteria.Any(sc => sc is TextureSplitCriteria) ? "enabled" : "disabled");

            return BuildBoundsTree(multiClipper, scheme, splitCriteria.ToArray(), surfaceExtent, info);
        }

        private static ITilingScheme GetTilingScheme(TilingScheme tilingScheme)
        {
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

            return scheme;
        }

        //each node name is of the form ABCDE... where
        //A is the index of a child of the root
        //B is the index of a child of the node corresponding to A, etc
        //thus each node name encodes a full path from the root to the node
        //and the collection of all leaf names encodes the full tree topology
        public static SceneNode BuildBoundsTree(MultiMeshClipper multiClipper, ITilingScheme tilingScheme,
                                                ITileSplitCriteria[] splitCriteria, double surfaceExtent = -1,
                                                Action<string> infoAction = null)
        {
            var info = infoAction ?? (msg => { });

            var totalBounds = multiClipper.TotalBounds;

            //if surfaceExtent is negative then treat the whole scene like surface
            //if surfaceExtent is zero then treat the whole scene like orbital
            BoundingBox? surfaceBounds = null;
            var orbitalSplitCriteria = splitCriteria.Where(sc => sc is FaceSplitCriteria).ToArray();
            if (surfaceExtent > 0)
            {
                double rad = 0.5 * surfaceExtent;
                surfaceBounds = new BoundingBox(new Vector3(-rad, -rad, totalBounds.Min.Z),
                                                new Vector3(rad, rad, totalBounds.Max.Z));
            }

            SceneNode root = new SceneNode("");
            root.AddComponent(new NodeBounds(totalBounds));
            Queue<SceneNode> queue = new Queue<SceneNode>();
            queue.Enqueue(root);

            int tilesComplete = 0;
            int surfaceTiles = 0, orbitalTiles = 0, surfaceSplits = 0, orbitalSplits = 0;
            while (queue.Count > 0)
            {
                List<SceneNode> toProcess = new List<SceneNode>(queue.Count());
                while (queue.Count() > 0)
                {
                    toProcess.Add(queue.Dequeue());
                }

                CoreLimitedParallel.ForEach(toProcess, cur =>
                {
                    var curBounds = cur.GetComponent<NodeBounds>().Bounds;

                    var sc = splitCriteria;
                    if (surfaceExtent == 0 || (surfaceBounds.HasValue && !surfaceBounds.Value.Intersects(curBounds)))
                    {
                        sc = orbitalSplitCriteria;
                        Interlocked.Increment(ref orbitalTiles);
                    }
                    else
                    {
                        Interlocked.Increment(ref surfaceTiles);
                    }
                    if (sc.Length > 0 && sc.Any(splitCrit => multiClipper.ShouldSplit(splitCrit, curBounds)))
                    {
                        if (sc == orbitalSplitCriteria)
                        {
                            Interlocked.Increment(ref orbitalSplits);
                        }
                        else
                        {
                            Interlocked.Increment(ref surfaceSplits);
                        }
                        info(string.Format("splitting tile: {0}", cur.Name));
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
                            SceneNode child = CreateChildNode(cur, ref counter, childBound);
                            
                            lock (queue)
                            {
                                queue.Enqueue(child);
                            }
                            
                        }
                    }
                    else
                    {
                        info(string.Format("not Splitting tile: {0} ({1})",
                                           cur.Name, Interlocked.Increment(ref tilesComplete)));
                    }
                });
            }
            info($"split {surfaceSplits}/{surfaceTiles} surface tiles, {orbitalSplits}/{orbitalTiles} orbital");
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

        private static SceneNode CreateChildNode(SceneNode cur, ref int counter, BoundingBox childBounds)
        {
            SceneNode child = new SceneNode(cur.Name + counter, cur.Transform);
            counter++;

            child.AddComponent(new NodeBounds(childBounds));
            return child;
        }
    }
}
