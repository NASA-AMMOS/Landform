using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Priority_Queue;
using OPS.Geometry;
using Microsoft.Xna.Framework;

namespace OPS.Geometry
{
    public class HausdorffDistance
    {
        private double ONE_THIRD = 1.0 / 3.0;
        private double hausdorffDistanceSquared;
        private SimplePriorityQueue<OctreeNode> queue;

        public double Calculate(Mesh meshA, Mesh meshB)
        {
            // Start with zero minimum distance and an empty priority queue
            hausdorffDistanceSquared = 0;
            queue = new SimplePriorityQueue<OctreeNode>();

            // Build an octree for both meshes and insert the mesh triangles into them
            Octree a = new Octree(meshA.Bounds());
            Octree b = new Octree(meshB.Bounds());
            List<OctreeNodeContents> triListA = new List<OctreeNodeContents>();
            List<OctreeNodeContents> triListB = new List<OctreeNodeContents>();
            foreach (Triangle tri in meshA.Triangles())
            {
                triListA.Add(new VoxelTriangle(tri));
            }
            foreach (Triangle tri in meshB.Triangles())
            {
                triListB.Add(new VoxelTriangle(tri));
            }
            a.InsertList(triListA);
            b.InsertList(triListB);

            // Enqueue the root cell of both octrees
            queue.Enqueue(a.Root, (float)ComputeMaxGeometricDistanceToClosestCellSquared(a.Root, b));
            queue.Enqueue(b.Root, (float)ComputeMaxGeometricDistanceToClosestCellSquared(b.Root, a));

            // Process each cell or triangle
            while (queue.Count > 0)
            {
                // Find the nearest cell in the other octree at the same level as measured by minimum distance
                OctreeNode current = queue.Dequeue();
                Octree currentTree = current.Owner;
                Octree otherTree = (currentTree == a) ? b : a;
                OctreeNode nearestCellOnOtherMesh = FindNearestCellToGivenCellAtSameLevel(current, otherTree);

                // Compute the shortest and furthest distance between these cells
                double shortestSquared = BoundingBoxExtensions.DistanceSquared(current.Bounds, nearestCellOnOtherMesh.Bounds);
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
                    VoxelTriangle currentVoxelTri = (VoxelTriangle) current.Contained[0];
                    Triangle currentTri = currentVoxelTri.Triangle;

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
                    VoxelTriangle voxelTri0 = new VoxelTriangle(tri0);
                    VoxelTriangle voxelTri1 = new VoxelTriangle(tri1);
                    VoxelTriangle voxelTri2 = new VoxelTriangle(tri2);
                    VoxelTriangle voxelTri3 = new VoxelTriangle(tri3);

                    // Find the corresponding base points for each point used in the subdivided mesh
                    OctreeNode lastTree = otherTree.Root;
                    BasePoint basepointP0 = currentVoxelTri.BasePoints[0];
                    BasePoint basepointP1 = currentVoxelTri.BasePoints[1];
                    BasePoint basepointP2 = currentVoxelTri.BasePoints[2];
                    BasePoint basepointM01 = FindNearestBasepointFromPoint(m01.Position, otherTree, lastTree, out lastTree);
                    BasePoint basepointM12 = FindNearestBasepointFromPoint(m12.Position, otherTree, lastTree, out lastTree);
                    BasePoint basepointM20 = FindNearestBasepointFromPoint(m20.Position, otherTree, lastTree, out lastTree);

                    // Attach the base points to each subdivided triangle
                    voxelTri0.BasePoints = new BasePoint[] { basepointM01, basepointM12, basepointM20 };
                    voxelTri1.BasePoints = new BasePoint[] { basepointP0, basepointM01, basepointM20 };
                    voxelTri2.BasePoints = new BasePoint[] { basepointP1, basepointM01, basepointM12 };
                    voxelTri3.BasePoints = new BasePoint[] { basepointP2, basepointM12, basepointM20 };

                    // Process each of the four new triangles
                    ProcessTriangle(voxelTri0, current, otherTree);
                    ProcessTriangle(voxelTri1, current, otherTree);
                    ProcessTriangle(voxelTri2, current, otherTree);
                    ProcessTriangle(voxelTri3, current, otherTree);
                }
                // If the current cell is a leaf, process its triangles and insert them into the queue
                else if (current.IsLeaf())
                {
                    foreach (VoxelTriangle tri in current.Intersecting)
                    {
                        ProcessTriangle(tri, current, otherTree);
                    }
                }
                // If not a leaf, insert its sub-cells into the queue
                else
                {
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

        private void ProcessTriangle(VoxelTriangle tri, OctreeNode current, Octree otherTree)
        {
            // Compute the barycenter of this triangle
            Vector3 barycenter = new BarycentricPoint(ONE_THIRD, ONE_THIRD, ONE_THIRD, tri.Triangle).Position;

            // Find the nearest base points to each of this triangle's three vertices if needed
            if (tri.BasePoints == null)
            {
                tri.BasePoints = FindNearestBasePointsFromTriangle(tri, otherTree);
            }

            // The three vertices of the triangle
            Vertex[] trianglePoints = tri.Triangle.Vertices();

            // Shortest case and furthest case distances from this triangle to the mesh itself
            double maxShortestDistanceSquared = 0;
            double maxFurthestDistanceSquared = 0;

            // Calculate the vector from triangle point (V) to base point (P) and update the shortest and furthest distances
            for (int i = 0; i < 3; i++)
            {
                double vToPLengthSquared = (tri.BasePoints[i].Position - trianglePoints[i].Position).LengthSquared();
                double bToPLengthSquared = (tri.BasePoints[i].Position - barycenter).LengthSquared();

                maxShortestDistanceSquared = Math.Max(maxShortestDistanceSquared, vToPLengthSquared);
                maxFurthestDistanceSquared = Math.Max(maxFurthestDistanceSquared, bToPLengthSquared);
            }

            // Update the global hausdorff distance with the minimum
            hausdorffDistanceSquared = Math.Max(hausdorffDistanceSquared, maxShortestDistanceSquared);

            // Don't bother adding most triangles to the queue since they are already less far than the hausdorff distance
            if (maxFurthestDistanceSquared < hausdorffDistanceSquared)
            {
                return;
            }

            // Stop subdividing once triangles become too small so they don't subdivide forever
            if (maxFurthestDistanceSquared - maxShortestDistanceSquared < 1e-1)
            {
                return;
            }

            // If it hit the same triangle with each of the three base points, the shortest is the actual distance, so just use that
            // Otherwise, add this triangle to the queue based on its furthest possible distance from the triangle to the mesh
            // TODO: Fix this comment ^^^
            if (!(tri.BasePoints[0].Triangle == tri.BasePoints[1].Triangle && tri.BasePoints[1].Triangle == tri.BasePoints[2].Triangle))
            {
                // Mis-using an octree node as a singular triangle by setting its owner property (first argument) to null
                OctreeNode voxelTriangle = new OctreeNode(null, null, tri.Bounds(), current.Depth);
                voxelTriangle.Contained.Add(tri);

                // Enqueue the triangle using the furthest distance metric
                queue.Enqueue(voxelTriangle, (float)maxFurthestDistanceSquared);
            }
        }

        private BasePoint[] FindNearestBasePointsFromTriangle(VoxelTriangle tri, Octree otherTree)
        {
            BasePoint[] basePoints = new BasePoint[3];
            OctreeNode last = otherTree.Root;
            Vertex[] vertices = tri.Triangle.Vertices();

            for (int i = 0; i < vertices.Length; i++)
            {
                basePoints[i] = FindNearestBasepointFromPoint(vertices[i].Position, otherTree, last, out last);
            }

            return basePoints;
        }

        private BasePoint FindNearestBasepointFromPoint(Vector3 point, Octree otherTree, OctreeNode startingOctreeNode, out OctreeNode lastOctreeNode)
        {
            VoxelTriangle triangle = (VoxelTriangle)otherTree.Closest(point, startingOctreeNode, out lastOctreeNode);
            Vector3 closestPoint = triangle.Triangle.ClosestPoint(point).Position;

            return new BasePoint(closestPoint, triangle);
        }

        private double ComputeMaxGeometricDistanceToClosestCellSquared(OctreeNode sourceCell, Octree otherOctreeRoot)
        {
            // Find the nearest cell in the other mesh's octree at the same level
            OctreeNode nearestCell = FindNearestCellToGivenCellAtSameLevel(sourceCell, otherOctreeRoot);

            // Find the furthest possible distance between the current and nearest cells
            return BoundingBoxExtensions.FurthestDistanceSquared(nearestCell.Bounds, sourceCell.Bounds);
        }

        private OctreeNode FindNearestCellToGivenCellAtSameLevel(OctreeNode sourceCell, Octree otherOctreeRoot)
        {
            double minDistanceSquared = Double.MaxValue;
            OctreeNode currentClosest = null;

            double currentDistanceSquared = 0;

            Func<OctreeNode, bool> shouldSearchNode = (node =>
            {
                currentDistanceSquared = BoundingBoxExtensions.DistanceSquared(sourceCell.Bounds, node.Bounds);
                return currentDistanceSquared < minDistanceSquared && (node.Depth <= sourceCell.Depth || sourceCell.IsLeaf());
            });

            Action<OctreeNode> update = (node => {
                if (node.Depth == sourceCell.Depth || node.IsLeaf())
                {
                    minDistanceSquared = currentDistanceSquared;
                    currentClosest = node;
                }
            });

            otherOctreeRoot.Visit(update, shouldSearchNode);
            
            return currentClosest;
        }
    }

    struct BasePoint
    {
        public Vector3 Position;
        public VoxelTriangle Triangle;

        public BasePoint(Vector3 position, VoxelTriangle triangle)
        {
            Position = position;
            Triangle = triangle;
        }
    }
}
