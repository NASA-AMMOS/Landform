using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Xna.Framework;
using JPLOPS.Geometry;
using JPLOPS.Imaging;
using JPLOPS.Util;

namespace JPLOPS.Pipeline.TilingServer
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

            var tilingScheme = project.TilingScheme;

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
                    int depth = sceneNode.Transform.Depth();
                    var tilingNode =
                    TilingNode.Create(pipeline, id, project.Name, parentId, isLeaf, depth, save: false);
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
                    var pair = tilingNode.LoadMeshImagePair(pipeline, cleanMesh: true,
                                                            warn: msg => pipeline.LogWarn($"tile {id}: {msg}"));
                    if (pair != null)
                    {
                        LogVerbose("loaded mesh with {0} triangles for user defined tile {0}",
                                   pair.Mesh.Faces.Count, id);
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
                root = BuildTileTreeFromInputs(pairs, tilingScheme, project.MaxFacesPerTile,
                                               project.MinTileExtent, project.MaxLeafArea,
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
                    int depth = sceneNode.Transform.Depth();
                    tilingNode = TilingNode.Create(pipeline, id, projectName, parentId, isLeaf, depth, save: false);
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

                spew("saved", ++numSaved, 500);
            }

            LogInfo("saving node IDs and project");
            project.SaveNodeIds(ids, pipeline);
            project.TilesDefined = true;
            project.Save(pipeline);
        }

        private static TileSplitCriteria[] MakeTileSplitCriteria(int maxFacesPerTile, double maxLeafArea,
                                                                 TextureSplitOptions texSplitOptions,
                                                                 bool useTexSplitApprox, Action<string> info = null)
        {
            //lower cost split criteria come before higher cost
            var splitCriteria = new List<TileSplitCriteria>();

            if (maxFacesPerTile > 0)
            {
                splitCriteria.Add(new FaceSplitCriteria(maxFacesPerTile));
            }

            if (maxLeafArea > 0)
            {
                splitCriteria.Add(new AreaSplitCriteria(maxLeafArea));
            }

            TextureSplitCriteria tsc = null;
            if (texSplitOptions != null)
            {
                if (useTexSplitApprox)
                {
                    tsc = new TextureSplitCriteriaApproximate(texSplitOptions);
                }
                else
                {
                    tsc = new TextureSplitCriteriaBackproject(texSplitOptions);
                }
                splitCriteria.Add(tsc);
            }

            if (info != null)
            {
                string fsStatus = maxFacesPerTile > 0 ? Fmt.KMG(maxFacesPerTile) : "unlimited";
                string areaStatus = maxLeafArea > 0 ? (maxLeafArea + "m^2") : "unlimited";
                string tsStatus = (tsc is TextureSplitCriteriaApproximate) ? "approximate" :
                    (tsc is TextureSplitCriteriaBackproject) ? "backproject" : "disabled";
                if (tsc != null)
                {
                    tsStatus += ", max leaf texture resolution " + tsc.options.MaxTileResolution;
                }
                info($"{splitCriteria.Count} split criteria: {fsStatus} max faces per tile, " +
                     $"max leaf area {areaStatus}, texture split {tsStatus}");
            }
                
            return splitCriteria.ToArray();
        }

        private static TileSplitCriteria[] MakeOrbitalSplitCriteria(int maxFacesPerTile, double maxLeafArea,
                                                                    Action<string> info = null)
        {
            //lower cost split criteria come before higher cost
            var splitCriteria = new List<TileSplitCriteria>();

            if (maxFacesPerTile > 0)
            {
                splitCriteria.Add(new FaceSplitCriteria(maxFacesPerTile));
            }

            if (maxLeafArea > 0)
            {
                splitCriteria.Add(new AreaSplitCriteria(maxLeafArea));
            }

            if (info != null)
            {
                string fsStatus = maxFacesPerTile > 0 ? Fmt.KMG(maxFacesPerTile) : "unlimited";
                string areaStatus = maxLeafArea > 0 ? (maxLeafArea + "m^2") : "unlimited";
                info($"{splitCriteria.Count} orbital split criteria: {fsStatus} max faces per tile, " +
                     $"max leaf area {areaStatus}");
            }
                
            return splitCriteria.ToArray();
        }

        /// <summary>
        /// creates a tile tree that has (up to) a fixed depth matching the number of existing LODs
        /// </summary>
        /// <param name="lodMeshOps">at least two LODs sorted by decreasing quality (best first)</param>
        public static SceneNode BuildTileTreeFromLODs(List<MeshOperator> lodMeshOps, TilingScheme tilingScheme,
                                                      int maxFacesPerTile = -1,
                                                      double minTileExtent = 0, double maxLeafArea = 0,
                                                      TextureSplitOptions texSplitOptions = null,
                                                      bool useTexSplitApprox = true, int maxHeight = -1,
                                                      bool enforceMaxFaces = true,
                                                      Action<string> info = null, Action<string> verbose = null)
        {
            info = info ?? (msg => { });
            verbose = verbose ?? (msg => { });

            info($"building tile tree from {lodMeshOps.Count} LODs, " +
                 $"min tile extent {minTileExtent:F3}, {tilingScheme} tiling scheme");

            if (lodMeshOps.Count < 1)
            {
                throw new InvalidDataException("expecting at least one LOD mesh");
            }

            if (lodMeshOps.Count > 1 && lodMeshOps[0].VertexCount < lodMeshOps[1].VertexCount)
            {
                info(string.Format("expecting LOD 0 ({0} verts) to be finer than LOD 1 ({1} verts)",
                                   lodMeshOps[0].VertexCount, lodMeshOps[1].VertexCount));
            }

            var splitCriteria =
                MakeTileSplitCriteria(maxFacesPerTile, maxLeafArea, texSplitOptions, useTexSplitApprox, info);

            var scheme = TilingSchemeBase.Create(tilingScheme, minTileExtent);

            var rootBounds = BoundingBoxExtensions.Union(lodMeshOps.Select(o => o.Bounds).ToArray());
            if (rootBounds.IsEmpty())
            {
                BoundingBoxExtensions.Extend(ref rootBounds, Vector3.Zero);
            }

            //child node names are created by adding onto parent name
            //so root name will be set to "root" after creating all descendants
            SceneNode root = new SceneNode("");
            
            // it is possible LODs might have different bounds if decimation stretches or shrinks triangles
            root.AddComponent(new NodeBounds(rootBounds));
            
            var previousLevelNodes = new ConcurrentBag<SceneNode> { root };
            var tallies = new ConcurrentDictionary<string, int>();
            int height = 1;
            var fsc = splitCriteria.FirstOrDefault(c => c is FaceSplitCriteria);
            int maxFaces = fsc != null ? ((FaceSplitCriteria)fsc).maxFaces : -1;
            while (previousLevelNodes.Count > 0 && rootBounds.Volume() > 0)
            {
                if (maxHeight > 0 && height >= maxHeight &&
                    (!enforceMaxFaces || maxFaces <= 0 || previousLevelNodes
                     .Where(n => n.HasComponent<FaceCount>())
                     .All(n => n.GetComponent<FaceCount>().NumTris <= maxFaces)))
                {
                    info($"limiting tile tree height to {maxHeight}");
                    break;
                }
                var currentLevelNodes = new ConcurrentBag<SceneNode>();
                CoreLimitedParallel.ForEach(previousLevelNodes, node =>
                {                    
                    string name = node == root ? "root" : node.Name;
                    var bounds = node.GetComponent<NodeBounds>().Bounds;
                    string splitType = null;
                    foreach (var crit in splitCriteria)
                    {
                        string reason = crit.ShouldSplit(bounds, lodMeshOps[0]); //use finest lod for split decisions
                        if (!string.IsNullOrEmpty(reason))
                        {
                            splitType = crit.GetType().Name;
                            verbose($"attempting to split level {height-1} tile {name}: {splitType} {reason}");
                            break;
                        }
                    }
                    if (splitType != null)
                    {
                        var childrenBounds = scheme.Split(bounds).Where(b => !lodMeshOps[0].Empty(b)).ToArray();
                        if (childrenBounds.Length > 1)
                        {
                            tallies.AddOrUpdate(splitType, st => 1, (st, t) => t + 1);
                            //verbose($"split tile {name} ({tilingScheme}, min axis {bounds.MinAxis()}): " +
                            //        bounds.Fmt() + " -> " + string.Join(", ", childrenBounds.Select(cb => cb.Fmt())));
                            int counter = 0; //note this is always exactly one decimal digit
                            foreach (var childBounds in childrenBounds)
                            {
                                var child = CreateChildNode(node, childBounds, enforceMaxFaces && maxHeight > 0,
                                                            ref counter, lodMeshOps[0]);
                                currentLevelNodes.Add(child);
                                //verbose($"made child {child.Name} " +
                                //        $"({childBounds.Fmt()} -> {child.GetComponent<NodeBounds>().Bounds.Fmt()}) " +
                                //        $"of {name} ({bounds.Fmt()})");
                            }
                        }
                        else
                        {
                            verbose($"did not split tile {name}: split resulted in less than two children");
                        }
                    }
                    //else
                    //{
                    //    verbose($"did not split tile {name}: no triggered split criteria");
                    //}
                });
                previousLevelNodes = currentLevelNodes;
                if (currentLevelNodes.Count > 0)
                {
                    height++;
                }
            }

            info($"total tile tree height: {height}" + (maxHeight > 0 ? $" (max {maxHeight})" : ""));
            foreach (var entry in tallies)
            {
                info($"split {entry.Value} tiles due to {entry.Key}");
            }

            root.Name = "root";
            return root;
        }

        public static SceneNode BuildTileTreeFromInputs(List<MeshImagePair> pairs, TilingScheme tilingScheme,
                                                        int maxFacesPerTile = 0, double minTileExtent = 0,
                                                        double maxLeafArea = 0, double maxOrbitalLeafArea = 0,
                                                        double surfaceExtent = -1,
                                                        TextureSplitOptions texSplitOptions = null,
                                                        bool useTexSplitApprox = true, int maxHeight = -1,
                                                        bool enforceMaxFaces = true,
                                                        Action<string> info = null, Action<string> verbose = null)
        {
            info = info ?? (msg => { });

            var meshOps = pairs
                .Where(p => p.Mesh != null || p.MeshOp != null)
                .Select(p => p.EnsureMeshOperator())
                .ToArray();

            info($"building tile tree from {meshOps.Length} inputs, " +
                 $"min tile extent {minTileExtent:F3}, {tilingScheme} tiling scheme");

            var splitCriteria = MakeTileSplitCriteria(maxFacesPerTile, maxLeafArea, texSplitOptions,
                                                      useTexSplitApprox, info);

            var orbitalSplitCriteria = splitCriteria.Where(sc => !(sc is TextureSplitCriteria)).ToArray();
            if (maxOrbitalLeafArea > 0)
            {
                orbitalSplitCriteria = MakeOrbitalSplitCriteria(maxFacesPerTile, maxOrbitalLeafArea, info);
            }

            return BuildBoundsTree(meshOps, tilingScheme, splitCriteria, minTileExtent, surfaceExtent,
                                   orbitalSplitCriteria, maxHeight, enforceMaxFaces, info: info, verbose: verbose);
        }

        public static SceneNode BuildBoundsTree(MultiMeshClipper multiClipper, TilingScheme tilingScheme,
                                                TileSplitCriteria[] splitCriteria, double minTileExtent = 0,
                                                double surfaceExtent = -1,
                                                TileSplitCriteria[] orbitalSplitCriteria = null,
                                                int maxHeight = -1, bool enforceMaxFaces = true,
                                                Action<string> info = null, Action<string> verbose = null)
        {
            return BuildBoundsTree(multiClipper.GetMeshOps(), tilingScheme, splitCriteria, minTileExtent,
                                   surfaceExtent, orbitalSplitCriteria, maxHeight, enforceMaxFaces, info, verbose);
        }

        //build a tile tree based on the geometry of the finest LOD mesh, represented by one or more meshOps
        //
        //each returned node name is of the form ABCDE... where
        //A is the index of a child of the root
        //B is the index of a child of the node corresponding to A, etc
        //thus each node name encodes a full path from the root to the node
        //and the collection of all leaf names encodes the full tree topology
        public static SceneNode BuildBoundsTree(MeshOperator[] meshOps, TilingScheme tilingScheme,
                                                TileSplitCriteria[] splitCriteria, double minTileExtent = 0,
                                                double surfaceExtent = -1,
                                                TileSplitCriteria[] orbitalSplitCriteria = null, int maxHeight = -1,
                                                bool enforceMaxFaces = true,
                                                Action<string> info = null, Action<string> verbose = null)
        {
            bool isVerbose = verbose != null;
            info = info ?? (msg => { });
            verbose = verbose ?? (msg => { });

            var rootBounds = BoundingBoxExtensions.Union(meshOps.Select(mo => mo.Bounds).ToArray());
            if (rootBounds.IsEmpty())
            {
                BoundingBoxExtensions.Extend(ref rootBounds, Vector3.Zero);
            }

            //if surfaceExtent is negative then treat the whole scene like surface
            //if surfaceExtent is zero treat the whole scene like orbital (handled below)
            BoundingBox? surfaceBounds = TilingProject.GetSurfaceBoundingBox(surfaceExtent); //null if negative
            if (surfaceBounds.HasValue)
            {
                var sb = surfaceBounds.Value;
                sb.Min.Z = rootBounds.Min.Z;
                sb.Max.Z = rootBounds.Max.Z;
                surfaceBounds = sb;
            }

            //child node names are created by adding onto parent name
            //so root name will be set to "root" after creating all descendants
            SceneNode root = new SceneNode("");

            root.AddComponent(new NodeBounds(rootBounds));

            var scheme = TilingSchemeBase.Create(tilingScheme, minTileExtent);

            int surfaceTiles = 0, orbitalTiles = 0, surfaceSplits = 0, orbitalSplits = 0;
            var previousLevelNodes = new ConcurrentBag<SceneNode> { root };
            var tallies = new ConcurrentDictionary<string, int>();
            int height = 1;
            int maxFaces = -1;
            while (previousLevelNodes.Count > 0 && rootBounds.Volume() > 0)
            {
                if (maxHeight > 0 && height >= maxHeight &&
                    (!enforceMaxFaces || maxFaces <= 0 || previousLevelNodes
                     .Where(n => n.HasComponent<FaceCount>())
                     .All(n => n.GetComponent<FaceCount>().NumTris <= maxFaces)))
                {
                    info($"limiting tile tree height to {maxHeight}");
                    break;
                }
                var currentLevelNodes = new ConcurrentBag<SceneNode>();
                double lastSpew = UTCTime.Now();
                CoreLimitedParallel.ForEach(previousLevelNodes, node =>
                {
                    string name = node == root ? "root" : node.Name;
                    var bounds = node.GetComponent<NodeBounds>().Bounds;

                    string tileType = "surface";
                    var sc = splitCriteria;
                    if (surfaceExtent == 0 || (surfaceBounds.HasValue && !surfaceBounds.Value.Intersects(bounds)))
                    {
                        tileType = "orbital";
                        sc = orbitalSplitCriteria;
                        Interlocked.Increment(ref orbitalTiles);
                    }
                    else
                    {
                        Interlocked.Increment(ref surfaceTiles);
                    }
                    double now = UTCTime.Now();
                    if ((now - lastSpew) > 10)
                    {
                        info($"tile tree height {height}, " +
                             $"{Fmt.KMG(surfaceTiles)} surface tiles, {Fmt.KMG(orbitalTiles)} orbital");
                        lastSpew = now;
                    }
                    var fsc = sc.FirstOrDefault(c => c is FaceSplitCriteria);
                    maxFaces = fsc != null ? ((FaceSplitCriteria)fsc).maxFaces : -1;
                    string splitType = null;
                    foreach (var crit in sc)
                    {
                        string reason = crit.ShouldSplit(bounds, meshOps);
                        if (!string.IsNullOrEmpty(reason))
                        {
                            splitType = crit.GetType().Name;
                            verbose($"attempting to split level {height-1} {tileType} tile {name}: " +
                                    $"{splitType} {reason}");
                            break;
                        }
                    }
                    if (splitType != null)
                    {
                        var childrenBounds = scheme.Split(bounds).Where(b => meshOps.Any(op => !op.Empty(b))).ToArray();
                        if (childrenBounds.Length > 1)
                        {
                            tallies.AddOrUpdate(splitType, st => 1, (st, t) => t + 1);
                            if (sc == orbitalSplitCriteria)
                            {
                                Interlocked.Increment(ref orbitalSplits);
                            }
                            else
                            {
                                Interlocked.Increment(ref surfaceSplits);
                            }
                            //verbose($"split tile {name} ({tilingScheme}, min axis {bounds.MinAxis()}): " +
                            //        bounds.Fmt() + " -> " + string.Join(", ", childrenBounds.Select(cb => cb.Fmt())));
                            int counter = 0; //note this is always exactly one decimal digit
                            foreach (var childBounds in childrenBounds)
                            {
                                var child = CreateChildNode(node, childBounds, enforceMaxFaces && maxHeight > 0,
                                                            ref counter, meshOps);
                                currentLevelNodes.Add(child);
                                //verbose($"made child {child.Name} " +
                                //        $"({childBounds.Fmt()} -> {child.GetComponent<NodeBounds>().Bounds.Fmt()}) " +
                                //        $"of {name} ({bounds.Fmt()})");
                            }
                        }
                        else
                        {
                            verbose($"did not split {tileType} tile {name}: split resulted in less than two children");
                        }
                    }
                    //else
                    //{
                    //    verbose($"did not split tile {name}: no triggered split criteria");
                    //}
                });
                previousLevelNodes = currentLevelNodes;
                if (currentLevelNodes.Count > 0)
                {
                    height++;
                }
            }

            info($"total tile tree height: {height}" + (maxHeight > 0 ? $" (max {maxHeight})" : ""));
            info($"split {surfaceSplits}/{surfaceTiles} surface tiles, {orbitalSplits}/{orbitalTiles} orbital");
            foreach (var entry in tallies)
            {
                info($"split {entry.Value} tiles due to {entry.Key}");
            }

            root.Name = "root";
            return root;
        }

        private MeshImagePair DownloadInput(TilingInput input)
        {
            Image image = null;
            if (input.ImageUrl != null)
            {
                if (input.ImageWidth < ChunkInput.SPARSE_IMAGE_CHUNK_RES &&
                    input.ImageHeight < ChunkInput.SPARSE_IMAGE_CHUNK_RES)
                {
                    image = pipeline.LoadImage(input.ImageUrl, noCache: true);
                }
                else
                {
                    image = new SparsePipelineImage(pipeline, input.ImageUrl, ChunkInput.SPARSE_IMAGE_CHUNK_RES);
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

        private static SceneNode CreateChildNode(SceneNode parent, BoundingBox bounds, bool estimateFaceCount,
                                                 ref int counter, params MeshOperator[] meshOps)
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
            if (estimateFaceCount)
            {
                child.AddComponent(new FaceCount(meshOps.Sum(op => op.CountFaces(bounds))));
            }
            return child;
        }
    }
}
