using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Xna.Framework;

using Priority_Queue;
using System.Runtime.CompilerServices;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Node used for the edge collapse queue
    /// </summary>
    class EdgeCollapseQueueNode : FastPriorityQueueNode
    {
        public CollapsableEdge Edge;

        public EdgeCollapseQueueNode(CollapsableVertexNode v1, CollapsableVertexNode v2, Vertex vNew, bool isOnPerimeter) : base()
        {
            this.Edge = new CollapsableEdge(v1, v2, null, vNew, isOnPerimeter);
        }

        public EdgeCollapseQueueNode(CollapsableEdge e) : base()
        {
            this.Edge = new CollapsableEdge((CollapsableVertexNode)e.Src, 
                (CollapsableVertexNode)e.Dst, null, e.VNew, e.IsPerimeterEdge);
        }
    }

    public static class EdgeCollapse
    {
        //Flag to enable checks for bad mesh topology (not geometry) in the graph structure after each collapse. Note that without preserveTopology, perimeter checks will fail, but others should succeed.
        readonly static bool _DEBUG = false;

        //algorithms from http://ieeexplore.ieee.org/document/6211122/?reload=true and http://hhoppe.com/newqem.pdf and https://www.cs.cmu.edu/~./garland/Papers/quadrics.pdf
        /// <summary>
        /// Returns a new mesh by iteratively collapsing edges in mesh until reaching approximately `targetNumFaces` polygons.
        /// perimeterPenaltyFactor scales the cost of collapsing edges touching the perimeter, as they will have fewer surrounding faces contributing to cost.
        /// preserveTopology when true forces collapses only to occur where an edge with only two neighboring faces (or perimeter with one face) to be collapsed. Also will not collapse outer edge of a tetrahedron.
        ///     Without this flag, algorithm will allow collapsing of non-manifold geometries (better for extreme collapsing), perimeters of holes may not be tracked properly.
        /// weightByArea when true will scale cost based on surrounding triangle area, rather than number of triangles. This will result in more collapses in areas of high triangle density and more uniform resulting meshes. When false, will better preserve triangle distribution.
        /// avoidFlips when true will prevent collapses that result in large changes to face normals that were previously uniform. Specifically, before preforming a collapse, a check will compare the magnitude of the sum of local face normals before and after. The collapse will be skipped if the result is less than flipThreshold.
        ///     Note that if flipThreshold is set too high (close to 0), collapses may be prevented where there is already high variance in normal direction, and flipping normal direction is not necessarily bad. For reference, if all normals are initially aligned and one is flipped, the result compared to threshold will be -2. 
        /// avoidSmallTris when true will prevent collapses that scale the smallest angle in a triangle past angle threshold. I.e. if angleThreshold is set to 0.5, then any collapse that halves the smallest angle of a triangle would be skipped. Similar to flips, a high threshold can prevent large numbers of collapses and produce bad (but interesting) meshes.
        ///     Note that this check involves angle computation (slow). Combined with checking flips, observed runtime was up to 2x compared to without in 1 million polygon meshes.
        /// notTouched takes a list of vertices that will not be allowed to move in the resulting mesh. This is useful for pinning corners of tiles, or areas of needed detail in meshes.
        /// accuracy_threshold (when not -1) will stop the decimation when this approximate error threshold between the decimated and original mesh is (conservatively) reached 
        /// </summary>
        /// <param name="mesh"></param>
        /// <param name="targetNumFaces"></param>
        /// <param name="perimeterPenaltyFactor"></param>
        /// <param name="preserveTopology"></param>
        /// <param name="weightByArea"></param>
        /// <param name="avoidFlips"></param>
        /// <param name="flipThreshold"></param>
        /// <param name="avoidSmallTris"></param>
        /// <param name="angleThreshold"></param>
        /// <param name="notTouched"></param>
        /// <returns></returns>
        public static Mesh QuadricEdgeCollapse(Mesh mesh, int targetNumFaces, double perimeterPenaltyFactor = 1, bool preserveTopology = true, bool weightByArea = false, bool avoidFlips = false, double flipThreshold = -1.0, bool avoidSmallTris = false, double angleThreshold = 0.25, List<Vertex> notTouched = null, double accuracyThreshold = -1)
        {
            mesh = new Mesh(mesh); //deep copy

            mesh.HasUVs = false;
            mesh.HasColors = false;
            mesh.HasNormals = false;
            OnlyPositions(mesh.Vertices);
            mesh.Clean();

            CollapsableEdgeGraph edgeGraph = new CollapsableEdgeGraph(mesh);

            //Flag user specified vertices as untouchable
            if (notTouched != null)
            {
                OnlyPositions(notTouched);
                foreach (CollapsableVertexNode v in edgeGraph.GetVertNodes())
                {
                    if (notTouched.Contains((Vertex)v))
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
                ((CollapsableVertexNode)edgeGraph.GetNode(i)).Q = 
                    GetQMatrix(i, mesh, edgeGraph.GetNode(i), adjacentFaces, perimeterPenaltyFactor, weightByArea);
                edgeGraph.GetNode(i).AdjFaceCount = adjacentFaces[i].Count;
            }

            // build min heap on QEM for each edge vertex pair
            FastPriorityQueue<EdgeCollapseQueueNode> heap = new FastPriorityQueue<EdgeCollapseQueueNode>(6*mesh.Faces.Count);
            foreach (CollapsableVertexNode v in edgeGraph.GetVertNodes())
            {
                foreach (CollapsableEdge e in v.GetAdjacentEdges())
                {
                    if (e.Src < e.Dst)
                    {
                        e.SetNewVertPos();
                        TryAddEdgeToQueue(heap, e, avoidFlips, flipThreshold, avoidSmallTris, angleThreshold, preserveTopology);
                    }
                }
            }

            // process edge collapses until target number of faces are left
            int numFaces = mesh.Faces.Count;
            int nVerts = edgeGraph.VertCount;

            while (numFaces > targetNumFaces && heap.Count > 0)
            {
                if (_DEBUG)
                {
                    foreach (CollapsableVertexNode v in edgeGraph.GetVertNodes())
                    {
                        if (v.IsActive)
                        {
                            foreach (CollapsableEdge e in v.GetAdjacentEdges())
                            {
                                if (!e.Dst.ContainsEdge(e))
                                {
                                    //Found an edge in which only one vertex has knowledge of the other (should each store an edge)
                                    throw new Exception("Edge(s) missing from mesh.");
                                }
                                Edge other = e.Dst.FindEdge(newEdge => newEdge == e);
                                if (other.IsPerimeterEdge != e.IsPerimeterEdge)
                                {
                                    //Found two instances of same edge with different perimeter property
                                    throw new Exception("Bad mesh perimeter.");
                                }
                            }
                        }
                    }
                }

                //Pop lowest cost edge
                EdgeCollapseQueueNode collapsingEdge = heap.Dequeue();
                CollapsableEdge edge = collapsingEdge.Edge;
                CollapsableVertexNode v1 = (CollapsableVertexNode)edge.Src;
                CollapsableVertexNode v2 = (CollapsableVertexNode)edge.Dst;

                //Skip if either vertex has been collapsed
                if (!v1.IsActive || !v2.IsActive)
                {
                    continue;
                }

                CollapsableVertexNode vNew = new CollapsableVertexNode(edge.VNew, edgeGraph.GetNewID());
                
                //Collapsing edge v1, v2 -> vNew
                vNew.Q = v1.Q + v2.Q;
                vNew.AdjFaceCount = v1.AdjFaceCount + v2.AdjFaceCount;
                vNew.IsTouchable = true;
                if(!v1.IsTouchable || !v2.IsTouchable)
                {
                    vNew.IsTouchable = false;
                }
                if (v1.IsOnPerimeter || v2.IsOnPerimeter)
                {
                    vNew.IsOnPerimeter = true;
                }

                //Break if vertex drift exceeds optional user-defined threshold parameter
                //Should guarantee break before actual mesh to mesh error reached
                //Need to test how close on more variety of mesh geometries; initial test showed ~2 factor for small decimations, unsure how this will hold up in general
                if (accuracyThreshold != -1)
                {
                    vNew.cost = Math.Max(v1.cost + Vector3.Distance(v1.Position, vNew.Position), v2.cost + Vector3.Distance(v2.Position, vNew.Position));
                    if (vNew.cost > accuracyThreshold)
                    {
                        break;
                    }
                }

                //Get edges between v1 and v2
                List<Edge> e12s = v1.FindAllEdges(e => e.Dst == v2).ToList();
                List<Edge> e21s = v2.FindAllEdges(e => e.Dst == v1).ToList();
                //delete collapsing edges
                foreach (CollapsableEdge e12 in e12s)
                {
                    if(e12.Left != null)
                    {
                        numFaces -= 1;
                    }
                    v1.RemoveEdge(e12);
                }
                foreach (CollapsableEdge e21 in e21s)
                {
                    if(e21.Left != null)
                    {
                        numFaces -= 1;
                    }
                    v2.RemoveEdge(e21);
                }

                foreach(CollapsableEdge e1x in v1.GetAdjacentEdges())
                {
                    CollapsableVertexNode vx = (CollapsableVertexNode)e1x.Dst;
                    foreach(CollapsableEdge exy in vx.GetAdjacentEdges())
                    {
                        if(exy.Dst == v1 || exy.Dst == v2)
                        {
                            if(exy.Left == v1 || exy.Left == v2)
                            {
                                if(exy.IsPerimeterEdge)
                                {
                                    if(exy.Dst == v1)
                                    {
                                        foreach (CollapsableEdge exz in vx.FindAllEdges(e => e.Dst == v2 || e.Dst == vNew))
                                        {
                                            exz.IsPerimeterEdge = true;
                                        }
                                    } else
                                    {
                                        foreach (CollapsableEdge exz in vx.FindAllEdges(e => e.Dst == v1 || e.Dst == vNew))
                                        {
                                            exz.IsPerimeterEdge = true;
                                        }
                                    }
                                }
                                exy.Dst = null;
                            } else
                            {
                                exy.Dst = vNew;
                            }

                        } else if(exy.Left == v1 || exy.Left == v2)
                        {
                            exy.Left = vNew;
                        }
                    }
                    vx.FilterEdges(e => e.Dst != null);
                    if (e1x.Left == v1 || e1x.Left == v2)
                    {
                        if (_DEBUG && e1x.Left == v1) { throw new Exception("Edge Left is Src"); }
                        if (e1x.IsPerimeterEdge)
                        {
                            foreach (CollapsableEdge e2x in v2.FindAllEdges(e => e.Dst == e1x.Dst))
                            {
                                e2x.IsPerimeterEdge = true;
                            }
                        }
                    } else
                    {
                        vNew.AddEdge(new CollapsableEdge(vNew, (CollapsableVertexNode)e1x.Dst, 
                            (CollapsableVertexNode)e1x.Left, e1x.IsPerimeterEdge));
                    }
                }
                foreach(CollapsableEdge e2x in v2.GetAdjacentEdges())
                {
                    CollapsableVertexNode vx = (CollapsableVertexNode)e2x.Dst;
                    foreach(CollapsableEdge exy in e2x.Dst.GetAdjacentEdges())
                    {
                        if(exy.Dst == v1 || exy.Dst == v2)
                        {
                            if(exy.Left == v1 || exy.Left == v2)
                            {
                                if (exy.IsPerimeterEdge)
                                {
                                    if (exy.Dst == v1)
                                    {
                                        foreach (CollapsableEdge exz in vx.FindAllEdges(e => e.Dst == v2 || e.Dst == vNew))
                                        {
                                            exz.IsPerimeterEdge = true;
                                        }
                                    }
                                    else
                                    {
                                        foreach (CollapsableEdge exz in vx.FindAllEdges(e => e.Dst == v1 || e.Dst == vNew))
                                        {
                                            exz.IsPerimeterEdge = true;
                                        }
                                    }
                                }
                                exy.Dst = null;
                            } else
                            {
                                exy.Dst = vNew;
                            }
                        } else if(exy.Left == v1 || exy.Left == v2)
                        {
                            exy.Left = vNew;
                        }
                    }
                    vx.FilterEdges(e => e.Dst != null);
                    if (e2x.Left == v1 || e2x.Left == v2)
                    {
                        if (_DEBUG && e2x.Left == v2)
                        {
                            throw new Exception("Edge Left is Src");
                        }
                        if (e2x.IsPerimeterEdge)
                        {
                            foreach (CollapsableEdge e1x in vNew.FindAllEdges(e => e.Dst == e2x.Dst))
                            {
                                e1x.IsPerimeterEdge = true;
                            }
                        }
                    } else
                    {
                        vNew.AddEdge(new CollapsableEdge(vNew, (CollapsableVertexNode)e2x.Dst, 
                            (CollapsableVertexNode)e2x.Left, e2x.IsPerimeterEdge));
                    }
                }         

                if (_DEBUG)
                {
                    foreach (CollapsableVertexNode v in edgeGraph.GetVertNodes())
                    {
                        if (v.IsActive)
                        {
                            foreach (CollapsableEdge e in v.GetAdjacentEdges())
                            {
                                if (e.Dst == v1 || e.Dst == v2)
                                {
                                    throw new Exception("Edge exists to deleted vertex in mesh.");
                                }
                            }
                        }
                    }
                }

                //Add new vertex
                edgeGraph.AddNode(vNew);

                //Remove old vertices
                v1.IsActive = false;
                v2.IsActive = false;

                //Add new edges to the queue
                foreach (CollapsableEdge e in vNew.GetAdjacentEdges())
                {
                    e.SetNewVertPos();
                    TryAddEdgeToQueue(heap, e, avoidFlips, flipThreshold, avoidSmallTris, angleThreshold, preserveTopology);
                }
                nVerts -= 1;
            }

            //Create a new mesh from list of edges
            var triangleList = new List<Triangle>();
            foreach (CollapsableVertexNode v in edgeGraph.GetVertNodes())
            {
                if (v.IsActive)
                {
                    foreach (CollapsableEdge e in v.GetAdjacentEdges())
                    {
                        if (e.Left != null && e.Src < e.Dst && e.Src < e.Left)
                        {
                            triangleList.Add(new Triangle(e.Src, e.Dst, e.Left));
                        }
                    }
                }
            }
            return new Mesh(triangleList);
        }

        /// <summary>
        /// Adds the edge to the queue with its cost as priority, unless it meets skip criteria
        /// </summary>
        /// <param name="queue"></param>
        /// <param name="e"></param>
        /// <param name="checkFlip"></param>
        /// <param name="flipThreshold"></param>
        /// <param name="checkTris"></param>
        /// <param name="angleThreshold"></param>
        /// <param name="preserveTopology"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void TryAddEdgeToQueue(FastPriorityQueue<EdgeCollapseQueueNode> queue, CollapsableEdge e, bool checkFlip, double flipThreshold, bool checkTris, double angleThreshold, bool preserveTopology)
        {
            //Skip if this would collapse around a corner
            if (e.Src.IsOnPerimeter && e.Dst.IsOnPerimeter && !e.IsPerimeterEdge)
            {
                return;
            }

            //Skip if both untouchable
            if (!((CollapsableVertexNode)e.Src).IsTouchable 
                && !((CollapsableVertexNode)e.Dst).IsTouchable)
            {
                return;
            }

            //Skip if this would collapse a tetrahedron or other complex geometry
            if (preserveTopology && (NumCommonNeighbors(e.Src, e.Dst) > 2 
                || (e.IsPerimeterEdge && NumCommonNeighbors(e.Src, e.Dst) > 1)))
            {
                return;
            }
            
            ///Skip if the collapse would result in bad local changes
            if ((!checkTris || GetSmallestAngleRatio(e) > angleThreshold)
                && (!checkFlip || CheckNormalChanges(e) > flipThreshold))
            {
                double cost = e.QEM(e.VNew);
                if(queue.Count == queue.MaxSize)
                {
                    queue.Resize(2 * queue.MaxSize);
                }
                queue.Enqueue(new EdgeCollapseQueueNode(e), (float)cost);
            }
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
            double offset = -1 * triangle.V0.Position.Dot(normal);
            return new Matrix(normal.X,0,0,0,normal.Y,0,0,0,normal.Z,0,0,0,offset,0,0,0);
        }

        /// <summary>
        /// Returns the angle between two vectors
        /// </summary>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        /// <returns></returns>
        public static double Angle(Vector3 v1, Vector3 v2)
        {
            return Math.Acos(v1.Dot(v2) / (v1.Length() * v2.Length()));
        }

        /// <summary>
        /// Compares all triangles before a collapse to after the collapse (pairwise). Returns the smallest ratio of angle change. 0 indicates a degenerate triangle would be created, 0-1 indicates at least one "skinnier" (smaller smallest angle) would be created. 1+ indicates all triangles would be less acute.
        /// </summary>
        /// <param name="collapsingEdge"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GetSmallestAngleRatio(CollapsableEdge collapsingEdge)
        {
            double minRatio = double.MaxValue;
            if (collapsingEdge.VNew.Position != collapsingEdge.Src.Position)
            {
                foreach (CollapsableEdge e in collapsingEdge.Src.GetAdjacentEdges())
                {
                    if (e.Left != null)
                    {
                        if (e.Left != collapsingEdge.Dst && e.Dst != collapsingEdge.Dst)
                        {
                            Vector3 v0a = collapsingEdge.VNew.Position;
                            Vector3 v0b = e.Src.Position;
                            Vector3 v1 = e.Dst.Position;
                            Vector3 v2 = e.Left.Position;
                            double a0 = Angle(v1 - v0a, v2 - v0a);
                            double a1 = Angle(v2 - v1, v0a - v1);
                            double a2 = Angle(v0a - v2, v1 - v2);
                            double b0 = Angle(v1 - v0b, v2 - v0b);
                            double b1 = Angle(v2 - v1, v0b - v1);
                            double b2 = Angle(v0b - v2, v1 - v2);
                            double minAngleNew = Math.Min(a0, Math.Min(a1, a2));
                            double minAngleOld = Math.Min(b0, Math.Min(b1, b2));
                            if(minAngleOld == 0)
                            {
                                return 1;
                            }
                            minRatio = Math.Min(minRatio, minAngleNew/minAngleOld);
                        }
                    }
                }
            }
            if (collapsingEdge.VNew.Position != collapsingEdge.Dst.Position)
            {
                foreach (CollapsableEdge e in collapsingEdge.Dst.GetAdjacentEdges())
                {
                    if (e.Left != null)
                    {
                        if (e.Left != collapsingEdge.Src && e.Dst != collapsingEdge.Src)
                        {
                            Vector3 v0a = collapsingEdge.VNew.Position;
                            Vector3 v0b = e.Src.Position;
                            Vector3 v1 = e.Dst.Position;
                            Vector3 v2 = e.Left.Position;
                            double a0 = Angle(v1 - v0a, v2 - v0a);
                            double a1 = Angle(v2 - v1, v0a - v1);
                            double a2 = Angle(v0a - v2, v1 - v2);
                            double b0 = Angle(v1 - v0b, v2 - v0b);
                            double b1 = Angle(v2 - v1, v0b - v1);
                            double b2 = Angle(v0b - v2, v1 - v2);
                            double minAngleNew = Math.Min(a0, Math.Min(a1, a2));
                            double minAngleOld = Math.Min(b0, Math.Min(b1, b2));
                            if (minAngleOld == 0)
                            {
                                return 1;
                            }
                            minRatio = Math.Min(minRatio, minAngleNew / minAngleOld);
                        }
                    }
                }
            }
            return minRatio;
        }

        /// <summary>
        /// Returns the change magnitude in sum of normal vectors before and after collapse. If this value is positive, the collapse results in local normals that are more aligned. In general, a higher return value indicates a "smoother" collapse. A low value can be used to detect face inversions. I.e. if all normals are initially pointing up and one is flipped, the result would be -2.
        /// </summary>
        /// <param name="collapsingEdge"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double CheckNormalChanges(CollapsableEdge collapsingEdge)
        {
            Vector3 oldMean = new Vector3(0, 0, 0);
            Vector3 newMean = new Vector3(0, 0, 0);
            Vector3 oldNorm;
            Vector3 newNorm;
            if (collapsingEdge.VNew.Position != collapsingEdge.Src.Position)
            {
                foreach (CollapsableEdge e in collapsingEdge.Src.GetAdjacentEdges())
                {
                    if (e.Left != null)
                    {
                        if (e.Left != collapsingEdge.Dst && e.Dst != collapsingEdge.Dst)
                        {
                            if( Triangle.ComputeNormal(e.Src.Position, e.Dst.Position, e.Left.Position, out oldNorm)
                                && Triangle.ComputeNormal(collapsingEdge.VNew.Position, e.Dst.Position, e.Left.Position, out newNorm))
                            {
                                oldMean += oldNorm;
                                newMean += newNorm;
                            }     
                        }
                    }
                }
            }
            if (collapsingEdge.VNew.Position != collapsingEdge.Dst.Position)
            {
                foreach (CollapsableEdge e in collapsingEdge.Dst.GetAdjacentEdges())
                {
                    if (e.Left != null)
                    {
                        if (e.Left != collapsingEdge.Src && e.Dst != collapsingEdge.Src)
                        {
                            if (Triangle.ComputeNormal(e.Src.Position, e.Dst.Position, e.Left.Position, out oldNorm)
                                && Triangle.ComputeNormal(collapsingEdge.VNew.Position, e.Dst.Position, e.Left.Position, out newNorm))
                            {
                                oldMean += oldNorm;
                                newMean += newNorm;
                            }
                        }
                    }
                }
            }
            if(newMean.LengthSquared() == 0)
            {
                return 1;
            }
            return (newMean.LengthSquared() - oldMean.LengthSquared());
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
        static Matrix GetQMatrix(int vertexIndex, Mesh mesh, VertexNode currentVert, List<Face>[] adjacentFaces, double perimeterFactor, bool normalizeArea)
        {
            Matrix vertQ = new Matrix(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            foreach (Face face in adjacentFaces[vertexIndex])
            {
                Triangle tri = new Triangle(mesh.Vertices[face.P0], mesh.Vertices[face.P1], mesh.Vertices[face.P2]);
                Matrix planeNormal = GetPlaneNormalAsMatrix(tri);
                double area = normalizeArea ? tri.Area() : 1;
                vertQ += planeNormal * Matrix.Transpose(planeNormal) * area;
            }
            if (currentVert.IsOnPerimeter)
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
        /// Return number of neighbors shared by v1 and v2 (from edge lists)
        /// </summary>
        /// <param name="v1"></param>
        /// <param name="v2"></param>
        /// <returns></returns>
        public static int NumCommonNeighbors(VertexNode v1, VertexNode v2)
        {
            int common = 0;
            foreach (Edge e in v1.GetAdjacentEdges())
            {
                VertexNode v = e.Dst;
                common += v2.FindAllEdges(f => e.Dst == f.Dst).ToList().Count;

            }

            if (_DEBUG)
            {
                int common1 = 0;
                foreach (CollapsableEdge e in v2.GetAdjacentEdges())
                {
                    CollapsableVertexNode v = (CollapsableVertexNode)e.Dst;
                    common1 += v1.FindAllEdges(f => e.Dst == f.Dst).ToList().Count;
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
