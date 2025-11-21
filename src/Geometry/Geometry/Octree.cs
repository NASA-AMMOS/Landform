using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

// Reimplemented Oct tree on triangles for fast nearest neighbor lookup on meshes
// From https://github.jpl.nasa.gov/OpsLab/SidekickPipeline/blob/dmc-again/OccluderGen/octree.hxx#L583

namespace JPLOPS.Geometry
{

    /// <summary>
    /// Efficient structure for finding nearest neighbor on a set of triangles
    /// </summary>
    public class Octree
    {
        // The root of the oct tree
        public OctreeNode Root;

        // The maximum number of OctreeNodes appearing in a OctreeNode's Contained list before splitting
        public int MaxOctreeNodeSize;

        // Maximum depth of the tree, OctreeNodes at this depth will not be split
        public int DepthLimit;

        // The number of OctreeNodes in the tree
        public int NodeCount;

        // The number of leaf OctreeNodes in the tree
        public int LeafCount;

        // The depth of the deepest node
        public int Deepest;

        public Octree(BoundingBox bounds, int maxOctreeNodeSize = 10, int maxDepth = 8)
        {
            this.MaxOctreeNodeSize = maxOctreeNodeSize;
            this.DepthLimit = maxDepth;
            this.Root = new OctreeNode(this, null, bounds, 0);
            this.NodeCount = 1;
            this.LeafCount = 1;
            this.Deepest = 1;
        }

        /// <summary>
        /// Applys a function to every node in the tree
        /// </summary>
        /// <param name="applyFunc"></param>
        public void Traverse(Action<OctreeNode> applyFunc)
        {
            Visit(applyFunc, new Func<OctreeNode, bool>(node => true ));
        }

        /// <summary>
        /// Recurses down the tree calling function applyFunc on each OctreeNode if should_expand returns true on that OctreeNode
        /// Note: When applyFunc is NOT called on a OctreeNode, this function will ignore the OctreeNode's children
        /// </summary>
        /// <param name="applyFunc"></param>
        /// <param name="should_expand"></param>
        public void Visit(Action<OctreeNode> applyFunc, Func<OctreeNode, bool> should_expand)
        {
            Queue<OctreeNode> toVisit = new Queue<OctreeNode>();
            toVisit.Enqueue(Root);
            while(toVisit.Count != 0)
            {
                OctreeNode OctreeNode = toVisit.Dequeue();
                if (!should_expand(OctreeNode))
                    continue;

                applyFunc(OctreeNode);
                if (OctreeNode.IsLeaf())
                    continue;

                for (int i = 0; i < 8; i++)
                    toVisit.Enqueue(OctreeNode.Children[i]);
            }
        }

        /// <summary>
        /// Traverses the tree from OctreeNode start calling function applyFunc to each OctreeNode if should_expand returns true on that OctreeNode
        /// Note: When applyFunc is NOT called on a OctreeNode, this function will ignore the OctreeNode's children
        /// </summary>
        /// <param name="start"></param>
        /// <param name="applyFunc"></param>
        /// <param name="should_expand"></param>
        public void VisitFrom(OctreeNode start, Action<OctreeNode> applyFunc, Func<OctreeNode, bool> should_expand)
        {
            HashSet<OctreeNode> visited = new HashSet<OctreeNode>();

            Queue<OctreeNode> toVisit = new Queue<OctreeNode>();
            toVisit.Enqueue(start);
            while(toVisit.Count != 0)
            {
                // Get the next OctreeNode to be searched from the queue
                OctreeNode OctreeNode = toVisit.Dequeue();
                if (visited.Contains(OctreeNode))
                    continue;
                visited.Add(OctreeNode);

                // Expand this OctreeNode
                if(should_expand(OctreeNode))
                {
                    applyFunc(OctreeNode);

                    if (!OctreeNode.IsLeaf())
                        for (int i = 0; i < 8; i++)
                            toVisit.Enqueue(OctreeNode.Children[i]);
                }

                // Add this OctreeNode's parent to the queue if not already searched
                if (OctreeNode.Parent != null && !visited.Contains(OctreeNode.Parent))
                    toVisit.Enqueue(OctreeNode.Parent);              
            }
        }

