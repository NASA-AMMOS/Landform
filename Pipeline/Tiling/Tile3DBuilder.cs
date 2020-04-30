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
        /// Converts an AABB to a 3D Tiles Box bound array
        /// </summary>
        /// <param name="b"></param>
        /// <returns></returns>
        static public List<double> BoundsToBox(BoundingBox b)
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

        static public BoundingBox BoxToBounds(List<double> box)
        {
            Vector3 center = new Vector3(box[0], box[1], box[2]);
            Vector3 halfX = new Vector3(box[3], box[4], box[5]);
            Vector3 halfY = new Vector3(box[6], box[7], box[8]);
            Vector3 halfZ = new Vector3(box[9], box[10], box[11]);
            Vector3 min = center - halfX - halfY - halfZ;
            Vector3 max = center + halfX + halfY + halfZ;
            return new BoundingBox(min, max);
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
    }
}
