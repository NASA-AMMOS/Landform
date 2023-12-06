using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    public interface OctreeNodeContents
    {
        BoundingBox Bounds();
        bool Intersects(BoundingBox other);
        double SquaredDistance(Vector3 xyz);
    }

    /// <summary>
    /// Node class used to build Oct trees on triangles
    /// </summary>
    public class OctreeNode
    {
        // The depth of this node, root being at depth 0
        public int Depth;

        // The nodes parent in the tree
        public OctreeNode Parent;

        // List of node's 8 children, null if leaf node
        public OctreeNode[] Children;

        // The 3D region contained by this node and its ancestors in the tree
        public BoundingBox Bounds;

        // The Oct tree this node belongs to
        public Octree Owner;

        // List of triangles fully contained by this node but by none of its children
        public List<OctreeNodeContents> Contained;

        // Only for leaf nodes, a list of triangles with bounding boxes that intersect this.Bounds
        public List<OctreeNodeContents> Intersecting;


        public OctreeNode(Octree owner, OctreeNode parent, BoundingBox bounds, int depth)
        {
            this.Owner = owner;
            this.Parent = parent;
            this.Bounds = bounds;
            this.Depth = depth;
            Children = null;
            Contained = new List<OctreeNodeContents>();
            Intersecting = new List<OctreeNodeContents>();
        }

        /// <summary>
        /// Returns the minimum squared distance between the point p and this node's bounding box
        /// </summary>
        /// <param name="p"></param>
        /// <param name="Bounds"></param>
        /// <returns></returns>
        public double SquaredDistance(Vector3 p)
        {
            double xDist = 0;
            if (p.X < Bounds.Min.X) xDist = Bounds.Min.X - p.X;
            if (p.X > Bounds.Max.X) xDist = p.X - Bounds.Max.X;

            double yDist = 0;
            if (p.Y < Bounds.Min.Y) yDist = Bounds.Min.Y - p.Y;
            if (p.Y > Bounds.Max.Y) yDist = p.Y - Bounds.Max.Y;

            double zDist = 0;
            if (p.Z < Bounds.Min.Z) zDist = Bounds.Min.Z - p.Z;
            if (p.Z > Bounds.Max.Z) zDist = p.Z - Bounds.Max.Z;

            return xDist * xDist + yDist * yDist + zDist * zDist;
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
            Children = new OctreeNode[8];
            for (int i = 0; i < 8; i++)
            {
                Children[i] = new OctreeNode(Owner, this, ChildBounds(i), Depth + 1);
            }

            List<OctreeNodeContents> newContained = new List<OctreeNodeContents>();

            // Try to find a new child owner for each triangle
            foreach (OctreeNodeContents content in Contained)
            {
                BoundingBox contentBounds = content.Bounds();
                bool foundHome = false;

                foreach (OctreeNode child in Children)
                {
                    if (child.Bounds.Contains(contentBounds) == ContainmentType.Contains)
                    {
                        child.Contained.Add(content);
                        foundHome = true;
                        break;
                    }
                }
                if (!foundHome)
                {
                    newContained.Add(content);
                }
            }

            Contained = newContained;

            // for each child, construct a list of all triangles it intersects (including full containment)
            foreach (OctreeNodeContents content in Intersecting)
            {
                foreach (OctreeNode child in Children)
                {
                    if (content.Intersects(child.Bounds))
                    {
                        child.Intersecting.Add(content);
                    }
                }
            }

            Intersecting.Clear();

            Owner.NodeCount += 8;
            Owner.LeafCount += 7;
            if (Depth + 1 > Owner.Deepest)
            {
                Owner.Deepest = Depth + 1;
            }
            return true;
        }

        public bool IsEmpty()
        {
            if (Contained.Count > 0)
            {
                return false;
            }

            if (!IsLeaf())
            {
                foreach (OctreeNode child in Children)
                {
                    if (!child.IsEmpty())
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}