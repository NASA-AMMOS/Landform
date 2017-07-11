using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Util;

namespace OPS.Pipeline
{
    /// <summary>
    /// Given a scene node, build a Tile3D tileset using the node as the root
    /// </summary>
    public class Tile3DBuilder
    {
        const string TEXTURE_EXTENSION = "png";  // Should be depricated when we move to glTF
        public delegate string NodeToRelativeUrl(SceneNode node);

        public Tile3D.Tileset Tileset { get; private set; }
        SceneNode Root;
        Dictionary<SceneNode, Tile3D.Tile> nodesToTiles = new Dictionary<SceneNode, Tile3D.Tile>();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="root">the root node to be used in the tileset</param>
        public Tile3DBuilder(SceneNode root)
        {
            this.Root = root;
        }

        /// <summary>
        /// Build the tileset
        /// </summary>
        /// <param name="nodeToUrl">Method that returns the asset url to use for a given node</param>
        public void BuildTileset(NodeToRelativeUrl nodeToUrl)
        {
            foreach(SceneNode curNode in Root.DepthFirstTraverse())
            {
                if(!nodesToTiles.ContainsKey(curNode))
                {
                    var tile = SceneNodeToTile(curNode, nodeToUrl);
                    nodesToTiles.Add(curNode, tile);
                    // Should only be null for root node
                    if(curNode.Transform.Parent != null)
                    {
                        nodesToTiles[curNode.Transform.Parent.Node].children.Add(tile);
                    }
                }
            }
            this.Tileset = new Tile3D.Tileset();
            this.Tileset.root = nodesToTiles[Root];
            this.Tileset.geometricError = 0;
        }

        /// <summary>
        /// Calculates the geometric error between all children and thir parent tiles using
        /// Bidirectional Hausdorff Distance
        /// </summary>
        public void CalculateGeometricError()
        {
            Parallel.ForEach(Root.DepthFirstTraverse(), curNode =>
            {
                var tile = nodesToTiles[curNode];
                tile.geometricError = CalculateGeometricError(curNode);
                if (curNode == this.Root)
                {
                    this.Tileset.geometricError = tile.geometricError;
                }
            });
        }

        /// <summary>
        /// Create a 3DTile for a node
        /// </summary>
        /// <param name="node"></param>
        /// <param name="nodeToUrl"></param>
        /// <returns></returns>
        Tile3D.Tile SceneNodeToTile(SceneNode node, NodeToRelativeUrl nodeToUrl)
        {
            Tile3D.Tile tile = new Tile3D.Tile(node.Bounds);
            tile.refine = Tile3D.RefineMode.replace;
            if(node.GetComponent<MeshImagePair>() != null)
            {
                tile.content = new Tile3D.Content();
                tile.content.url = nodeToUrl(node);
                tile.content.textureExension = TEXTURE_EXTENSION;
            }
            return tile;
        }

        /// <summary>
        /// Returns max difference between node and its children
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        double CalculateGeometricError(SceneNode node)
        {
            if(node.Transform.ChildCount == 0)
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
                parent = parent.Transform.Parent.Node;
            }
            // Get first set of dicendants that have meshes
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
            // return distance
            return Distance(parentMesh, childrenMeshes.ToArray());
        }

        /// <summary>
        /// Compute difference between parent mesh and a list of children
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="children"></param>
        /// <returns></returns>
        double Distance(Mesh parent, params Mesh[] children)
        {
            Mesh merged = Mesh.Merge(parent.HasNormals, parent.HasUVs, parent.HasColors, children);
            if(!parent.Bounds().Intersects(merged.Bounds()))
            {
                return merged.Bounds().MaxDimension();
            }
            return MeshLab.BidirectionalHausdorffDistance(parent, merged).Max;
        }
    }
}
