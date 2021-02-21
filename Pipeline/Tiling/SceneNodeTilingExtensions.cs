using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Util;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Pipeline.TilingServer;

namespace OPS.Pipeline
{
    public static class SceneNodeTilingExtensions
    {
        public const double DEFAULT_SEARCH_RATIO = 1.1f;

        public static bool useTextureError;

        public static void SaveMesh(this SceneNode node, string directory, string meshExtension = "ply", string imageExtension = "jpg")
        {
            meshExtension = "." + meshExtension;
            imageExtension = "." + imageExtension;

            var pair = node.GetComponent<MeshImagePair>();
            Mesh m = pair.Mesh;
            string imgName = null;
            if (pair.Image != null)
            {
                imgName = Path.Combine(directory, node.Name + imageExtension);
                pair.Image.Save<byte>(imgName);
            }
            m.Save(Path.Combine(directory, node.Name + meshExtension), imgName);
        }

        public static List<SceneNode> FindOverlapingNodes(this SceneNode root, int minDepth, BoundingBox box)
        {
            List<SceneNode> result = new List<SceneNode>();
            Stack<SceneNode> stack = new Stack<SceneNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                SceneNode node = stack.Pop();
                if (node == null)
                {
                    continue;
                }
                BoundingBox nodeBounds = node.GetComponent<NodeBounds>().Bounds;
                if (!nodeBounds.Intersects(box))
                {
                    continue;
                }
                if (node.IsLeaf || node.Transform.Depth() >= minDepth)
                {
                    result.Add(node);
                    continue;
                }
                else
                {
                    foreach (var child in node.Transform.Children.Select(t => t.Node))
                    {
                        stack.Push(child);
                    }
                }
            }
            return result;
        }

        public static int ComputeParentTileResolution(IEnumerable<MeshImagePair> childMeshImagePairs,
                                                      BoundingBox cropBounds, int maxTextureSize = int.MaxValue)
        {
            if (maxTextureSize == 0)
            {
                return 0; //texturing disabled
            }
            if (maxTextureSize < 0)
            {
                maxTextureSize = int.MaxValue;
            }
            // Read all overlapping meshes, crop each to the extent of the leaf tile
            // and calculate the area the triangles occupy in units of pixels.  Sum all
            // the areas and round up to nearest power of two to decide size of the new tile
            double totalPixels = 0;
            foreach (var p in childMeshImagePairs)
            {
                var clipped = Mesh.Clip(p.Mesh, cropBounds);
                totalPixels += TextureBaker.ComputePixelArea(clipped, p.Image);
            }
            int size =  TextureBaker.PixelAreaToSquareDimension(totalPixels);
            size = Math.Min(size, maxTextureSize);
            return size;
        }

        /// <summary>
        /// Returns a bounding box that is the union of all direct children bounding boxes
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public static BoundingBox ChildBounds(this SceneNode node)
        {
            var childBounds = node.Children.Select(c => c.GetComponent<NodeBounds>().Bounds).ToArray();
            return BoundingBoxExtensions.Union(childBounds);
        }

        public static bool AllChildrenHaveMeshes(this SceneNode node)
        {
            return node.Children.All(n => n.HasComponent<MeshImagePair>());
        }

        /// <summary>
        /// find all nodes that would be required to build a mesh for a given node
        ///
        /// this is potentially more than just the topological descendants of the node
        /// because typically we cast a wider spatial search which enables better boundary conditions for the mesh
        ///
        /// this method returns all nodes d that meet the following conjunctive criteria
        /// a) d descends from root
        /// b) d is at least the same depth (topological distance) from root as this node's children
        /// c) the bounding box of d intersects the search bounds, computed as the bounding box union of our
        ///    children's bounds scaled (generally up) by the given search ratio
        ///
        /// NOTE: as in all the tiling code bounds are all in same coordinate frame (all node Transforms are identity)
        /// </summary>
        public static List<SceneNode> FindNodesRequiredForParent
            (this SceneNode node, SceneNode root, double childBoundSearchRatio = DEFAULT_SEARCH_RATIO)
        {
            int childDepth = node.Children.First().Transform.Depth();
            var searchBounds = node.ChildBounds().CreateScaled(childBoundSearchRatio);
            return root.FindOverlapingNodes(childDepth, searchBounds);
        }