        /// <summary>
        /// Inserts a single triangle into the Oct tree. The triangle will have a designated owner that fully contains its bounds.
        /// Child OctreeNodes of owner will also store if they intersect the triangle bounds
        /// </summary>
        /// <param name="contents"></param>
        public void Insert(OctreeNodeContents contents)
        {
            // find smallest fully containing OctreeNode
            BoundingBox triBounds = contents.Bounds();
            OctreeNode owner = Root;
            bool foundChild = true;

            while (foundChild && !owner.IsLeaf())
            {
                foundChild = false;
                for (int i = 0; i < 8; i++)
                {
                    OctreeNode child = owner.Children[i];
                    if (child.Bounds.Contains(triBounds) == ContainmentType.Contains)
                    {
                        foundChild = true;
                        owner = child;
                        break;
                    }
                }
            }

            owner.Contained.Add(contents);

            // fix intersecting lists as well
            Visit( node =>
            {
                if (!node.IsLeaf())
                    return;
                node.Intersecting.Add(contents);
            }, OctreeNode =>
            {
                return contents.Intersects( OctreeNode.Bounds);
            });
        }

        /// <summary>
        /// Loops over the triangles in mesh and adds them to the Oct tree, then splits OctreeNodes from the root down as needed
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="meshImage"></param>
        public void InsertList(IEnumerable<OctreeNodeContents> list)
        {
            foreach(OctreeNodeContents contents in list)
            {
                Insert(contents);
            }
            Visit(node =>
            {
                if (node.Depth >= DepthLimit)
                    return;

                if (node.IsLeaf() && node.Contained.Count > MaxOctreeNodeSize)
                {
                    node.Split();
                }
            }, OctreeNode => { return true; });
        }

        /// <summary>
        /// Given a list of child indicies, this method will decend the tree correspoinding to each
        /// child index and return the last child or the first leaf node along the path
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public OctreeNode FollowPath(List<int> path)
        {
            OctreeNode current = Root;
            foreach (int i in path)
            {
                if (current.IsLeaf())
                {
                    break;
                }

                current = current.Children[i];
            }
            return current;
        }

        /// <summary>
        /// Returns the closest triangle (with its texture) on the mesh to query point xyz. Returns null if mesh is empty
        /// </summary>
        /// <param name="xyz"></param>
        /// <returns></returns>
        public OctreeNodeContents Closest(Vector3 xyz)
        {
            return Closest(xyz, Root);
        }

        /// <summary>
        /// Returns the closest triangle (with its texture) on the mesh to query point xyz using OctreeNode start as initial guess. Returns null if mesh is empty
        /// </summary>
        /// <param name="xyz"></param>
        /// <param name="start"></param>
        /// <returns></returns>
        public OctreeNodeContents Closest(Vector3 xyz, OctreeNode start)
        {
            double closestDist = Double.MaxValue;
            OctreeNodeContents closestTri = null;

            // checks all triangles in leaf OctreeNode and updates closest to query point
            Action<OctreeNode> searchOctreeNode = node => {
                if(!node.IsLeaf())
                {
                    return;
                }
                foreach(OctreeNodeContents contents in node.Intersecting)
                {
                    double dist = contents.SquaredDistance(xyz);
                    if(dist < closestDist)
                    {
                        closestDist = dist;
                        closestTri = contents;
                    }
                }

            };

            // returns true if OctreeNode could contain the closest triangle
            Func<OctreeNode, bool> should_expand = node => {
                return node.SquaredDistance(xyz) <= closestDist + 1e-8;
            };

            // search for the closest triangle using initial guess `start`
            VisitFrom(start, searchOctreeNode, should_expand);

            return closestTri;
        }

        /// <summary>
        /// returns the closest triangle on the mesh to query point `xyz` using OctreeNode `start` as initial guess. Returns null if mesh is empty.
        /// Additionally returns the last OctreeNode visited via outpointer end (useful as starting point of next search)
        /// </summary>
        /// <param name="xyz"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        public OctreeNodeContents Closest(Vector3 xyz, OctreeNode start, out OctreeNode end)
        {
            double closestDist = Double.MaxValue;
            OctreeNodeContents closestTri = null;

            OctreeNode endOctreeNode = Root;

            // checks all triangles in leaf OctreeNode and updates closest to query point
            Action<OctreeNode> searchOctreeNode = node => {
                if (!node.IsLeaf())
                {
                    return;
                }
                foreach (OctreeNodeContents contents in node.Intersecting)
                {
                    double dist = contents.SquaredDistance(xyz);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closestTri = contents;
                        endOctreeNode = node;
                    }
                }

            };

            // returns true if OctreeNode could contain the closest triangle
            Func<OctreeNode, bool> should_expand = node => {
                return node.SquaredDistance(xyz) <= closestDist + 1e-8;
            };

            // search for the closest triangle using initial guess `start`
            VisitFrom(start, searchOctreeNode, should_expand);

            end = endOctreeNode;

            return closestTri;
        }
    }
}