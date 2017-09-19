using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;
using OPS.Geometry;

using Priority_Queue;
using System.Collections;

using triple = System.Single; //Will not double
using sink = System.Double; //Doesn't float

namespace OPS.Geometry
{
    /// <summary>
    /// Comparer passed to fast priority queue
    /// </summary>
    class EdgeComparer : IEqualityComparer<Edge>
    {
        public EdgeComparer() { }

        public bool Equals(Edge e1, Edge e2)
        {
            return e1.Equals(e2);
        }

        public int GetHashCode(Edge e)
        {
            return e.GetHashCode();
        }
    }

    /// <summary>
    /// Node used for the edge collapse queue
    /// </summary>
    class EdgeCollapseQueueNode : FastPriorityQueueNode
    {
        public Edge Edge;
        public VertexNode VNew;

        public EdgeCollapseQueueNode(VertexNode v1, VertexNode v2, VertexNode vNew, bool isOnPerimeter) : base()
        {
            this.Edge = new Edge(v1, v2, null, isOnPerimeter);
            this.VNew = vNew;
        }

        public EdgeCollapseQueueNode(Edge e, VertexNode vNew)
        {
            this.Edge = new Edge(e.Src, e.Dst, null, e.IsPerimeterEdge);
            this.VNew = vNew;
        }
    }



    public static class EdgeCollapse
    {
        const bool _DEBUG = false;

        //from http://ieeexplore.ieee.org/document/6211122/?reload=true and http://hhoppe.com/newqem.pdf and https://www.cs.cmu.edu/~./garland/Papers/quadrics.pdf
        /// <summary>
        /// Returns a new mesh by iteratively collapsing edges in mesh until reaching approximately `targetNumFaces` polygons
        /// Increasing `perimeterFactor` will weight the algorithm away from collapsing perimeter edges
        /// `avoidPerimeter` prevents the perimeter from collapsing inward
        /// Vertices in `notTouched` will never be collapsed (though vertices around them might be, this can lead malformed meshes when used without `avoidPerimeter` flag)
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="targetNumFaces"></param>
        /// <param name="perimeterFactor"></param>
        /// <param name="preservePerimeter"></param>
        /// <param name="fillPerimeter"></param>
        /// <param name="notTouched"></param>
        /// <param name="avoidTetrahedrons"></param>
        /// <returns></returns>
        public static Mesh QuadricEdgeCollapse(Mesh mesh, int targetNumFaces, sink perimeterFactor = 1, List<Vertex> notTouched = null)
        {
            mesh.HasUVs = false;
            mesh.HasColors = false;
            mesh.HasNormals = false;
            OnlyPositions(mesh.Vertices);
            mesh.Clean();

            EdgeGraph edgeGraph = new EdgeGraph(mesh);

            //Flag user specified vertices as untouchable
            if (notTouched != null)
            {
                OnlyPositions(notTouched);
                foreach (VertexNode v in edgeGraph.vertNodes)
                {
                    if (notTouched.Contains(v.Vert))
                    {
                        v.IsTouchable = false;
                    }
                }
            }

            //Compute Q matrix for each vertex
            //  Precompute adjacent faces for each vertex
            List<Face>[] adjacentFaces = GetVertexFaceAdjacency(mesh);
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                edgeGraph.vertNodes[i].Q = GetQMatrix(i, mesh, edgeGraph.vertNodes, adjacentFaces, perimeterFactor);
            }

            // build min heap on QEM for each edge vertex pair
            FastPriorityQueue<EdgeCollapseQueueNode> heap = new FastPriorityQueue<EdgeCollapseQueueNode>(6*mesh.Faces.Count);
            foreach (VertexNode v in edgeGraph.vertNodes)
            {
                foreach (Edge e in v.AdjacentEdges)
                {
                    if (e.Src < e.Dst)
                    {
                        VertexNode vNew = new VertexNode(e.GetNewVertPos(), edgeGraph.GetNewID());
                        sink cost = e.QEM(vNew.Vert);
                        heap.Enqueue(new EdgeCollapseQueueNode(e, vNew), (triple)cost);                      
                    }
                }
            }