        /// <summary>
        /// Assumes all nodes below this node have been processed
        /// </summary>
        /// <param name="node"></param>
        /// <param name="root"></param>
        /// <param name="maxFaceCountTarget"></param>
        /// <param name="maxTextureSize"></param>
        /// <param name="skirtAxis"></param>
        /// <param name="childBoundSearchRatio"></param>
        public static bool BuildGeometryFromChildren
            (this SceneNode node, SceneNode root, MeshReconstructionMethod reconstructionMethod,
             int maxFaceCountTarget, SkirtMode? skirtAxis, TextureMode textureMode, int maxTextureSize,
             TextureProjector textureProjector = null, Image textureImage = null,
             double childBoundSearchRatio = DEFAULT_SEARCH_RATIO,
             Action<string> info = null, Action<string> error = null)
        {
            info = info ?? (msg => {});
            error = error ?? (msg => {});

            info("merging child meshes");

            var children = FindNodesRequiredForParent(node, root, childBoundSearchRatio);

            var childMeshImagePairs = children
                .Where(n => n.HasComponent<MeshImagePair>())
                .Select(n => n.GetComponent<MeshImagePair>());

            var childMeshes = childMeshImagePairs.Where(p => p.Mesh != null).Select(p => p.Mesh);
            
            Mesh combinedFull = Mesh.MergeWithCommonAttributes(childMeshes.ToArray(), clean: true, normalize: true);
            if (!combinedFull.HasNormals)
            {
                combinedFull.GenerateVertexNormals();
            }

            // Compute an enlargedMinBounds instead of just using searchBounds
            // because in the tiling server "BuildParent" routine we construct a flat tree
            // with just the parent node and all of its dependents as children.
            // As a result "ChildBounds" is no longer a reliable measure.
            // This is pretty nuanced and could potentially benefit from a refactor in the future.
            BoundingBox minimumBounds = node.GetComponent<NodeBounds>().Bounds;
            BoundingBox enlargedMinBounds = minimumBounds.CreateScaled(childBoundSearchRatio);
            combinedFull = Mesh.Clip(combinedFull, enlargedMinBounds);

            combinedFull.NormalizeNormals();

            Mesh combinedDecimated = null;
            Mesh fullClipped = Mesh.Clip(combinedFull, minimumBounds);
            
            if (fullClipped.Vertices.Count == 0)
            {
                error("parent tile mesh empty");
                return false;
            }

            // If the combined mesh is already less than the target face count we can skip the ResampleDecimation
            // This also has the added benifit of avoiding calls to ResampleDecimation
            // on very low face count meshes which can sometimes fail
            if (fullClipped.Faces.Count <= maxFaceCountTarget)
            {
                combinedDecimated = fullClipped;
            }
            else
            { 
                Vector3? cornerDirection = null;
                if (skirtAxis.HasValue)
                {
                    switch (skirtAxis.Value)
                    {
                        case SkirtMode.X: cornerDirection = Vector3.UnitX; break;
                        case SkirtMode.Y: cornerDirection = Vector3.UnitY; break;
                        case SkirtMode.Z: cornerDirection = Vector3.UnitZ; break;
                    }
                }
                info("decimating parent tile mesh");
                combinedDecimated = combinedFull.ResampleDecimation(maxFaceCountTarget, reconstructionMethod,
                                                                    clippingBounds: minimumBounds,
                                                                    cornerDirection: cornerDirection);
            }

            info("cleaning parent tile mesh");
            combinedDecimated.Clean();

            info("computing parent tile resolution");
            int size = 0;
            if (textureMode != TextureMode.None)
            {
                size = ComputeParentTileResolution(childMeshImagePairs, combinedDecimated.Bounds(), maxTextureSize);
            }

            Image img = null, index = null;
            if (size != 0)
            {
                if (textureProjector != null)
                {
                    info("atlasing parent tile with texture projection");
                    combinedDecimated.ProjectTexture(textureProjector.ImageWidth, textureProjector.ImageHeight,
                                                     textureProjector.CameraModel,
                                                     meshToImage: textureProjector.MeshToImage);
                    if (textureMode != TextureMode.Clip || textureImage == null)
                    {
                        combinedDecimated.RescaleUVsForTexture(size, size);
                    }
                }
                else
                {
                    info($"atlasing parent tile with UVAtlas, resolution {size}");
                    combinedDecimated = UVAtlas.Atlas(combinedDecimated, size, size);
                    if (combinedDecimated == null)
                    {
                        error("failed to atlas parent tile with UVAtlas");
                        return false;
                    }
                }

                if (textureMode == TextureMode.Clip && textureProjector != null && textureImage != null)
                {
                    if (childMeshImagePairs.All(mip => mip.Index != null))
                    {
                        index = new Image(3, textureImage.Width, textureImage.Height);
                        for (int r = 0; r < index.Height; r++)
                        {
                            for (int c = 0; c < index.Width; c++)
                            {
                                index[0, r, c] = 1; //reserve 0 as invalid
                                index[1, r, c] = r;
                                index[2, r, c] = c;
                            }
                        }
                    }
                    var logger = new ThunkLogger() { Info = info, Warn = error, Error = error };
                    var tmc = new TexturedMeshClipper(logger: logger);
                    var pair = tmc.RemapMeshClipImage(combinedDecimated, textureImage, index, size);
                    combinedDecimated = pair.Mesh;
                    img = pair.Image;
                }
                else
                {
                    //we need to bake parent tile textures even when textureMode is Clip
                    //unless we also have a texture projector to assign appropriate UVs
                    info("baking parent tile texture");
                    TextureBaker tb = new TextureBaker(childMeshImagePairs.ToArray());
                    img = tb.Bake(combinedDecimated, size, size, out index); //Writes index iff indexes not null
                    //note that if textureMode is clip then leaf tile textures may have actually been clipped
                    //even though we are baking here
                    //because leaf tiles can take their UVs from the input meshes
                    //but a parent tile can only get usable UVs for clipping by texture projection
                }
            }

            if (!combinedDecimated.HasNormals)
            {
                info("generating parent tile mesh vertex normals");
                combinedDecimated.GenerateVertexNormals();
            }

            info("completing parent");
            // We need to combine bounds here because decimated bounds may be smaller than the child bounds
            var bounds = BoundingBox.CreateMerged(combinedDecimated.Bounds(), minimumBounds);
            node.GetComponent<NodeBounds>().Bounds = bounds;

            // Add new mesh and image to parent
            node.AddComponent(new MeshImagePair(combinedDecimated, img, index));

            //prevent UpdateGeometricError() from using child meshes if combinedDecimated == fullClipped
            //meaning we chose not to decimate the merged clipped child meshes
            //in that case the geometric error is just the max of the children's errors
            info("computing parent tile geometric error");
            var depMeshes = new List<Mesh>(); //empty list (not null) if combinedDecimated == fullClipped
            if (combinedDecimated != fullClipped)
            {
                depMeshes.Add(fullClipped);
            }
            node.UpdateGeometricError(children, depMeshes, info);
            //TODO: move constants in UpdateGeometricError() to TilingDefaults when dev/tiling-updates is merged

            return true;
        }

