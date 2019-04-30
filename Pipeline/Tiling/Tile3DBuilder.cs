using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OPS.Geometry;
using OPS.Util;
using Microsoft.Xna.Framework;

namespace OPS.Pipeline
{
    /// <summary>
    /// Given a scene node, build a Tile3D tileset using the node as the root
    /// </summary>
    public class Tile3DBuilder
    {
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
        public void BuildTileset(NodeToRelativeUrl nodeToUrl, bool useCesiumHackTransform = false)
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
                        nodesToTiles[curNode.Transform.Parent.Node].Children.Add(tile);
                    }
                }
            }
            this.Tileset = new Tile3D.Tileset();
            this.Tileset.Root = nodesToTiles[Root];
            this.Tileset.GeometricError = this.Root.GetOrAddComponent<NodeBounds>().Bounds.MaxDimension();

            if (useCesiumHackTransform)
            {
                // Put together a hack matrix for viewing in cesium
                Matrix m = new Matrix(96.86356343768793, 24.848542777253734, 0, 0,
                 -15.986465724980844, 62.317780594908875, 76.5566922962899, 0,
                 19.02322243409411, -74.15554020821229, 64.3356267137516, 0,
                 1215107.7612304366, -4736682.902037748, 4081926.095098698, 1);
                Vector3 scale;
                Quaternion q;
                Vector3 trans;
                m.Decompose(out scale, out q, out trans);
                scale = new Vector3(1, 1, 1);
                Matrix rot = Matrix.CreateRotationX(MathHelper.ToRadians(90)) * Matrix.CreateFromQuaternion(q);
                m = Matrix.CreateScale(scale) * rot * Matrix.CreateTranslation(trans);
                m.Decompose(out scale, out q, out trans);
                this.Tileset.Root.Transform = MatrixToList(m);
            } else
            {
                this.Tileset.Root.Transform = MatrixToList(Matrix.Identity);
            }
        }

        public static List<double> MatrixToList(Matrix m)
        {            
            return new double[]
            {
                m.M11, m.M12,m.M13,m.M14,
                m.M21, m.M22,m.M23,m.M24,
                m.M31, m.M32,m.M33,m.M34,
                m.M41, m.M42,m.M43,m.M44
            }.ToList();
        }

        Matrix ListToMatrix(List<double> list)
        {
           return new Matrix(list[0], list[1], list[2], list[3],
                             list[4], list[5], list[6], list[7],
                             list[8], list[9], list[10], list[11],
                             list[12], list[13], list[14], list[15]);
        }

        /// <summary>
        /// Calculates the geometric error between all children and thir parent tiles using
        /// Bidirectional Hausdorff Distance
        /// </summary>
        public void CalculateGeometricError()
        {
            CoreLimitedParallel.ForEach(Root.DepthFirstTraverse(), curNode =>
            {
                if (!curNode.HasComponent<NodeGeometricError>())
                {
                    curNode.AddComponent<NodeGeometricError>(new NodeGeometricError(CalculateGeometricError(curNode)));
                }
            });
        }

        /// <summary>
        /// Converts an AABB to a 3D Tiles Box bound array
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        List<double> BoundsToBox(BoundingBox b)
        {
            // "description" : "An array of 12 numbers that define an oriented bounding box.  
            // The first three elements define the x, y, and z values for the center of the box.  
            // The next three elements (with indices 3, 4, and 5) define the x axis direction and half-length.  
            // The next three elements (indices 6, 7, and 8) define the y axis direction and half-length.  
            // The last three elements (indices 9, 10, and 11) define the z axis direction and half-length.",
            Vector3 halfExtent = b.Extent()/2;
            Vector3 center = b.Center();
            Vector3 xaxis = new Vector3(halfExtent.X, 0, 0);
            Vector3 yaxis = new Vector3(0, halfExtent.Y, 0);
            Vector3 zaxis = new Vector3(0, 0, halfExtent.Z);
            var box = new double[] { center.X, center.Y, center.Z, xaxis.X, xaxis.Y, xaxis.Z, yaxis.X, yaxis.Y, yaxis.Z, zaxis.X, zaxis.Y, zaxis.Z };
            return new List<double>(box);
        }

        /// <summary>
        /// Create a 3DTile for a node
        /// </summary>
        /// <param name="node"></param>
        /// <param name="nodeToUrl"></param>
        /// <returns></returns>
        Tile3D.Tile SceneNodeToTile(SceneNode node, NodeToRelativeUrl nodeToUrl)
        {
            Tile3D.Tile tile = new Tile3D.Tile();
            tile.BoundingVolume.Box = BoundsToBox(node.GetOrAddComponent<NodeBounds>().Bounds);
            tile.Refine = Tile3D.TileRefine.REPLACE;
            if(node.GetComponent<MeshImagePair>() != null)
            {
                tile.Content = new Tile3D.TileContent();
                tile.Content.Uri = nodeToUrl(node);
            }
            if(node.HasComponent<NodeGeometricError>())
            {
                tile.GeometricError = node.GetComponent<NodeGeometricError>().Error;
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
            return parentMesh.HausdorffDistance(childrenMeshes.ToArray());
        }
    }
}
