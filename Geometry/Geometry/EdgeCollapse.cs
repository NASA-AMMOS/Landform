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
    /// Stores a vertex with its associated error matrix, edges, and flags
    /// </summary>
    class VertexData
    {
        public Vertex Vert;
        public Matrix Q;
        public List<Edge> AdjacentEdges;
        public bool IsOnPerimeter;
        public bool IsTouchable;
        public bool IsActive;

        public VertexData(Vertex vert)
        {
            this.Vert = vert;
            this.Q = new Matrix();
            this.AdjacentEdges = new List<Edge>();
            this.IsOnPerimeter = false;
            this.IsTouchable = true;
            this.IsActive = true;
        }

        public VertexData(Vertex vert, Matrix Q, List<Edge> adjacentEdges, bool isOnPerimeter, bool isTouchable)
        {
            this.Vert = vert;
            this.Q = Q;
            this.AdjacentEdges = adjacentEdges;
            this.IsOnPerimeter = isOnPerimeter;
            this.IsTouchable = isTouchable;
            this.IsActive = true;
        }
    }

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
    /// Stores two VertexData, and the third VertexData of its left face to store winding order
    /// </summary>
    class Edge {
        public VertexData Src, Dst;
        public VertexData Left;
        public bool IsPerimeterEdge;

        public Edge(VertexData v1, VertexData v2, VertexData left, bool isPerimeterEdge = false)
        {
            this.Src = v1;
            this.Dst = v2;
            this.Left = left;
            this.IsPerimeterEdge = isPerimeterEdge;
        }

        public override bool Equals(object obj)
        {
            if (obj == null || this.GetType() != obj.GetType()) return false;
            Edge edgeObj = (Edge)obj;
            if (Object.ReferenceEquals(this.Src, edgeObj.Src) && Object.ReferenceEquals(this.Dst, edgeObj.Dst)) return true;
            if (Object.ReferenceEquals(this.Src, edgeObj.Dst) && Object.ReferenceEquals(this.Dst, edgeObj.Src)) return true;
            return false;
        }

        public static bool operator ==(Edge lhs, Edge rhs)
        {
            if (Object.ReferenceEquals(lhs, null)) return Object.ReferenceEquals(rhs, null);
            return lhs.Equals(rhs);
        }

        public static bool operator !=(Edge lhs, Edge rhs)
        {
            return !(lhs == rhs);
        }

        public override int GetHashCode()
        {
            return Src.Vert.GetHashCode() + Dst.Vert.GetHashCode();
        }

        /// <summary>
        /// Compute Quadric Error Metric (QEM) for a new vertex position using sum of Q matrices
        /// </summary>
        /// <param name="vert"></param>
        /// <param name="Q"></param>
        /// <returns></returns>
        public sink QEM(Vertex vert)
        {
            Matrix v = new Matrix(vert.Position.X, 0, 0, 0, vert.Position.Y, 0, 0, 0, vert.Position.Z, 0, 0, 0, 1, 0, 0, 0);
            return (Matrix.Transpose(v) * (Src.Q + Dst.Q) * v).M11;
        }

        /// <summary>
        /// Compares the error cost of collapsing this edge to either or the two end points, or the midpoint. Returns the best option.
        /// </summary>
        /// <returns></returns>
        Vertex GetNewVertPosSimple()
        {
            Vertex v1 = this.Src.Vert;
            Vertex best = v1;
            sink minCost = QEM(v1); 
            Vertex v2 = this.Dst.Vert;
            if(QEM(v2) < minCost)
            {
                best = v2;
                minCost = QEM(v2);
            }
            Vertex mid = new Vertex((Src.Vert.Position.X + Dst.Vert.Position.X) / 2, (Src.Vert.Position.Y + Dst.Vert.Position.Y) / 2, (Src.Vert.Position.Z + Dst.Vert.Position.Z) / 2);
            if(QEM(mid) < minCost)
            {
                best = mid;
            }
            return best;
        }

        /// <summary>
        /// Returns the position of the Vertex to create upon collapsing this edge. Attempts to find local minimum in error, otherwise defaults to comparing ends and midpoint. Restriced for edges and user-specified vertices.
        /// </summary>
        /// <returns></returns>
        public Vertex GetNewVertPos()
        {
            if(Src.IsOnPerimeter && !Dst.IsOnPerimeter || !Src.IsTouchable && Dst.IsTouchable)
            {
                return Src.Vert;
            }
            if(Dst.IsOnPerimeter && !Src.IsOnPerimeter || !Dst.IsTouchable && Src.IsTouchable)
            {
                return Dst.Vert;
            }
            if (!IsPerimeterEdge)
            {
                Matrix Q = Src.Q + Dst.Q;
                Q[3, 0] = 0;
                Q[3, 1] = 0;
                Q[3, 2] = 0;
                Q[3, 3] = 1;
                if (Q.Determinant() > 1e-8)
                {
                    Matrix res = Matrix.Invert(Q) * new Matrix(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0);
                    return new Vertex(res.M11, res.M21, res.M31);
                }
            }
            return GetNewVertPosSimple();
        }
    }

    /// <summary>
    /// Node used for the edge collapse queue
    /// </summary>
    class EdgeCollapseQueueNode : FastPriorityQueueNode
    {
        public Edge Edge;
        public VertexData VNew;

        public EdgeCollapseQueueNode(VertexData v1, VertexData v2, VertexData vNew, bool isOnPerimeter) : base()
        {
            this.Edge = new Edge(v1, v2, null, isOnPerimeter);
            this.VNew = vNew;
        }

        public EdgeCollapseQueueNode(Edge e, VertexData vNew)
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

            List<VertexData> currentVerts = new List<VertexData>();

            //Construct VertexData objects for each vertex
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                currentVerts.Add(new VertexData(mesh.Vertices[i]));
            }

            //Add adjacency info
            foreach (Face face in mesh.Faces)
            {
                currentVerts[face.P0].AdjacentEdges.Add(new Edge(currentVerts[face.P0], currentVerts[face.P1], currentVerts[face.P2]));
                currentVerts[face.P1].AdjacentEdges.Add(new Edge(currentVerts[face.P1], currentVerts[face.P2], currentVerts[face.P0]));
                currentVerts[face.P2].AdjacentEdges.Add(new Edge(currentVerts[face.P2], currentVerts[face.P0], currentVerts[face.P1]));
            }

            //Flag perimeter vertices and edges
            foreach(VertexData v in currentVerts)
            {
                foreach(Edge e in v.AdjacentEdges)
                {
                    VertexData other = e.Dst;
                    if(!other.AdjacentEdges.Contains(e))
                    {
                        e.IsPerimeterEdge = true;
                        other.AdjacentEdges.Add(new Edge(other, v, null, true));
                        v.IsOnPerimeter = true;
                        other.IsOnPerimeter = true;
                    }
                }
            }


            //Flag user specified vertices as untouchable
            if (notTouched != null)
            {
                OnlyPositions(notTouched);
                foreach (VertexData v in currentVerts)
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
                currentVerts[i].Q = GetQMatrix(i, mesh, currentVerts, adjacentFaces, perimeterFactor);
            }

            // build min heap on QEM for each edge vertex pair
            FastPriorityQueue<EdgeCollapseQueueNode> heap = new FastPriorityQueue<EdgeCollapseQueueNode>(6*mesh.Faces.Count);
            foreach (VertexData v in currentVerts)
            {
                foreach (Edge e in v.AdjacentEdges)
                {
                    if (compareVerts(e.Src.Vert, e.Dst.Vert))
                    {
                        VertexData vNew = new VertexData(e.GetNewVertPos());
                        sink cost = e.QEM(vNew.Vert);
                        heap.Enqueue(new EdgeCollapseQueueNode(e, vNew), (triple)cost);                      
                    }
                }
            }

            // process edge collapses until target number of faces are left
            int numFaces = mesh.Faces.Count;
            int nVerts = currentVerts.Count;

            while (numFaces > targetNumFaces && heap.Count > 0)
            {
                if (_DEBUG)
                {
                    foreach (VertexData v in currentVerts)
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
                VertexData v1 = edge.Src;
                VertexData v2 = edge.Dst;
                VertexData vNew = collapsingEdge.VNew;
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
                    VertexData neighbor = e.Dst;
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
                    foreach (VertexData v in currentVerts)
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
                currentVerts.Add(vNew);

                //Remove old vertices
                v1.IsActive = false;
                v2.IsActive = false;
                currentVerts.Remove(v1);
                currentVerts.Remove(v2);


                //Add new edges to the queue
                foreach (Edge e in vNew.AdjacentEdges)
                {
                    VertexData v3 = new VertexData(e.GetNewVertPos());
                    double cost = e.QEM(v3.Vert);
                    heap.Enqueue(new EdgeCollapseQueueNode(e, v3), (triple)cost);
                }
                nVerts -= 1;
            }

            //Create a new mesh from list of edges
            var triangleList = new List<Triangle>();
            foreach (VertexData v in currentVerts)
            {
                foreach (Edge e in v.AdjacentEdges)
                {
                    if (e.Left != null /*&& compareVerts(e.Src.Vert, e.Dst.Vert) && compareVerts(e.Src.Vert, e.Left.Vert)*/)
                    {
                        triangleList.Add(new Triangle(e.Src.Vert, e.Dst.Vert, e.Left.Vert));
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
        /// Defines a simple total ordering on unique Vertex positions, useful for avoiding duplicate edges - only create edge if v1 "less than" v2
        /// </summary>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        /// <returns></returns>
        static bool compareVerts(Vertex v1, Vertex v2)
        {
            if (v1.Position.X > v2.Position.X)
                return true;
            if (v1.Position.X < v2.Position.X)
                return false;
            if (v1.Position.Y > v2.Position.Y)
                return true;
            if (v1.Position.Y < v2.Position.Y)
                return false;
            if (v1.Position.Z > v2.Position.Z)
                return true;
            if (v1.Position.Z < v2.Position.Z)
                return false;
            //Should never be reached as mesh is cleaned; if two vertices coincide as a result of decimation, keep both and clean after
            Console.Out.WriteLine("Duplicate vertices detected after clean during mesh decimation.");
            return true;
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
        static Matrix GetQMatrix(int vertexIndex, Mesh mesh, List<VertexData> currentVerts, List<Face>[] adjacentFaces, sink perimeterFactor)
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
        static int NumCommonNeighbors(VertexData v1, VertexData v2)
        {
            int common = 0;
            foreach(Edge e in v1.AdjacentEdges)
            {
                VertexData v = e.Dst;
                common += v2.AdjacentEdges.FindAll(f => e.Dst == f.Dst).Count;
               
            }

            if (_DEBUG)
            {
                int common1 = 0;
                foreach (Edge e in v2.AdjacentEdges)
                {
                    VertexData v = e.Dst;
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