        /// <summary>
        /// Given a list of nodes, connect them in a tree based on name prefix convention and return the root
        ///
        /// each node name is of the form ABCDE... where
        /// A is the index of a child of the root
        /// B is the index of a child of the node corresponding to A, etc
        /// thus each node name encodes a full path from the root to the node
        /// and the collection of all leaf names encodes the full tree topology
        ///
        /// as long as all the leaves are provided this function will reconstitute any missing parent nodes
        /// </summary>
        /// <param name="nodes"></param>
        /// <returns></returns>
        public static SceneNode ConnectNodesByName(IEnumerable<SceneNode> nodes)
        {
            Dictionary<string, SceneNode> lookup = new Dictionary<string, SceneNode>();
            foreach(var node in nodes)
            {
                lookup.Add(node.Name, node);
            }
            Queue<SceneNode> nodesToConnect = new Queue<SceneNode>(nodes);
            SceneNode root = null;
            while(nodesToConnect.Count != 0)
            {
                var node = nodesToConnect.Dequeue();
                if(node.Name == "root")
                {
                    root = node;
                    continue;
                }
                string parentId = (node.Name.Length == 1) ? "root" : node.Name.Substring(0, node.Name.Length - 1);
                if(!lookup.ContainsKey(parentId))
                {
                    var p = new SceneNode(parentId);
                    nodesToConnect.Enqueue(p);
                    lookup.Add(parentId, p);
                }
                var parent = lookup[parentId];
                node.Transform.SetParent(parent.Transform);
            }
            return root;
        }
        