            // process edge collapses until target number of faces are left
            int numFaces = mesh.Faces.Count;
            int nVerts = edgeGraph.vertNodes.Count;

            while (numFaces > targetNumFaces && heap.Count > 0)
            {
                if (_DEBUG)
                {
                    foreach (VertexNode v in edgeGraph.vertNodes)
                    {
                        foreach (Edge e in v.AdjacentEdges)
                        {
                            if (!e.Dst.AdjacentEdges.Contains(e))
                            {
                                //Found an edge in which only one vertex has knowledge of the other (should each store an edge)
                                throw new Exception("Edge(s) missing from mesh.");
                            }
                            Edge other = e.Dst.AdjacentEdges.Find(newEdge => newEdge == e);
                            if (other.IsPerimeterEdge != e.IsPerimeterEdge)
                            {
                                //Found two instances of same edge with different perimeter property
                                throw new Exception("Bad mesh perimeter.");
                            }
                        }
                    }
                }

                //Pop lowest cost edge
                EdgeCollapseQueueNode collapsingEdge = heap.Dequeue();
                Edge edge = collapsingEdge.Edge;
                VertexNode v1 = edge.Src;
                VertexNode v2 = edge.Dst;
                VertexNode vNew = collapsingEdge.VNew;
                sink temp = edge.QEM(vNew.Vert);

                //Skip if either vertex has been collapsed
                if(!v1.IsActive || !v2.IsActive)
                {
                    continue;
                }

                //Skip if this would collapse around a corner
                if(v1.IsOnPerimeter && v2.IsOnPerimeter && !edge.IsPerimeterEdge)
                {
                    continue;
                }

                //Skip if both untouchable
                if(!v1.IsTouchable && !v2.IsTouchable)
                {
                    continue;
                }

                //Skip if this would collapse a tetrahedron or other complex geometry
                if((NumCommonNeighbors(v1, v2) > 2 || (edge.IsPerimeterEdge && NumCommonNeighbors(v1, v2) > 1)))
                {
                    continue;
                }

                //Collapsing edge v1, v2 -> vNew
                vNew.Q = v1.Q + v2.Q;
                vNew.IsTouchable = true;
                if(!v1.IsTouchable || !v2.IsTouchable)
                {
                    vNew.IsTouchable = false;
                }
                if (v1.IsOnPerimeter || v2.IsOnPerimeter)
                {
                    vNew.IsOnPerimeter = true;
                }
                vNew.AdjacentEdges = new List<Edge>();

                //Get edges between v1 and v2
                Edge e12 = v1.AdjacentEdges.Find(e => e.Dst == v2);
                Edge e21 = v2.AdjacentEdges.Find(e => e.Dst == v1);

                //Delete edges with a collapsing left face in v1/v2 and shared neighbors
                //preserve perimeter knowledge when collapsing an interior vertex to the perimeter
                if (e12.Left != null)
                {
                    Edge neighborEdge = e12.Left.AdjacentEdges.Find(e => e.Left == v2 && e.Dst == v1);
                    if(neighborEdge.IsPerimeterEdge)
                    {
                        e12.Left.AdjacentEdges.Find(e => e.Dst == v2).IsPerimeterEdge = true;
                    }
                    e12.Left.AdjacentEdges.Remove(neighborEdge);

                    Edge v2Edge = v2.AdjacentEdges.Find(e => e.Dst == e12.Left);
                    Edge v1Edge = v1.AdjacentEdges.Find(e => e.Dst == e12.Left);
                    if(v2Edge.IsPerimeterEdge)
                    {
                        v1Edge.IsPerimeterEdge = true;
                    }
                    v2.AdjacentEdges.Remove(v2Edge);

                    numFaces -= 1;
                }
                if (e21.Left != null)
                {
                    Edge neighborEdge = e21.Left.AdjacentEdges.Find(e => e.Left == v1 && e.Dst == v2);
                    if(neighborEdge.IsPerimeterEdge)
                    {
                        e21.Left.AdjacentEdges.Find(e => e.Dst == v1).IsPerimeterEdge = true;
                    }
                    e21.Left.AdjacentEdges.Remove(neighborEdge);

                    Edge v1Edge = v1.AdjacentEdges.Find(e => e.Dst == e21.Left);
                    Edge v2Edge = v2.AdjacentEdges.Find(e => e.Dst == e21.Left);
                    if(v1Edge.IsPerimeterEdge)
                    {
                        v2Edge.IsPerimeterEdge = true;
                    }
                    v1.AdjacentEdges.Remove(v1Edge);

                    numFaces -= 1;
                }

                //delete collapsing edges
                v1.AdjacentEdges.Remove(e12);
                v2.AdjacentEdges.Remove(e21);

                //Add edges to vNew
                foreach (Edge e in v1.AdjacentEdges)
                {
                    vNew.AdjacentEdges.Add(new Edge(vNew, e.Dst, e.Left, e.IsPerimeterEdge));
                }
                foreach (Edge e in v2.AdjacentEdges)
                {
                    vNew.AdjacentEdges.Add(new Edge(vNew, e.Dst, e.Left, e.IsPerimeterEdge));
                }

                //Update neighbor's edges
                foreach(Edge e in vNew.AdjacentEdges)
                {
                    VertexNode neighbor = e.Dst;
                    foreach(Edge f in neighbor.AdjacentEdges.FindAll(dstEdge => dstEdge.Dst == v1 || dstEdge.Dst == v2))
                    {
                        f.Dst = vNew;
                    }
                    Edge leftEdge = neighbor.AdjacentEdges.Find(lftEdge => lftEdge.Left == v1 || lftEdge.Left == v2);
                    if (leftEdge != null)
                    {
                        leftEdge.Left = vNew;
                    }
                }

                if (_DEBUG)
                {
                    foreach (VertexNode v in edgeGraph.vertNodes)
                    {
                        foreach (Edge e in v.AdjacentEdges)
                        {
                            if (e.Dst == v1 || e.Dst == v2)
                            {
                                throw new Exception("Edge exists to deleted vertex in mesh.");
                            }
                        }
                    }
                }

                //Add new vertex
                edgeGraph.vertNodes.Add(vNew);

                //Remove old vertices
                v1.IsActive = false;
                v2.IsActive = false;

                //Add new edges to the queue
                foreach (Edge e in vNew.AdjacentEdges)
                {
                    VertexNode v3 = new VertexNode(e.GetNewVertPos(), edgeGraph.GetNewID());
                    double cost = e.QEM(v3.Vert);
                    heap.Enqueue(new EdgeCollapseQueueNode(e, v3), (triple)cost);
                }
                nVerts -= 1;
            }

