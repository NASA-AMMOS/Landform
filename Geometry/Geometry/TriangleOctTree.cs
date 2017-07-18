using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using OPS.Geometry;
using OPS.Imaging;

// Reimplemented Oct tree on triangles for fast nearest neighbot lookup on meshes
// From https://github.jpl.nasa.gov/OpsLab/SidekickPipeline/blob/dmc-again/OccluderGen/octree.hxx#L583

namespace OPS.Geometry
{
    /// <summary>
    /// Container for Triangle that also stores its corresponding texture
    /// </summary>
    public class TexturedTriangle
    {
        public TexturedTriangle(Triangle tri, Image texture)
        {
            this.tri = tri;
            this.texture = texture;
        }

        public Triangle tri;
        public Image texture;
    }

    /// <summary>
    /// Node class used to build Oct trees on triangles
    /// </summary>
    public class Node
    {
        // The depth of this node, root being at depth 0
        public int Depth;

        // The nodes parent in the tree
        public Node Parent;

        // List of node's 8 children, null if leaf node
        public Node[] Children;

        // The 3D region contained by this node and its ancestors in the tree
        public BoundingBox Bounds;

        // The Oct tree this node belongs to
        public TriangleOctTree Owner;

        // List of triangles fully contained by this node but by none of its children
        public List<TexturedTriangle> Contained;

        // Only for leaf nodes, a list of triangles with bounding boxes that intersect this.Bounds
        public List<TexturedTriangle> Intersecting;


        public Node(TriangleOctTree owner, Node parent, BoundingBox bounds, int depth)
        {
            this.Owner = owner;
            this.Parent = parent;
            this.Bounds = bounds;
            this.Depth = depth;
            Children = null;
            Contained = new List<TexturedTriangle>();
            Intersecting = new List<TexturedTriangle>();
        }

        /// <summary>
        /// Get the bounding box of this node's ith child
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public BoundingBox ChildBounds(int i)
        {
            double sizeX = (Bounds.Max.X - Bounds.Min.X) / 2;
            double sizeY = (Bounds.Max.Y - Bounds.Min.Y) / 2;
            double sizeZ = (Bounds.Max.Z - Bounds.Min.Z) / 2;
            double xOffset = (i & 0x01) != 0 ? sizeX : 0;
            double yOffset = (i & 0x02) != 0 ? sizeY : 0;
            double zOffset = (i & 0x04) != 0 ? sizeZ : 0;
            Vector3 min = new Vector3(Bounds.Min.X + xOffset, Bounds.Min.Y + yOffset, Bounds.Min.Z + zOffset);
            Vector3 max = new Vector3(min.X + sizeX, min.Y + sizeY, min.Z + sizeZ);
            return new BoundingBox(min, max);
        }

        public bool IsLeaf()
        {
            return Children == null;
        }

        /// <summary>
        /// Initialize node's 8 children and push down ownership of triangles
        /// </summary>
        /// <returns></returns>
        public bool Split()
        {
            if (!IsLeaf())
                return false;
            Children = new Node[8];
            for(int i = 0; i < 8; i++)
            {
                Children[i] = new Node(Owner, this, ChildBounds(i), Depth + 1);
            }

            List<TexturedTriangle> newContained = new List<TexturedTriangle>();

            // Try to find a new child owner for each triangle
            foreach (TexturedTriangle txtTri in Contained)
            {
                BoundingBox triBounds = txtTri.tri.Bounds();
                bool foundHome = false;

                foreach (Node child in Children)
                {
                    if(child.Bounds.Contains(triBounds) == ContainmentType.Contains)
                    {
                        child.Contained.Add(txtTri);
                        foundHome = true;
                        break;
                    }
                }
                if(!foundHome)
                {
                    newContained.Add(txtTri);
                }
            }

            Contained = newContained;

            // for each child, construct a list of all triangles it intersects (including full containment)
            foreach(TexturedTriangle txtTri in Intersecting)
            {
                foreach (Node child in Children)
                {
                    if(TriangleOctTree.TriIntersectsBox(txtTri.tri, child.Bounds))
                    {
                        child.Intersecting.Add(txtTri);
                    }
                }
            }

            Intersecting.Clear();

            Owner.NodeCount += 8;
            Owner.LeafCount += 7;

            return true;
        }
    }