        /// <summary>
        /// Given a tree with leaves that have meshes, compute bounding boxes up the tree such that
        /// parents bounding boxes fully enclose their children.  Add NodeBounds components onto the
        /// nodes of the tree and set their bounds accordingly.  If parent nodes have mesh data their
        /// meshes will also be enclosed by the calculated bounds.
        /// </summary>
        /// <param name="root"></param>
        public static void ComputeBounds(SceneNode root, bool useExistingLeafBounds = false)
        {
            HashSet<SceneNode> curParents = new HashSet<SceneNode>();
            foreach (var leaf in root.Leaves())
            {
                if (!useExistingLeafBounds || !leaf.HasComponent<NodeBounds>())
                {
                    var pair = leaf.GetComponent<MeshImagePair>();
                    var meshBounds = pair.Mesh.Bounds();
                    if (meshBounds.IsEmpty())
                    {
                        meshBounds = new BoundingBox();
                    }
                    leaf.GetOrAddComponent<NodeBounds>().Bounds = meshBounds;
                }
                if (leaf.Parent != null)
                {
                    curParents.Add(leaf.Parent);
                }
            }
            while (curParents.Count > 0)
            {
                HashSet<SceneNode> nextParents = new HashSet<SceneNode>();
                foreach (var p in curParents)
                {
                    p.GetOrAddComponent<NodeBounds>().Bounds =
                        BoundingBoxExtensions.Union(p.Children.Select(c => c.GetOrAddComponent<NodeBounds>().Bounds)
                                                    .ToArray());
                    if (p.HasComponent<MeshImagePair>() && p.GetComponent<MeshImagePair>().Mesh != null)
                    {
                        p.GetComponent<NodeBounds>().Bounds =
                            BoundingBoxExtensions.Union(p.GetComponent<MeshImagePair>().Mesh.Bounds(),
                                                        p.GetComponent<NodeBounds>().Bounds);
                    }
                    if (p.Parent != null)
                    {
                        nextParents.Add(p.Parent);
                    }
                }
                curParents = nextParents;
            }           
        }