            //Create a new mesh from list of edges
            var triangleList = new List<Triangle>();
            foreach (VertexNode v in edgeGraph.vertNodes)
            {
                if (v.IsActive)
                {
                    foreach (Edge e in v.AdjacentEdges)
                    {
                        if (e.Left != null /*&& compareVerts(e.Src.Vert, e.Dst.Vert) && compareVerts(e.Src.Vert, e.Left.Vert)*/)
                        {
                            triangleList.Add(new Triangle(e.Src.Vert, e.Dst.Vert, e.Left.Vert));
                        }
                    }
                }
            }
            return new Mesh(triangleList);
        }

        /// <summary>
        /// Sets the UV, color, and Normal of a Vertex to 0 vector
        /// </summary>
        /// <param name="list"></param>
        static void OnlyPositions(List<Vertex> list)
        {
            foreach (Vertex v in list)
            {
                v.UV = new Vector2(0, 0);
                v.Color = new Vector4(0, 0, 0, 0);
                v.Normal = new Vector3(0, 0, 0);
            }
        }

        

        /// <summary>
        /// Creates a 4x4 matrix where first column vector is the triangle normal, and all other entries are zero
        /// </summary>
        /// <param name="triangle"></param>
        /// <returns></returns>
        static Matrix GetPlaneNormalAsMatrix(Triangle triangle)
        {
            Vector3 normal = triangle.Normal;
            sink offset = -1 * triangle.V0.Position.Dot(normal);
            return new Matrix(normal.X,0,0,0,normal.Y,0,0,0,normal.Z,0,0,0,offset,0,0,0);
        }