    /// <summary>
    /// Efficient structure for finding nearest neighbor on a set of triangles
    /// </summary>
    public class TriangleOctTree
    {
        // The root of the oct tree
        public Node Root;

        // The maximum number of nodes appearing in a node's Contained list before splitting
        public int MaxNodeSize;

        // Maximum depth of the tree, nodes at this depth will not be split
        public int MaxDepth;

        // The number of nodes in the tree
        public int NodeCount;

        // The number of leaf nodes in the tree
        public int LeafCount;

        public TriangleOctTree(BoundingBox bounds, int maxNodeSize = 10, int maxDepth = 8)
        {
            this.MaxNodeSize = maxNodeSize;
            this.MaxDepth = maxDepth;
            this.Root = new Node(this, null, bounds, 0);
            this.NodeCount = 1;
            this.LeafCount = 1;
        }

        /// <summary>
        /// Recurses down the tree calling function applyFunc on each node if should_expand returns true on that node
        /// Note: When applyFunc is NOT called on a node, this function will ignore the node's children
        /// </summary>
        /// <param name="applyFunc"></param>
        /// <param name="should_expand"></param>
        public void Visit(Action<Node> applyFunc, Func<Node, bool> should_expand)
        {
            Queue<Node> toVisit = new Queue<Node>();
            toVisit.Enqueue(Root);
            while(toVisit.Count != 0)
            {
                Node node = toVisit.Dequeue();
                if (!should_expand(node))
                    continue;

                applyFunc(node);
                if (node.IsLeaf())
                    continue;

                for (int i = 0; i < 8; i++)
                    toVisit.Enqueue(node.Children[i]);
            }
        }

        /// <summary>
        /// Traverses the tree from Node start calling function applyFunc to each node if should_expand returns true on that node
        /// Note: When applyFunc is NOT called on a node, this function will ignore the node's children
        /// </summary>
        /// <param name="start"></param>
        /// <param name="applyFunc"></param>
        /// <param name="should_expand"></param>
        public void VisitFrom(Node start, Action<Node> applyFunc, Func<Node, bool> should_expand)
        {
            HashSet<Node> visited = new HashSet<Node>();

            Queue<Node> toVisit = new Queue<Node>();
            toVisit.Enqueue(start);
            while(toVisit.Count != 0)
            {
                // Get the next node to be searched from the queue
                Node node = toVisit.Dequeue();
                if (visited.Contains(node))
                    continue;
                visited.Add(node);

                // Expand this node
                if(should_expand(node))
                {
                    applyFunc(node);

                    if (!node.IsLeaf())
                        for (int i = 0; i < 8; i++)
                            toVisit.Enqueue(node.Children[i]);
                }

                // Add this node's parent to the queue if not already searched
                if (node.Parent != null && !visited.Contains(node.Parent))
                    toVisit.Enqueue(node.Parent);              
            }
        }

        /// <summary>
        /// Inserts a single triangle into the Oct tree. The triangle will have a designated owner that fully contains its bounds.
        /// Child nodes of owner will also store if they intersect the triangle bounds
        /// </summary>
        /// <param name="txtTri"></param>
        public void Insert(TexturedTriangle txtTri)
        {
            // find smallest fully containing node
            BoundingBox triBounds = txtTri.tri.Bounds();
            Node owner = Root;
            bool foundChild = true;

            while (foundChild && !owner.IsLeaf())
            {
                foundChild = false;
                for (int i = 0; i < 8; i++)
                {
                    Node child = owner.Children[i];
                    if (child.Bounds.Contains(triBounds) == ContainmentType.Contains)
                    {
                        foundChild = true;
                        owner = child;
                        break;
                    }
                }
            }

            owner.Contained.Add(txtTri);

            // fix intersecting lists as well
            Visit( node =>
            {
                if (!node.IsLeaf())
                    return;
                node.Intersecting.Add(txtTri);
            }, node =>
            {
                return TriIntersectsBox(txtTri.tri, node.Bounds);
            });
        }
        /// <summary>
        /// Loops over the triangles in mesh and adds them to the Oct tree, then splits nodes from the root down as needed
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="meshImage"></param>
        public void InsertMesh(Mesh mesh, Image meshImage)
        {
           foreach(Triangle tri in mesh.Triangles())
            {
                TexturedTriangle txtTri = new TexturedTriangle(tri, meshImage);
                Insert(txtTri);
            }
            Visit(node =>
            {
                if (node.Depth >= MaxDepth)
                    return;

                if (node.IsLeaf() && node.Contained.Count > MaxNodeSize)
                {
                    node.Split();
                }
            }, node => { return true; });
        }