        /// <summary>
        /// Add or recompute NodeGeometricError.
        ///
        /// Assumes the node's children are available and already have their errors computed.
        ///
        /// https://github.com/CesiumGS/3d-tiles/blob/master/3d-tiles-overview.pdf
        /// discusses specifically what the geometric error is supposed to represent in section 5 - Geometric Error:
        /// > Each tileset and each tile has a geometricError property that quantifies the error
        /// > of the simplified geometry compared to the actual geometry.
        ///
        /// For a leaf node the error is always 0.
        ///
        /// For a node with no mesh of its own the error is the max of its children's errors.
        ///
        /// Otherwise, for a parent node we essentially compute the the Hausdorff distance between the decimated mesh,
        /// if any, vs the child meshes.  We then add that to the maximum geometric error of any of the children.
        /// Because none of that will account for situations where the parent geometry is good but its texture is less
        /// good, we also estimate the effective parent mesh texture resolution in units of lineal meters per texel,
        /// multiplied by an adjustment factor.  If that is larger than th Hausdorff distance it is used instead
        /// (i.e. instead of the sum of the Hausdorff distance and the max child error).
        ///
        /// Yes, this is quite confusing.  Consider the effect in the viewer, where the maximum screenspace error
        /// threshold is set at say 16 pixels.
        ///
        /// First consider the nominal  case when the tile geometric error dominates the  texture error.  The tile error
        /// will be transformed  from meters to screen pixels depending  on the current distance from the  camera to the
        /// tile.   This computation  is done  assuming the  tile error  is measured  in lineal  meters.  If  the actual
        /// geometric error, say 0.05m, dominates the tile texture error and the effective conversion factor from linear
        /// error in meters to  screen pixels (dependent on the camera FOV, screen  resolution, and distance from camera
        /// to terrain) is greater than 320 then it will call for switching to the next finer LOD, because errors in the
        /// currently displayed geometry can move things more than 0.05m  * 320px/m = 16 px from  where they should be.
        ///   
        /// Now consider the case where the tile texture error dominates, say 0.05, meaning the actual tile texture
        /// resolution is 0.0125 lineal meters per texel if TEXTURE_ERROR_MULTIPLIER=4.  Then one lineal texel maps to
        /// 0.0125*320 = 4 lineal pixels (16 square pixels) on screen, a relatively large amount of texture
        /// magnification.  The next finer LOD will be triggered because of the texture magnification.
        /// </summary>
        public static double UpdateGeometricError(this SceneNode node,
                                                  List<SceneNode> dependencies,
                                                  List<Mesh> dependencyMeshes = null,
                                                  Action<string> info = null)
        {
            info = info ?? (msg => {});
            int nd = dependencies.Count;

            //TODO: move these to TilingDefaults when dev/tiling-updates is merged
            double TEXTURE_ERROR_MULTIPLIER = 4;
            double HAUSDORFF_RELATIVE_ACCURACY = 0.005; //0.5% of mesh bounds

            if (node.IsLeaf)
            {
                node.GetOrAddComponent<NodeGeometricError>().Error = 0;
                info($"{node.Name} is a leaf, geometric error 0");
                return 0;
            }

            double maxDepError = 0;
            foreach (var dep in dependencies)
            {
                var depError = dep.GetComponent<NodeGeometricError>();
                if (depError != null)
                {
                    maxDepError = Math.Max(depError.Error, maxDepError);
                }
            }

            var mip = node.GetComponent<MeshImagePair>();
            if (mip == null || mip.Mesh == null)
            {
                node.GetOrAddComponent<NodeGeometricError>().Error = maxDepError;
                info($"{node.Name} empty, geometric error {maxDepError:F3} (max of {nd} dependencies)");
                return maxDepError;
            }

            if (dependencyMeshes == null)
            {
                dependencyMeshes = dependencies
                    .Select(d => d.GetComponent<MeshImagePair>())
                    .Where(p => p != null && p.Mesh != null)
                    .Select(p => p.Mesh)
                    .ToList();
            }

            double meshError = 0; //meters
            if (dependencyMeshes.Count > 0)
            {
                double accuracy = 0.001; //1mm
                var bounds = node.GetComponent<NodeBounds>();
                if (bounds != null)
                {
                    accuracy = bounds.Bounds.MaxDimension() * HAUSDORFF_RELATIVE_ACCURACY;
                }
                //the merged dependency meshes can be a significant superset of this node's mesh
                //just compute the unidirectional Hausdorff distance from this node's mesh to the merged dep meshes
                bool symmetric = false;
                meshError = maxDepError + mip.Mesh.HausdorffDistance(accuracy, symmetric, dependencyMeshes.ToArray());
            }

            info($"{node.Name} mesh error {meshError:F3} (incl max {maxDepError:F3} of {nd} dependencies)");

            double textureError = 0; //lineal meters per texel
            if (useTextureError)
            {
                double pixelArea = 0, surfaceArea = 0;
                if (mip.Image != null)
                {
                    pixelArea = mip.Mesh.ComputePixelArea(mip.Image);
                    if (pixelArea > 0)
                    {
                        surfaceArea = mip.Mesh.SurfaceArea();
                        textureError = TEXTURE_ERROR_MULTIPLIER * Math.Sqrt(surfaceArea / pixelArea);
                    }
                }
                info($"{node.Name} texture error {textureError:F3}" +
                     (pixelArea > 0 ? $" = {TEXTURE_ERROR_MULTIPLIER} * sqrt({surfaceArea:F3}m^2 / {pixelArea:F3}px^2)"
                      : ""));
            }

            double error = Math.Max(meshError, textureError);
            info($"{node.Name} geometric error {error:F3}, meshError={meshError:F3}, textureError={textureError:F3}");

            node.GetOrAddComponent<NodeGeometricError>().Error = error;
            return error;
        }

