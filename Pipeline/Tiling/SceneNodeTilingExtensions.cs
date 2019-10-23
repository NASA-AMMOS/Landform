using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;
using OPS.Util;

namespace OPS.Pipeline
{
    public static class SceneNodeTilingExtensions
    {

        public const double DEFAULT_SEARCH_RATIO = 1.1f;

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

        public static int ComputeParentTileResolution(IEnumerable<MeshImagePair> pairs, BoundingBox cropBounds, int maxTextureSize = int.MaxValue)
        {
            // Read all overlapping meshes, crop each to the extent of the leaf tile
            // and calculate the area the triangles occupy in units of pixels.  Sum all
            // the areas and round up to nearest power of two to decide size of the new tile
            double totalPixels = 0;
            foreach (var p in pairs)
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

        public static List<SceneNode> FindNodesRequiredForParent(this SceneNode node, SceneNode root, double childBoundSearchRatio = DEFAULT_SEARCH_RATIO)
        {
            BoundingBox tmp;
            return FindNodesRequiredForParent(node, root, out tmp, childBoundSearchRatio);
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
        public static List<SceneNode> FindNodesRequiredForParent(this SceneNode node, SceneNode root, out BoundingBox searchBounds, double childBoundSearchRatio = DEFAULT_SEARCH_RATIO)
        {
            int childDepth = node.Children.First().Transform.Depth();
            searchBounds = node.ChildBounds();
            searchBounds = BoundingBoxExtensions.Scale(searchBounds, childBoundSearchRatio);
            var childNodes = root.FindOverlapingNodes(childDepth, searchBounds);
            return childNodes;

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
        public static bool BuildGeometryFromChildren(this SceneNode node, SceneNode root,
                                                     MeshReconMethod reconstructionMethod, int maxFaceCountTarget,
                                                     int maxTextureSize, SkirtMode? skirtAxis,
                                                     double childBoundSearchRatio = DEFAULT_SEARCH_RATIO,
                                                     Action<string> info = null, Action<string> error = null)
        {
            info = info ?? (msg => {});
            error = error ?? (msg => {});

            info("merging child meshes");

            BoundingBox searchBounds;
            var childNodes = FindNodesRequiredForParent(node, root, out searchBounds, childBoundSearchRatio);
            var pairs = childNodes.Where(n => n.HasComponent<MeshImagePair>()).Select(n => n.GetComponent<MeshImagePair>());
            var childMeshes = pairs.Where(p => p.Mesh != null).Select(p => p.Mesh);

            Mesh combinedFull = Mesh.MergeWithCommonAttributes(childMeshes.ToArray(), clean:true, normalize:true);
            if (!combinedFull.HasNormals)
            {
                combinedFull.GenerateVertexNormals();
            }
            BoundingBox minimumBounds = node.GetComponent<NodeBounds>().Bounds;
            // Note that we compute an enlargedMinBounds instead of just using searchBounds because in the tiling server "BuildParent" routine we
            // construct a flat tree with just the parent node and all of its dependence as children.  As a result "ChildBounds" is no longer a reliable
            // measure.  This is pretty nuanced and could potentially benefit from a refactor in the future
            BoundingBox enlargedMinBounds = BoundingBoxExtensions.Scale(minimumBounds, childBoundSearchRatio);
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
                    if (skirtAxis.Value == SkirtMode.X)
                    {
                        cornerDirection = Vector3.UnitX;
                    }
                    else if (skirtAxis.Value == SkirtMode.Y)
                    {
                        cornerDirection = Vector3.UnitY;
                    }
                    else if (skirtAxis.Value == SkirtMode.Z)
                    {
                        cornerDirection = Vector3.UnitZ;
                    }
                }
                info("decimating parent tile mesh");
                combinedDecimated = combinedFull.ResampleDecimation(reconstructionMethod, maxFaceCountTarget,
                                                                    clippingBounds: minimumBounds,
                                                                    cornerDirection: cornerDirection);
            }

            info("cleaning parent tile mesh");
            combinedDecimated.Clean();

            info("computing parent tile geometric error and resolution");
            NodeGeometricError geoError = node.GetOrAddComponent<NodeGeometricError>();
            double accuracy = combinedDecimated.Bounds().MaxDimension() * 0.005; // 0.5 percent of max bounds 
            double geometricError = combinedDecimated.HausdorffDistance(accuracy, fullClipped);
            geoError.Error = Math.Max(geoError.Error, geometricError);

            int size = ComputeParentTileResolution(pairs, combinedDecimated.Bounds(), maxTextureSize);

            Image img = null;
            if (size != 0)
            {               
                info(string.Format("atlasing parent tile with UVAtlas, resolution {0}", size));
                combinedDecimated = UVAtlas.Atlas(combinedDecimated, size, size);

                info("baking parent tile texture");
                img = TextureBaker.BakeTexture(pairs.ToArray(), combinedDecimated, size, size);

                // Estimate the size of a pixel for this texture
                // If this is greater than the geometric error use it instead
                var ext = minimumBounds.Extent();
                double sizePerPixel = new Vector2(ext.X, ext.Z).Length() / new Vector2(size / 2, size / 2).Length();
                geoError.Error = Math.Max(geoError.Error, sizePerPixel);
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
            node.AddComponent(new MeshImagePair(combinedDecimated, img));

            // Ensure geo error is at least as large as children
            foreach (var child in node.Children)
            {
                geoError.Error = Math.Max(child.GetComponent<NodeGeometricError>().Error, geoError.Error);
            }

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
                    leaf.GetOrAddComponent<NodeBounds>().Bounds = pair.Mesh.Bounds();
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
                        BoundingBoxExtensions.Union(p.Children.Select(c => c.GetOrAddComponent<NodeBounds>().Bounds).ToArray());
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
        /// Returns this tree as a set of groups where each group contains all the nodes at a given depth
        /// The first group is the deapest and the last group is the shallowest (containing only the root of the tree)
        /// </summary>
        /// <param name="root"></param>
        /// <returns></returns>
        public static IOrderedEnumerable<IGrouping<int, SceneNode>> GetReverseDepthGroups(this SceneNode root)
        {
            return root.DepthFirstTraverse().Where(n => !n.IsLeaf).GroupBy(n => n.Transform.Depth()).OrderBy(g => -g.Key);
        }

        /// <summary>
        /// Returns max difference between node and its children
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public static double CalculateGeometricError(this SceneNode node)
        {
            if (node.Transform.ChildCount == 0)
            {
                return 0;
            }
            // If this node doesn't have a mesh get the first parent mesh 
            // supports case when some parent nodes dont have meshes
            Mesh parentMesh = null;
            SceneNode parent = node;
            while(parent != null)
            {
                var pair = parent.GetComponent<MeshImagePair>();
                if(pair != null && pair.Mesh != null)
                {
                    parentMesh = pair.Mesh;
                    break;
                }

                //if hit root bail out
                if (parent.Transform.Parent == null)
                    break;

                parent = parent.Transform.Parent.Node;
            }

            if(parentMesh == null)
            {
                return 0; //no meshes including or above this node
            }

            // Get first set of descendants that have meshes
            List<Mesh> childrenMeshes = new List<Mesh>();
            Queue<SceneNode> childrenQueue = new Queue<SceneNode>();
            foreach (var n in node.Children)
            {
                childrenQueue.Enqueue(n);
            }
            while (childrenQueue.Count > 0)
            {
                SceneNode curNode = childrenQueue.Dequeue();
                var pair = curNode.GetComponent<MeshImagePair>();
                if (pair != null && pair.Mesh != null)
                {
                    childrenMeshes.Add(pair.Mesh);
                }
                else
                {
                    foreach (var n in curNode.Children)
                    {
                        childrenQueue.Enqueue(n);
                    }
                }
            }
            // If there are no children with meshes there is no error
            if (childrenMeshes.Count == 0)
            {
                return 0;
            }
            return parentMesh.HausdorffDistance(childrenMeshes.ToArray());
        }
    }
}
