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

            var idToSceneNode = new Dictionary<string, SceneNode>();
            var idToTilingNode = new ConcurrentDictionary<string, TilingNode>();

            int numUserTiles = 0;
            if (TilingSchemeBase.IsUserProvided(tilingScheme)) // build a tree based on user supplied leaf tiles
            {
                // (user may or may not also have supplied some parent tiles)

                LogInfo("user defined tiling scheme");

                var inputs = loadInputs();
                foreach (var input in inputs)
                {
                    var id = input.TileId;
                    idToSceneNode[id] = new SceneNode(id);
                }
                var sceneNodes = idToSceneNode.Values;

                numUserTiles = idToSceneNode.Count;

                LogInfo("connecting {0} user defined nodes by name and adding missing parent nodes", numUserTiles);

                switch (tilingScheme)
                {
                    case TilingScheme.UserDefined:
                    {
                        root = SceneNodeTilingExtensions.ConnectNodesByName(sceneNodes);
                        break;
                    }
                    case TilingScheme.Flat:
                    {
                        root = sceneNodes.Where(sn => sn.Name == "root").First();
                        foreach (var child in sceneNodes.Where(sn => sn.Name != "root"))
                        {
                            child.Transform.SetParent(root.Transform);
                        }
                        break;
                    }
                    default: throw new Exception("unexpected tiling scheme: " + tilingScheme);
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
                        var meshBounds = pair.Mesh.Bounds();
                        if (meshBounds.IsEmpty())
                        {
                            pipeline.LogWarn("empty mesh for user defined tile {0}", id);
                            meshBounds = new BoundingBox();
                        }
                        sceneNode.GetComponent<NodeBounds>().Bounds = meshBounds;
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
                root = BuildTileTreeFromInputs(pipeline, tilingScheme, project.FacesPerTile, pairs,
                                               info: msg => LogInfo(msg), verbose: msg => LogVerbose(msg));
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

        /// <summary>
        /// creates a tile tree that has (up to) a fixed depth matching the number of existing LODs
        //  does not use any mesh or texture based split criteria
        /// </summary>
        /// <param name="lodMeshOps">sorted by decreasing quality (best first)</param>
        /// <returns></returns>
        public static SceneNode BuildTileTreeFromLODs(PipelineCore pipeline, TilingScheme tilingScheme,
                                                      List<MeshOperator> lodMeshOps)
        {
            if (lodMeshOps.Count < 2)
            {
                throw new InvalidDataException("expecting > 1 LOD meshes, received " + lodMeshOps.Count);
            }

            if (lodMeshOps[0].CountVertices() < lodMeshOps[1].CountVertices())
            {
                pipeline.LogWarn("expecting LOD 0 ({0} verts) to be finer than LOD 1 ({1} verts)",
                                 lodMeshOps[0].CountVertices(), lodMeshOps[1].CountVertices());
            }

            var scheme = TilingSchemeBase.Create(tilingScheme);

            var lodBounds = lodMeshOps.Select(op => op.Bounds).ToArray();

            //child node names are created by adding onto parent name
            //so root name will be set to "root" after creating all descendants
            SceneNode root = new SceneNode("");

            // it is possible lodMeshes might have different bounds if decimation stretches or shrinks triangles
            root.AddComponent(new NodeBounds(BoundingBoxExtensions.Union(lodBounds)));

            var previousLevelNodes = new List<SceneNode> { root };
            for (int lod = lodMeshOps.Count - 2; lod >= 0; lod--)
            {
                var meshOp = lodMeshOps[lod];
                var currentLevelNodes = new List<SceneNode>();
                foreach (var node in previousLevelNodes)
                {                    
                    var bounds = node.GetComponent<NodeBounds>().Bounds;
                    int counter = 0; //note this is always exactly one decimal digit
                    foreach (var cb in scheme.Split(bounds).Where(b => !meshOp.Empty(b)))
                    {
                        currentLevelNodes.Add(CreateChildNode(node, cb, ref counter, meshOp));
                    }
                }
                previousLevelNodes = currentLevelNodes;
            }
            root.Name = "root";
            return root;
        }

        public static SceneNode BuildTileTreeFromInputs(PipelineCore pipeline, TilingScheme tilingScheme,
                                                        int maxFacesPerTile, List<MeshImagePair> pairs,
                                                        SplitByTextureOpts texOpts = null, double surfaceExtent = -1,
                                                        Action<string> info = null, Action<string> verbose = null)
        {
            //TODO when merge branch dev/texture-utilization
            //var multiClipper = new MultiMeshClipper(powerOfTwoTextures: powerOfTwoTextures, logger: pipeline);
            var multiClipper = new MultiMeshClipper();
            foreach (var pair in pairs)
            {
                multiClipper.AddInput(pair.Mesh, pair.Image);
            }

            //lower cost split criteria come before higher cost
            var splitCriteria = new List<ITileSplitCriteria> { new FaceSplitCriteria(maxFacesPerTile) };

            if (texOpts != null)
            {
                if (texOpts.useApproximateTileSplit)
                {
                    splitCriteria.Add(new TextureSplitCriteriaApproximate(texOpts));
                }
                else
                {
                    splitCriteria.Add(new TextureSplitCriteriaBackproject(texOpts));
                }
               
            }

            return BuildBoundsTree(multiClipper, tilingScheme, splitCriteria.ToArray(), surfaceExtent, info, verbose);
        }

        //each node name is of the form ABCDE... where
        //A is the index of a child of the root
        //B is the index of a child of the node corresponding to A, etc
        //thus each node name encodes a full path from the root to the node
        //and the collection of all leaf names encodes the full tree topology
        public static SceneNode BuildBoundsTree(MultiMeshClipper multiClipper, TilingScheme tilingScheme,
                                                ITileSplitCriteria[] splitCriteria, double surfaceExtent = -1,
                                                Action<string> info = null, Action<string> verbose = null)
        {
            info = info ?? (msg => { });
            verbose = verbose ?? (msg => { });

            string fsStatus = "unlimited";
            var fs = splitCriteria.Where(sc => sc is FaceSplitCriteria).Cast<FaceSplitCriteria>().FirstOrDefault();
            if (fs != null)
            {
                fsStatus = Fmt.KMG(fs.maxFaces);
            }
            string tsStatus = splitCriteria.Any(sc => sc is TextureSplitCriteria) ? "enabled" : "disabled";
            info($"building bounds tree, {splitCriteria.Length} split criteria: " +
                 $"{fsStatus} max faces per tile, texture split {tsStatus}");

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

            //child node names are created by adding onto parent name
            //so root name will be set to "root" after creating all descendants
            SceneNode root = new SceneNode("");
            var meshOps = multiClipper.GetMeshOps();

            root.AddComponent(new NodeBounds(totalBounds));

            var scheme = TilingSchemeBase.Create(tilingScheme);

            int surfaceTiles = 0, orbitalTiles = 0, surfaceSplits = 0, orbitalSplits = 0;
            var previousLevelNodes = new ConcurrentBag<SceneNode> { root };
            while (previousLevelNodes.Count > 0)
            {
                var currentLevelNodes = new ConcurrentBag<SceneNode>();
                CoreLimitedParallel.ForEach(previousLevelNodes, node =>
                {
                    var bounds = node.GetComponent<NodeBounds>().Bounds;

                    var sc = splitCriteria;
                    if (surfaceExtent == 0 || (surfaceBounds.HasValue && !surfaceBounds.Value.Intersects(bounds)))
                    {
                        sc = orbitalSplitCriteria;
                        Interlocked.Increment(ref orbitalTiles);
                    }
                    else
                    {
                        Interlocked.Increment(ref surfaceTiles);
                    }
                    bool shouldSplit = false;
                    foreach (var criteria in sc)
                    {
                        if (criteria.ShouldSplit(bounds, meshOps))
                        {
                            shouldSplit = true;
                            verbose($"splitting tile {node.Name} due to {criteria.GetType().Name}");
                            break;
                        }
                    }
                    if (shouldSplit)
                    {
                        if (sc == orbitalSplitCriteria)
                        {
                            Interlocked.Increment(ref orbitalSplits);
                        }
                        else
                        {
                            Interlocked.Increment(ref surfaceSplits);
                        }

                        var childrenBounds = scheme.Split(bounds);
                        verbose($"split tile {node.Name} ({tilingScheme}, min axis {bounds.MinAxis()}): " +
                                bounds.Fmt() + " -> " + string.Join(", ", childrenBounds.Select(cb => cb.Fmt())));
                        childrenBounds = childrenBounds.Where(b => meshOps.Any(op => !op.Empty(b)));
                        verbose($"filtered child bounds: " + string.Join(", ", childrenBounds.Select(cb => cb.Fmt())));

                        int counter = 0; //note this is always exactly one decimal digit
                        foreach (var childBounds in childrenBounds)
                        {
                            var child = CreateChildNode(node, childBounds, ref counter, meshOps);
                            currentLevelNodes.Add(child);
                            verbose($"made child {child.Name} " +
                                    $"({childBounds.Fmt()} -> {child.GetComponent<NodeBounds>().Bounds.Fmt()}) " +
                                    $"of {node.Name} ({bounds.Fmt()})");
                        }
                    }
                    else
                    {
                        verbose($"not splitting {node.Name}");
                    }
                });
                previousLevelNodes = currentLevelNodes;
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

        private static SceneNode CreateChildNode(SceneNode parent, BoundingBox bounds, ref int counter,
                                                 params MeshOperator[] meshOps)
        {
            string childName = parent.Name + counter++;
            string parentName = !string.IsNullOrEmpty(parent.Name) ? parent.Name : "root";

            //For user-defined nodes, which are typically leaves, the bounds will be recomputed as the predefined mesh's
            //bounds in DownloadInputsAndBuildTree(), but the result should be pretty much the same as we compute here.
            //For bounds computed in BuildLeaves, same story.
            //
            //For (non-user-defined) parent tiles the bounds will be updated when the parent tile mesh is created in
            //BuildParent to ensure the parent bounds includes both its children and its own mesh, which may exceed
            //these bounds a bit due to effects of mesh geometry decimation.
            //
            //Another thing to keep in mind here is that the bounds that were passed in to CreateChildNode() are
            //generally just any subregion of the parent's bounds.  They are not necessarily tight to the child
            //geometry, though it should have already been ensured that they contain at least some child geometry, not
            //totally empty.  That is actually OK for most codepaths, because these bounds will generally be replaced by
            //the actual child mesh bounds as explained above.  However, it is not good when using QuadAuto tiling
            //scheme, because that needs to be able to reason correctly about which bounding box dimension is smallest
            //(and correspondingly which face is largest).  So that is why we incur the extra cost of unioning the
            //ClippedMeshBounds() here.

            if (meshOps != null && meshOps.Length > 0)
            {
                bounds = BoundingBoxExtensions.Union(meshOps.Select(op => op.ClippedMeshBounds(bounds)).ToArray());
            }

            if (bounds.IsEmpty())
            {
                throw new Exception($"can't create empty child {childName} of {parentName}");
            }

            SceneNode child = new SceneNode(childName, parent.Transform);
            child.AddComponent(new NodeBounds(bounds));
            return child;
        }
    }
}