        public static void DumpStats(this SceneNode root, Action<string> writeLine)
        {
            var nodes = root.DepthFirstTraverse().ToList();

            foreach (var node in nodes)
            {
                if (node.HasComponent<MeshImagePair>() && !node.HasComponent<MeshImagePairStats>())
                {
                    node.AddComponent(new MeshImagePairStats(node.GetComponent<MeshImagePair>()));
                }
            }

            void dumpTextureStats(IEnumerable<MeshImagePairStats> mipStats, string prefix = "")
            {
                var minUVArea = mipStats.Min(s => s.UVArea);
                var maxUVArea = mipStats.Max(s => s.UVArea);
                
                var texRes = mipStats
                    .Where(s => s.MeshArea > 0 && s.UVArea > 0 && s.NumPixels > 0)
                    .Select(s => (s.UVArea * s.NumPixels) / (s.MeshArea * 100 * 100))
                    .OrderBy(v => v);
                var minTexRes = texRes.FirstOrDefault();
                var maxTexRes = texRes.LastOrDefault();

                if (minTexRes > 0 || maxTexRes > 0)
                {
                    writeLine(string.Format("{0}texture utilization {1:f3}-{2:f3}; texels/cm^2 {3:f3}-{4:f3}",
                                            prefix, minUVArea, maxUVArea, minTexRes, maxTexRes));
                }
            }

            void dumpLevel(IEnumerable<SceneNode> level, string msg)
            {
                var errors = level
                    .Where(node => node.HasComponent<NodeGeometricError>())
                    .Select(node => node.GetComponent<NodeGeometricError>().Error)
                    .OrderBy(e => e)
                    .ToList();
                if (errors.Count > 0)
                {
                    msg += string.Format("; geometric error {0:f3}-{1:f3}", errors.First(), errors.Last());
                }

                writeLine(msg);

                var bounds = level
                    .Where(node => node.HasComponent<NodeBounds>())
                    .Select(node => node.GetComponent<NodeBounds>().Bounds)
                    .OrderBy(b => b.Volume())
                    .ToList();
                if (bounds.Count > 0)
                {
                    var minBounds = bounds.First();
                    var maxBounds = bounds.Last();
                    msg = string.Format("  {0} bounds {1}{2}",
                                        bounds.Count, minBounds.FmtExtent(),
                                        bounds.Count > 1 ? ("-" + maxBounds.FmtExtent()) : "");
                    writeLine(msg);
                }

                var mipStats = level
                    .Where(node => node.HasComponent<MeshImagePairStats>())
                    .Select(node => node.GetComponent<MeshImagePairStats>())
                    .ToList();

                if (mipStats.Count > 0)
                {
                    msg = "";

                    var imgStats = mipStats.Where(s => s.NumPixels > 0).OrderBy(s => s.NumPixels).ToList();
                    if (imgStats.Count > 0)
                    {
                        var minImg = imgStats.First();
                        var maxImg = imgStats.Last();
                        msg = string.Format("  {0} images {1}x{2}-{3}x{4}, {5} total pixels", imgStats.Count,
                                            minImg.ImageWidth, minImg.ImageHeight,
                                            maxImg.ImageWidth, maxImg.ImageHeight,
                                            Fmt.KMG(imgStats.Sum(s => s.NumPixels)));
                    }

                    int numIndices = mipStats.Count(s => s.HasIndex);
                    if (numIndices > 0)
                    {
                        msg += string.Format("{0}{1} indices", (msg != "") ? ", " : "", numIndices);
                    }

                    var vertStats = mipStats.Where(s => s.NumVerts > 0).OrderBy(s => s.NumVerts).ToList();
                    if (vertStats.Count > 0)
                    {
                        msg += (msg != "") ? ", " : "  "; 

                        var minVerts = vertStats.First().NumVerts;
                        var maxVerts = vertStats.Last().NumVerts;
                        msg += string.Format("{0} meshes {1}-{2} verts", vertStats.Count,
                                             Fmt.KMG(minVerts), Fmt.KMG(maxVerts));

                        var triStats = mipStats.Where(s => s.NumTris > 0).OrderBy(s => s.NumTris).ToList();
                        if (triStats.Count > 0)
                        {
                            var minTris = triStats.First().NumTris;
                            var maxTris = triStats.Last().NumTris;

                            var minMeshArea = triStats.Min(s => s.MeshArea);
                            var maxMeshArea = triStats.Max(s => s.MeshArea);
                            
                            var minTriArea = triStats.Min(s => s.MinTriArea);
                            var maxTriArea = triStats.Max(s => s.MaxTriArea);
                            
                            msg += string.Format(", {0}-{1} tris ({2} total), mesh area {3:f3}-{4:f3} ({5} total)"
                                                 + "; tri area {6:f3}-{7:f3}",
                                                 Fmt.KMG(minTris), Fmt.KMG(maxTris),
                                                 Fmt.KMG(triStats.Sum(s => s.NumTris)),
                                                 minMeshArea, maxMeshArea, Fmt.KMG(triStats.Sum(s => s.MeshArea)),
                                                 minTriArea, maxTriArea);
                            writeLine(msg);

                            dumpTextureStats(triStats, "  ");
                        }
                    }
                }
            }

            var levels = nodes.GroupBy(n => n.Transform.Depth()).OrderBy(g => g.Key);

            foreach (var level in levels)
            {
                string msg = string.Format("level {0}: {1} tiles, {2} leaves",
                                           level.Key, level.Count(), level.Count(n => n.IsLeaf));

                var parents = level.Where(node => node.Children.Count() > 0).ToList();
                if (parents.Count > 0)
                {
                    int minBranch = parents.Min(node => node.Children.Count());
                    if (minBranch > 0)
                    {
                        msg += string.Format("; branching factor {0}", minBranch);
                        int maxBranch = parents.Max(node => node.Children.Count());
                        if (maxBranch > minBranch)
                        {
                            msg += string.Format("-{0}", maxBranch);
                        }
                    }
                }

                dumpLevel(level, msg);
            }

            var leaves = nodes.Where(node => node.IsLeaf);
            var leafLevels = leaves.Select(n => n.Transform.Depth()).DefaultIfEmpty(-1);
            dumpLevel(leaves, string.Format("{0} leaves at level(s) {1}-{2}",
                                            leaves.Count(), leafLevels.Min(), leafLevels.Max()));

            writeLine(string.Format("tile tree has {0} levels, {1} total tiles, {2} leaves",
                                    levels.Count(), nodes.Count, nodes.Count(node => node.IsLeaf)));

            var meshStats = nodes
                .Where(node => node.HasComponent<MeshImagePairStats>())
                .Select(node => node.GetComponent<MeshImagePairStats>())
                .Where(s => s.NumTris > 0)
                .ToList();

            writeLine(string.Format("{0} meshes, {1} textures, {2} triangles, {3} texels",
                                    meshStats.Count, meshStats.Count(s => s.NumPixels > 0),
                                    Fmt.KMG(meshStats.Sum(s => s.NumTris)),
                                    Fmt.KMG(meshStats.Sum(s => s.NumPixels))));

            if (meshStats.Count > 0)
            {
                dumpTextureStats(meshStats);
            }
        }
    }
}