        /// <summary>
        /// Returns the minimum squared distance between the point p and bounding box
        /// </summary>
        /// <param name="p"></param>
        /// <param name="box"></param>
        /// <returns></returns>
        public static double SquaredDistToBox(Vector3 p, BoundingBox box)
        {
            double xDist = 0;
            if (p.X < box.Min.X) xDist = box.Min.X - p.X;
            if (p.X > box.Max.X) xDist = p.X - box.Max.X;

            double yDist = 0;
            if (p.Y < box.Min.Y) yDist = box.Min.Y - p.Y;
            if (p.Y > box.Max.Y) yDist = p.Y - box.Max.Y;

            double zDist = 0;
            if (p.Z < box.Min.Z) zDist = box.Min.Z - p.Z;
            if (p.Z > box.Max.Z) zDist = p.Z - box.Max.Z;

            return xDist * xDist + yDist * yDist + zDist * zDist;
        }

        /// <summary>
        /// Returns true if the BOUNDING BOX of Triangle tri intersects other bounding box
        /// Can potentially cause false positives but faster
        /// </summary>
        /// <param name="tri"></param>
        /// <param name="box"></param>
        /// <param name="checkXYZ"></param>
        /// <returns></returns>
        public static bool TriIntersectsBox(Triangle tri, BoundingBox box)
        {
            if (box.Contains(tri.Bounds()) == ContainmentType.Disjoint)
                return false;
            return true;

        }

        /// <summary>
        /// Returns the closest triangle (with its texture) on the mesh to query point xyz. Returns null if mesh is empty
        /// </summary>
        /// <param name="xyz"></param>
        /// <returns></returns>
        public TexturedTriangle Closest(Vector3 xyz)
        {
            return Closest(xyz, Root);
        }

        /// <summary>
        /// Returns the closest triangle (with its texture) on the mesh to query point xyz using node start as initial guess. Returns null if mesh is empty
        /// </summary>
        /// <param name="xyz"></param>
        /// <param name="start"></param>
        /// <returns></returns>
        public TexturedTriangle Closest(Vector3 xyz, Node start)
        {
            double closestDist = Double.MaxValue;
            TexturedTriangle closestTri = null;

            // checks all triangles in leaf node and updates closest to query point
            Action<Node> searchNode = node => {
                if(!node.IsLeaf())
                {
                    return;
                }
                foreach(TexturedTriangle txtTri in node.Intersecting)
                {
                    double dist = txtTri.tri.SqrdDistToTri(xyz);
                    if(dist < closestDist)
                    {
                        closestDist = dist;
                        closestTri = txtTri;
                    }
                }

            };

            // returns true if node could contain the closest triangle
            Func<Node, bool> should_expand = node => {
                return SquaredDistToBox(xyz, node.Bounds) <= closestDist + 1e-8;
            };

            // search for the closest triangle using initial guess `start`
            VisitFrom(start, searchNode, should_expand);

            return closestTri;
        }

        /// <summary>
        /// returns the closest triangle on the mesh to query point `xyz` using node `start` as initial guess. Returns null if mesh is empty.
        /// Additionally returns the last node visited via outpointer end (useful as starting point of next search)
        /// </summary>
        /// <param name="xyz"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        public TexturedTriangle Closest(Vector3 xyz, Node start, out Node end)
        {
            double closestDist = Double.MaxValue;
            TexturedTriangle closestTri = null;

            Node endNode = Root;

            // checks all triangles in leaf node and updates closest to query point
            Action<Node> searchNode = node => {
                if (!node.IsLeaf())
                {
                    return;
                }
                foreach (TexturedTriangle txtTri in node.Intersecting)
                {
                    double dist = txtTri.tri.SqrdDistToTri(xyz);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestTri = txtTri;
                        endNode = node;
                    }
                }

            };

            // returns true if node could contain the closest triangle
            Func<Node, bool> should_expand = node => {
                return SquaredDistToBox(xyz, node.Bounds) <= closestDist + 1e-8;
            };

            // search for the closest triangle using initial guess `start`
            VisitFrom(start, searchNode, should_expand);

            end = endNode;

            return closestTri;
        }
    }
}