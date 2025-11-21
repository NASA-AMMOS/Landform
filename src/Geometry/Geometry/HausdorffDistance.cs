using System;
using System.Collections.Generic;
using Priority_Queue;
using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Computes the distance between the surfaces of two meshes
    /// Based on "Fast and Accurate Hausdorff Distance Calculation Between Meshes"
    /// By Michael Guthe, Pavel Borodin, and Reinhard Klein
    /// http://cg.cs.uni-bonn.de/en/publications/paper-details/guthe-2005-fast/
    /// </summary>
    public class HausdorffDistance
    {

        /// <summary>
        /// Calculates the bidirectional Hausdorff error distance (in model units) between mesh A and B within an allowed error distance (in model units)
        /// </summary>
        /// <param name="meshA">The first model</param>
        /// <param name="meshB">The second model</param>
        /// <param name="maxErrorEpsilon">Maximum allowed uncertainty in the answer, where accuracy comes at the cost of speed</param>
        /// <param name="symmetric">if false then compute unidirectional Hausdorff distance from meshA to meshB</param>
        /// <returns></returns>
        public static double Calculate(Mesh meshA, Mesh meshB, double maxErrorEpsilon, bool symmetric = true)
        {
            // The square of the Hausdorff distance, which starts at 0 and grows until the algorithm ends, when it returns its square root
            double hausdorffDistanceSquared = 0;
            // Priority queue holding the octree node cells and also triangles themselves which get directed by the cell or tree's maximum possible distance to another triangle
            SimplePriorityQueue<OctreeNode> queue = new SimplePriorityQueue<OctreeNode>();
            // The max error in distance allowed when the algorithm runs, telling it when it can stop subdividing for higher accuracy
            // Subdivision is disabled when 0, meaning it runs fast but isn't accurate
            double epsilon = maxErrorEpsilon;

            // Build an octree for both meshes and insert the mesh triangles into them
            BoundingBox combinedBounds = BoundingBoxExtensions.Union(meshA.Bounds(), meshB.Bounds());

            // Build two octrees with the same bounds so that each cell can correspond to a cell in the other octree

            // Insert all the triangles from each mesh into their respective octree
            Octree octreeA = new Octree(combinedBounds);
            List<OctreeNodeContents> triListA = new List<OctreeNodeContents>();
            foreach (Triangle tri in meshA.Triangles())
            {
                triListA.Add(new HausdorffTriangle(tri));
            }
            octreeA.InsertList(triListA);
            triListA = null;

            Octree octreeB = new Octree(combinedBounds);
            List<OctreeNodeContents> triListB = new List<OctreeNodeContents>();
            foreach (Triangle tri in meshB.Triangles())
            {
                triListB.Add(new HausdorffTriangle(tri));
            }
            octreeB.InsertList(triListB);
            triListB = null;

            // Action to cache the traversal path in each voxel triangle to reach its containing octree node cell
            Action<OctreeNode> saveTraversal = (node => {
                // List of traversal steps (each from 0-7) used to go down through the branches of the tree
                List<int> path = new List<int>();

                // Walk up tree, updating current to new node and adding the branch index to the traversal path list
                OctreeNode current = node;
                while (current.Parent != null)
                {
                    // Check each branch in the parent to see if it's equal to the current branch we are walking on
                    int i = 0;
                    foreach (OctreeNode child in current.Parent.Children)
                    {
                        // If this is the same branch as myself, use stop checking all the other branches after me
                        if (child == current)
                        {
                            break;
                        }
                        
                        // This current node must be in a later branch owned by the parent of this node
                        i++;
                    }

                    // Once it breaks or finishes the loop and has the correct value of i, that's the index of the current node
                    path.Add(i);

                    // Update the current node to its parent so it walks one level up the tree, where it can eventually stop looping upon reaching null (the root)
                    current = current.Parent;
                }

                // Place the traversal path in the triangle structure so it knows how to reach its octree node cell
                foreach (HausdorffTriangle voxelTri in node.Contained)
                {
                    // Traversal goes from the bottom up, so it must be reversed to traverse from the top back down
                    List<int> reversedPath = new List<int>(path);
                    reversedPath.Reverse();

                    // Put the correctly ordered traversal path on the voxel triangle
                    voxelTri.TraversalPath = reversedPath;
                }
            });

            // Traverse all the nodes in both octrees and determine then cache the traversal path for every voxel triangle in the tree
            octreeA.Traverse(saveTraversal);
            octreeB.Traverse(saveTraversal);

            // Enqueue the root cell of both octrees
            queue.Enqueue(octreeA.Root, (float)ComputeMaxGeometricDistanceToClosestCellSquared(octreeA.Root, octreeB));

            if (symmetric)
            {
                queue.Enqueue(octreeB.Root,
                              (float)ComputeMaxGeometricDistanceToClosestCellSquared(octreeB.Root, octreeA));
            }

            // Process each cell or triangle
            while (queue.Count > 0)
            {
                // Find the nearest cell in the other octree at the same level as measured by minimum distance
                OctreeNode current = queue.Dequeue();
                Octree currentTree = current.Owner;
                Octree otherTree = (currentTree == octreeA) ? octreeB : octreeA;
                OctreeNode nearestCellOnOtherMesh = FindNearestCellToGivenCellAtSameLevel(current, otherTree);

                // Compute the shortest and furthest distance between these cells
                double shortestSquared = BoundingBoxExtensions.ClosestDistanceSquared(current.Bounds, nearestCellOnOtherMesh.Bounds);
                double furthestSquared = BoundingBoxExtensions.FurthestDistanceSquared(current.Bounds, nearestCellOnOtherMesh.Bounds);

                // Grow the current hausdorff distance if the shortest is bigger
                hausdorffDistanceSquared = Math.Max(hausdorffDistanceSquared, shortestSquared);

                // Skip traversal of this cell's subtree
                if (furthestSquared <= hausdorffDistanceSquared)
                {
                    continue;
                }

                // The current cell actually represents an individual triangle, 
                if (current.Owner == null)
                {
                    HausdorffTriangle currentOctreeTri = (HausdorffTriangle)current.Contained[0];
                    Triangle currentTri = currentOctreeTri.Triangle;

                    // Triangle points
                    Vertex p0 = currentTri.V0;
                    Vertex p1 = currentTri.V1;
                    Vertex p2 = currentTri.V2;

                    // Halfway points between each triangle point
                    Vertex m01 = new Vertex((p0.Position + p1.Position) / 2);
                    Vertex m12 = new Vertex((p1.Position + p2.Position) / 2);
                    Vertex m20 = new Vertex((p2.Position + p0.Position) / 2);

                    // Subdivide the triangle into new triangles
                    Triangle tri0 = new Triangle(m01, m12, m20);
                    Triangle tri1 = new Triangle(p0, m01, m20);
                    Triangle tri2 = new Triangle(p1, m01, m12);
                    Triangle tri3 = new Triangle(p2, m12, m20);

                    // Turn triangles into voxel triangles
                    HausdorffTriangle octreeTri0 = new HausdorffTriangle(tri0);
                    HausdorffTriangle octreeTri1 = new HausdorffTriangle(tri1);
                    HausdorffTriangle octreeTri2 = new HausdorffTriangle(tri2);
                    HausdorffTriangle octreeTri3 = new HausdorffTriangle(tri3);

                    // Find the corresponding base points for each point used in the subdivided mesh
                    OctreeNode lastTree = otherTree.FollowPath(currentOctreeTri.TraversalPath);
                    BasePoint basepointP0 = currentOctreeTri.BasePoints[0];
                    BasePoint basepointP1 = currentOctreeTri.BasePoints[1];
                    BasePoint basepointP2 = currentOctreeTri.BasePoints[2];
                    BasePoint basepointM01 = FindNearestBasePointFromPoint(m01.Position, otherTree, lastTree, out lastTree);
                    BasePoint basepointM12 = FindNearestBasePointFromPoint(m12.Position, otherTree, lastTree, out lastTree);
                    BasePoint basepointM20 = FindNearestBasePointFromPoint(m20.Position, otherTree, lastTree, out lastTree);

                    // Attach the base points to each subdivided triangle
                    octreeTri0.BasePoints = new BasePoint[] { basepointM01, basepointM12, basepointM20 };
                    octreeTri1.BasePoints = new BasePoint[] { basepointP0, basepointM01, basepointM20 };
                    octreeTri2.BasePoints = new BasePoint[] { basepointP1, basepointM01, basepointM12 };
                    octreeTri3.BasePoints = new BasePoint[] { basepointP2, basepointM12, basepointM20 };

                    // Add the traversal path to each new triangle
                    octreeTri0.TraversalPath = currentOctreeTri.TraversalPath;
                    octreeTri1.TraversalPath = currentOctreeTri.TraversalPath;
                    octreeTri2.TraversalPath = currentOctreeTri.TraversalPath;
                    octreeTri3.TraversalPath = currentOctreeTri.TraversalPath;

                    // Process each of the four new triangles
                    ProcessTriangle(octreeTri0, current, otherTree, epsilon, ref hausdorffDistanceSquared, queue);
                    ProcessTriangle(octreeTri1, current, otherTree, epsilon, ref hausdorffDistanceSquared, queue);
                    ProcessTriangle(octreeTri2, current, otherTree, epsilon, ref hausdorffDistanceSquared, queue);
                    ProcessTriangle(octreeTri3, current, otherTree, epsilon, ref hausdorffDistanceSquared, queue);
                }
                // If the current cell is a leaf, process its triangles and insert them into the queue
                else if (current.IsLeaf())
                {
                    // Process each triangle that intersects this cell (including those wholly contained)
                    foreach (HausdorffTriangle tri in current.Intersecting)
                    {
                        ProcessTriangle(tri, current, otherTree, epsilon, ref hausdorffDistanceSquared, queue);
                    }
                }
                // If not a leaf, insert its sub-cells into the queue
                else
                {
                    // Insert all this cell's children into the queue
                    foreach (OctreeNode child in current.Children)
                    {
                        // Only process cells containing triangles at any sub-level
                        if (!child.IsEmpty())
                        {
                            // Add this child to the priority queue at the furthest-case distance
                            queue.Enqueue(child, (float)ComputeMaxGeometricDistanceToClosestCellSquared(child, otherTree));
                        }
                    }
                }
            }

            // Return the final hausdorff distance
            return Math.Sqrt(hausdorffDistanceSquared);
        }

        /// <summary>
        /// Take a triangle and check its surroundings for its minimum and maximum possible distance to something else, and enqueue it if it isn't ruled out
        /// </summary>
        /// <param name="tri">The voxel triangle getting processed</param>
        /// <param name="current">The cell which contains this triangle</param>
        /// <param name="otherTree">The entire other octree from the other mesh which this triangle is not a part of</param>
        private static void ProcessTriangle(HausdorffTriangle tri, OctreeNode current, Octree otherTree, double epsilon, ref double hausdorffDistanceSquared, SimplePriorityQueue<OctreeNode> queue)
        {
            // Compute the barycenter of this triangle
            Vector3 barycenter = tri.Triangle.Barycenter();

            // Find the nearest base points to each of this triangle's three vertices if needed
            if (tri.BasePoints == null)
            {
                tri.BasePoints = FindNearestBasePointsFromTriangle(tri, otherTree);
            }

            // The three vertices of the triangle
            Vertex[] trianglePoints = tri.Triangle.Vertices();

            // Shortest case and furthest case distances from this triangle to the mesh itself
            double shortestDistanceSquared = 0;
            double furthestDistanceSquared = 0;

            // Calculate the vector from triangle point (V) to base point (P) and update the shortest and furthest distances
            for (int i = 0; i < 3; i++)
            {
                double vToPLengthSquared = (tri.BasePoints[i].Position - trianglePoints[i].Position).LengthSquared();
                double bToPLengthSquared = (tri.BasePoints[i].Position - barycenter).LengthSquared();

                shortestDistanceSquared = Math.Max(shortestDistanceSquared, vToPLengthSquared);
                furthestDistanceSquared = Math.Max(furthestDistanceSquared, bToPLengthSquared);
            }

            // Update the global hausdorff distance with the minimum
            hausdorffDistanceSquared = Math.Max(hausdorffDistanceSquared, shortestDistanceSquared);

            // Don't bother adding the triangle to the queue if its furthest case is already shorter than the hausdorff distance
            if (furthestDistanceSquared < hausdorffDistanceSquared)
            {
                return;
            }

            // Stop subdividing once triangles become too small so that they don't subdivide forever
            if (shortestDistanceSquared > epsilon || furthestDistanceSquared < epsilon)
            {
                return;
            }

            // If it hit the same triangle with each of the three base points, the shortest case distance is the actual distance, so don't add the triangle to the queue
            if (tri.BasePoints[0].TriangleContent == tri.BasePoints[1].TriangleContent && tri.BasePoints[1].TriangleContent == tri.BasePoints[2].TriangleContent)
            {
                return;
            }

            // Add this triangle to the queue by mis-using an octree node as a singular triangle by setting its owner property (first argument) to null
            OctreeNode voxelTriangle = new OctreeNode(null, null, tri.Bounds(), current.Depth);
            voxelTriangle.Contained.Add(tri);

            // Enqueue the triangle using the furthest distance metric
            queue.Enqueue(voxelTriangle, (float)furthestDistanceSquared);
        }

        /// <summary>
        /// Finds the points at which the three triangle points are geometrically closest to the surrounding mesh from the opposite octree
        /// </summary>
        /// <param name="tri">The triangle which we are finding nearby points from</param>
        /// <param name="otherTree">The entire other octree for the mesh which this triangle is not a part of</param>
        /// <returns></returns>
        private static BasePoint[] FindNearestBasePointsFromTriangle(HausdorffTriangle tri, Octree otherTree)
        {
            // Prepare a home for the three base points that will be found
            BasePoint[] basePoints = new BasePoint[3];

            // Get the three vertices in this triangle from which we find their nearest base points
            Vertex[] vertices = tri.Triangle.Vertices();

            // In order to traverse the octree for nearby cells to find the triangle on which the nearest base point will be determined to exist,
            // we need to start somewhere close by traversing down to the triangle's wholly-containing cell
            OctreeNode last = otherTree.FollowPath(tri.TraversalPath);

            // Find the nearest base point located on the other mesh to each of the tree triangle vertices from the triangle in this mesh
            basePoints[0] = FindNearestBasePointFromPoint(vertices[0].Position, otherTree, last, out last);
            basePoints[1] = FindNearestBasePointFromPoint(vertices[1].Position, otherTree, last, out last);
            basePoints[2] = FindNearestBasePointFromPoint(vertices[2].Position, otherTree, last, out last);

            // Return the base points which are closest to each of the tree triangle vertices in the opposite mesh
            return basePoints;
        }

        /// <summary>
        /// Takes a point and looks around the octree for the other mesh, trying to find the geometrically closest point to this point in space on that other mesh
        /// </summary>
        /// <param name="point">The point in 3D space where we want the closest point on the other mesh</param>
        /// <param name="otherTree">The entire other octree containing the other mesh triangles to which we are looking for the closest point</param>
        /// <param name="startingOctreeNode">A shortcut octree cell where we can begin searching its surroundings</param>
        /// <param name="lastOctreeNode">An octree node that gets mutated after the search is finished to be used as a refined shortcut in future nearby searches</param>
        /// <returns>The base point where the triangle on the other mesh is closest to this point in 3D space</returns>
        private static BasePoint FindNearestBasePointFromPoint(Vector3 point, Octree otherTree, OctreeNode startingOctreeNode, out OctreeNode lastOctreeNode)
        {
            // Find the closest triangle to this requested point in 3D space
            HausdorffTriangle triangleContent = (HausdorffTriangle)otherTree.Closest(point, startingOctreeNode, out lastOctreeNode);

            // Find the physical point on the triangle which is geometrically closest to the requested point
            Vector3 closestPoint = triangleContent.Triangle.ClosestPoint(point).Position;

            // Create a basepoint that wraps the point in space along with the triangle it's on
            return new BasePoint(closestPoint, triangleContent);
        }

        /// <summary>
        /// Gets the worst-case, furthest possible distance from one cell to any other closest cell that exists in the other octree
        /// </summary>
        /// <param name="sourceCell">The octree cell from which we are finding the closest other cell's furthest possible distance</param>
        /// <param name="otherOctreeRoot">The root of the other octree which must be traversed to find the closest cell to the given one</param>
        /// <returns>A distance between the two cells from furthest corner to furthest corner, squared</returns>
        private static double ComputeMaxGeometricDistanceToClosestCellSquared(OctreeNode sourceCell, Octree otherOctreeRoot)
        {
            // Find the nearest cell in the other mesh's octree at the same level
            OctreeNode nearestCell = FindNearestCellToGivenCellAtSameLevel(sourceCell, otherOctreeRoot);

            // Find the furthest possible distance between the current and nearest cells
            return BoundingBoxExtensions.FurthestDistanceSquared(nearestCell.Bounds, sourceCell.Bounds);
        }

        /// <summary>
        /// Looks through neighboring cells in the opposite octree at the same depth and gets which cell is closest
        /// </summary>
        /// <param name="sourceCell">The cell from which we are looking for the closest neighbor</param>
        /// <param name="otherOctreeRoot">The root of the other octree so it can look for neighbors to the given cell at its depth</param>
        /// <returns>The cell that is found to be closest to the given cell in the other octree</returns>
        private static OctreeNode FindNearestCellToGivenCellAtSameLevel(OctreeNode sourceCell, Octree otherOctreeRoot)
        {
            // Start with the biggest possible distance that represents the shortest distance to the queried cells so far
            double minDistanceSquared = Double.MaxValue;

            // Transient result for the closest cell encountered so far, which eventually gets returned
            OctreeNode currentClosest = null;

            // The distance squared of each node the search encounters
            // This is set each time when checking if this distance is shorter than the current shortest, so it can then go and update the shortest without calculating the value again
            double currentDistanceSquared = 0;

            // Determines if the node should be searched inside of, since it may already be further than the current closest
            Func<OctreeNode, bool> shouldSearchNode = (node =>
            {
                // Find the distance from the given cell to this current node being traversed and cache that result in case it is indeed shorter, so it can be used in the update function
                currentDistanceSquared = BoundingBoxExtensions.ClosestDistanceSquared(sourceCell.Bounds, node.Bounds);

                // Return true if his node's distance is shorter than the current shortest and without going deeper than this cell
                return currentDistanceSquared < minDistanceSquared && (node.Depth <= sourceCell.Depth || sourceCell.IsLeaf());
            });

            // Update the shortest distance to a smaller value if this cell's distance is the right depth
            Action<OctreeNode> update = (node => {
                if (node.Depth == sourceCell.Depth || node.IsLeaf())
                {
                    // Update the current closest distance and the current closest cell node
                    minDistanceSquared = currentDistanceSquared;
                    currentClosest = node;
                }
            });

            // Traverse through the other octree in order to find the closest other cell at the correct depth
            otherOctreeRoot.Visit(update, shouldSearchNode);
            
            // Return the closest cell that was found to the given one from the other tree
            return currentClosest;
        }
    }

    /// <summary>
    /// Wrapper for a base point that holds the 3D position and the triangle the base point lies on
    /// </summary>
    struct BasePoint
    {
        public Vector3 Position;
        public HausdorffTriangle TriangleContent;

        public BasePoint(Vector3 position, HausdorffTriangle triangleContent)
        {
            Position = position;
            TriangleContent = triangleContent;
        }
    }

    /// <summary>
    /// Structure to facilitate storing triangles in an OctTree
    /// </summary>
    class HausdorffTriangle : OctreeNodeContents
    {
        public Triangle Triangle { get; internal set; }
        public BasePoint[] BasePoints = null;
        public List<int> TraversalPath;

        public HausdorffTriangle(Triangle tri)
        {
            Triangle = tri;
        }

        public BoundingBox Bounds()
        {
            return Triangle.Bounds();
        }

        /// <summary>
        /// Returns true if the triangle in this voxel intersects the given bounding box
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Intersects(BoundingBox other)
        {
            return Triangle.Intersects(other);
        }

        /// <summary>
        /// Returns the shortest distance between the xyz point and triangle squared
        /// </summary>
        /// <param name="xyz"></param>
        /// <returns></returns>
        public double SquaredDistance(Vector3 xyz)
        {
            return Triangle.SquaredDistance(xyz);
        }
    }
}