        /// <summary>
        /// Get Q (sum squared error) matrix for a vertex from the orinal mesh
        /// </summary>
        /// <param name="vertexIndex"></param>
        /// <param name="mesh"></param>
        /// <param name="currentVerts"></param>
        /// <param name="adjacentFaces"></param>
        /// <param name="perimeterFactor"></param>
        /// <returns></returns>
        static Matrix GetQMatrix(int vertexIndex, Mesh mesh, List<VertexNode> currentVerts, List<Face>[] adjacentFaces, sink perimeterFactor)
        {
            Matrix vertQ = new Matrix(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            
            foreach (Face face in adjacentFaces[vertexIndex])
            {
                Matrix planeNormal = GetPlaneNormalAsMatrix(new Triangle(mesh.Vertices[face.P0], mesh.Vertices[face.P1], mesh.Vertices[face.P2]));
                vertQ += planeNormal * Matrix.Transpose(planeNormal);
            }
            if (currentVerts[vertexIndex].IsOnPerimeter)
            {
                vertQ *= perimeterFactor;
            }
            return vertQ;
        }

        /// <summary>
        /// Get a mapping from vertex (by index in mesh) to list of adjacent faces
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        static List<Face>[] GetVertexFaceAdjacency(Mesh mesh) {
            int nVerts = mesh.Vertices.Count;
            //vertexFaceList[i] contains list of faces adjacent to vertex i in mesh
            List<Face>[] vertexFaceList = new List<Face>[nVerts];
            for (int i = 0; i < nVerts; i++)
            {
                vertexFaceList[i] = new List<Face>();
            }
            foreach (Face face in mesh.Faces)
            {
                vertexFaceList[face.P0].Add(face);
                vertexFaceList[face.P1].Add(face);
                vertexFaceList[face.P2].Add(face);
            }

            return vertexFaceList;
        }

        /// <summary>
        /// Return the corners of a mesh (min x+y, x-y, -x+y, -x-y)
        /// </summary>
        /// <param name="mesh"></param>
        /// <returns></returns>
        public static List<Vertex> GetCorners(Mesh mesh)
        {
            Vertex lowerLeft = mesh.Vertices[0];
            Vertex lowerRight = mesh.Vertices[0];
            Vertex upperLeft = mesh.Vertices[0];
            Vertex upperRight = mesh.Vertices[0];
            foreach (Vertex v in mesh.Vertices)
            {
                if (v.Position.X + v.Position.Y < lowerLeft.Position.X + lowerLeft.Position.Y)
                {
                    lowerLeft = v;
                }
                if (-1 * v.Position.X + v.Position.Y < -1 * lowerRight.Position.X + lowerRight.Position.Y)
                {
                    lowerRight = v;
                }
                if (v.Position.X - v.Position.Y < upperLeft.Position.X - upperLeft.Position.Y)
                {
                    upperLeft = v;
                }
                if (-1 * v.Position.X - v.Position.Y < -1 * upperRight.Position.X - upperRight.Position.Y)
                {
                    upperRight = v;
                }
            }
            return new List<Vertex> { lowerLeft, lowerRight, upperLeft, upperRight };
        }

        /// <summary>
        /// Return number of neighbors shared by v1 and v2 (from edge lists)
        /// </summary>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        /// <returns></returns>
        static int NumCommonNeighbors(VertexNode v1, VertexNode v2)
        {
            int common = 0;
            foreach(Edge e in v1.AdjacentEdges)
            {
                VertexNode v = e.Dst;
                common += v2.AdjacentEdges.FindAll(f => e.Dst == f.Dst).Count;
               
            }

            if (_DEBUG)
            {
                int common1 = 0;
                foreach (Edge e in v2.AdjacentEdges)
                {
                    VertexNode v = e.Dst;
                    common1 += v1.AdjacentEdges.FindAll(f => e.Dst == f.Dst).Count;
                }
                if (common != common1)
                {
                    throw new Exception("Checking common neighbors between vertices a,b and b,a in mesh returned different results.");
                }
            }

            return common;
        }
    }
}
