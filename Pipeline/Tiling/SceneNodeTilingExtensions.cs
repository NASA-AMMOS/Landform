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
            return TextureBaker.PixelAreaToSquareDimension(totalPixels);
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
        public static void BuildGeometryFromChildren(this SceneNode node, SceneNode root, int maxFaceCountTarget, int maxTextureSize, SkirtMode? skirtAxis, double childBoundSearchRatio = DEFAULT_SEARCH_RATIO)
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

            Mesh combinedFull = Mesh.Merge(childMeshesWithoutSkirts);
            combinedFull = Mesh.Clip(combinedFull, searchBounds);
            combinedFull.NormalizeNormals();
            // TODO: handle the fact that we are reconstucting a larger area so we should inflate the number of faces cleverly
            // TODO: Don't use 3 that only works for quad trees, this should be based off number of children
            int targetFaces = combinedFull.Faces.Count() / node.ChildCount/*3*/;  // could do 4 but lets try 3 for some extra around the edges
            targetFaces = Math.Min(targetFaces, maxFaceCountTarget);
            // Minimum bounds is a tight fitting bounding box around the child meshes with skirts
            BoundingBox minimumBounds = node.GetComponent<NodeBounds>().Bounds;
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
            Mesh combinedDecimated = combinedFull.ResampleDecimation(targetFaces, clippingBounds: minimumBounds, cornerDirection: cornerDirection);
            combinedDecimated.Clean();

            NodeGeometricError geoError = node.GetOrAddComponent<NodeGeometricError>();
            Mesh fullClipped = Mesh.Clip(combinedFull, minimumBounds);
            double geometricError = combinedDecimated.HausdorffDistance(fullClipped);
            geoError.Error = Math.Max(geoError.Error, geometricError);

            // We want a 2x reduction in both size dimensions (4x reduction in area)
            int size = ComputeParentTileResolution(pairs, combinedDecimated.Bounds()) / 2;
            Image img = null;
            if (size != 0)
            {
                size = Math.Min(size, maxTextureSize);
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
                // TODO remove skirt stuff - maintian unskrited meshes in the tree and only skirt them when saving
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
