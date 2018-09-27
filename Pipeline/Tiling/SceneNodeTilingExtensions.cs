using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        public const int MIN_FACES_FOR_RECONSTRUCTION = 50;

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
        public static void BuildGeometryFromChildren(this SceneNode node, SceneNode root, MeshReconMethod reconstructionMethod, int maxFaceCountTarget, int maxTextureSize, SkirtMode? skirtAxis, double childBoundSearchRatio = DEFAULT_SEARCH_RATIO)
        {
            BoundingBox searchBounds;
            var childNodes = FindNodesRequiredForParent(node, root, out searchBounds, childBoundSearchRatio);
            var pairs = childNodes.Select(n => n.GetComponent<MeshImagePair>());
            var childMeshesWithoutSkirts = pairs.Select(p =>
            {
                var tmp = new Mesh(p.Mesh);
                if (skirtAxis.HasValue)
                {
                    // TODO remove skirt stuff
                    tmp.RemoveSkirt(skirtAxis.Value);
                }
                return tmp;
            }).ToArray();

            Mesh combinedFull = Mesh.MergeWithCommonAttributes(childMeshesWithoutSkirts);
            if (!combinedFull.HasNormals)
            {
                combinedFull.GenerateVertexNormals();
            }
            combinedFull = Mesh.Clip(combinedFull, searchBounds);
            combinedFull.NormalizeNormals();
            BoundingBox minimumBounds = node.GetComponent<NodeBounds>().Bounds;

            // We limit target faces only to maxface count.  This means there will be little to no face reduction
            // until the face limit is hit. This favors trying to make all tiles the same complexity rather than trying to always have a
            // constant amount of leaf/parent tile complexity reduction.  This choice primarily affects parent tiles near leafs.
            Mesh combinedDecimated = null;
            Mesh combinedFullClipped = Mesh.Clip(combinedFull, minimumBounds);
            // Resample decimation can fail on meshes with very few faces.  If we are below the threshold where we expect this to fail just
            // pass along the geometry assuming it is less than maxFaceCount
            if (combinedFullClipped.Faces.Count < MIN_FACES_FOR_RECONSTRUCTION && combinedFullClipped.Faces.Count <= maxFaceCountTarget)
            {
                combinedDecimated = combinedFullClipped;
            }
            else
            { 
                // Note: that we choose to do a resample decimation even when we have fewer than maxFaceCountTarget
                // We could consider just passing along the combinedFullClipped geometry but doing a decimation here 
                // probably helps avoid propegating topological issues.  This would be a good thing to investigate.
                int targetFaces = combinedFullClipped.Faces.Count();
                targetFaces = Math.Min(targetFaces, maxFaceCountTarget);
                // Minimum bounds is a tight fitting bounding box around the child meshes with skirts
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
                        cornerDirection = Vector3.UnitY;
                    }
                }
                combinedDecimated = combinedFull.ResampleDecimation(reconstructionMethod, targetFaces, clippingBounds: minimumBounds, cornerDirection: cornerDirection);
            }
            combinedDecimated.Clean();

            NodeGeometricError geoError = node.GetOrAddComponent<NodeGeometricError>();
            Mesh fullClipped = Mesh.Clip(combinedFull, minimumBounds);
            double geometricError = combinedDecimated.HausdorffDistance(fullClipped);
            geoError.Error = Math.Max(geoError.Error, geometricError);

            int size = ComputeParentTileResolution(pairs, combinedDecimated.Bounds(), maxTextureSize);
            Image img = null;
            if (size != 0)
            {               
                combinedDecimated = UVAtlas.Atlas(combinedDecimated, size, size);
                img = TextureBaker.BakeTexture(pairs.ToArray(), combinedDecimated, size, size);
                // Estimate the size of a pixel for this texture.  If this is greater than the geometric error use it instead
                var ext = minimumBounds.Extent();
                double sizePerPixel = new Vector2(ext.X, ext.Z).Length() / new Vector2(size / 2, size / 2).Length();
                geoError.Error = Math.Max(geoError.Error, sizePerPixel);
            }
            combinedDecimated.GenerateVertexNormals();
            if (skirtAxis.HasValue)
            {
                combinedDecimated.AddSkirt(skirtAxis.Value);
            }
            // We need to combine bounds here because decimated bounds may be smaller than the child bounds
            var bounds = BoundingBox.CreateMerged(combinedDecimated.Bounds(), minimumBounds);
            node.GetComponent<NodeBounds>().Bounds = bounds;
            // Add new mesh and image to parent
            node.AddComponent(new MeshImagePair(combinedDecimated, img));

            // Ensure geo error is at least as large as children
            foreach (var child in node.Children)
            {
                var error = child.GetComponent<NodeGeometricError>().Error;
                geoError.Error = Math.Max(error, geoError.Error);
            }
        }

        /// <summary>
        /// Given a list of nodes, connect them in a tree based on name prefix convention and return the root
        /// </summary>
        /// <param name="nodes"></param>
        /// <returns></returns>
        public static SceneNode ConnectNodesByName(List<SceneNode> nodes)
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
        public static void ComputeBounds(SceneNode root)
        {
            HashSet<SceneNode> curParents = new HashSet<SceneNode>();
            foreach (var leaf in root.Leaves())
            {
                var pair = leaf.GetComponent<MeshImagePair>();
                leaf.GetOrAddComponent<NodeBounds>().Bounds = pair.Mesh.Bounds();
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
                    p.GetOrAddComponent<NodeBounds>().Bounds = BoundingBoxExtensions.Union(p.Children.Select(c => c.GetOrAddComponent<NodeBounds>().Bounds).ToArray());
                    if(p.HasComponent<MeshImagePair>() && p.GetComponent<MeshImagePair>().Mesh != null)
                    {
                        p.GetComponent<NodeBounds>().Bounds = BoundingBoxExtensions.Union(p.GetComponent<MeshImagePair>().Mesh.Bounds(), p.GetComponent<NodeBounds>().Bounds);
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
    }
}
