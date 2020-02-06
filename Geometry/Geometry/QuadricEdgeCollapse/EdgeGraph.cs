using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OPS.Geometry;
using Microsoft.Xna.Framework;

namespace OPS.Geometry
{
    /// <summary>
    /// Stores a mesh as a node-edge graph along with local metrics used in edge collapse
    /// </summary>
    public class EdgeGraph
    {
        public List<VertexNode> VertNodes;
        int newID;

        public EdgeGraph(Mesh mesh)
        {
            VertNodes = new List<VertexNode>();

            //Construct VertexNode objects for each vertex
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                VertNodes.Add(new VertexNode(mesh.Vertices[i], i));
            }
            newID = mesh.Vertices.Count;

            //Add adjacency info
            foreach (Face face in mesh.Faces)
            {
                VertNodes[face.P0].AdjacentEdges.Add(new Edge(VertNodes[face.P0], VertNodes[face.P1], VertNodes[face.P2], null));
                VertNodes[face.P1].AdjacentEdges.Add(new Edge(VertNodes[face.P1], VertNodes[face.P2], VertNodes[face.P0], null));
                VertNodes[face.P2].AdjacentEdges.Add(new Edge(VertNodes[face.P2], VertNodes[face.P0], VertNodes[face.P1], null));
            }

            //Flag perimeter vertices and edges
            foreach (VertexNode v in VertNodes)
            {
                foreach (Edge e in v.AdjacentEdges)
                {
                    VertexNode other = e.Dst;
                    if (!other.AdjacentEdges.Contains(e))
                    {
                        other.AdjacentEdges.Add(new Edge(other, v, null, true));
                        e.IsPerimeterEdge = true;
                        v.IsOnPerimeter = true;
                        other.IsOnPerimeter = true;
                    }
                }
            }
        }

        /// <summary>
        /// Returns a fresh id for a new node
        /// </summary>
        /// <returns></returns>
        public int GetNewID()
        {
            newID += 1;
            return newID;
        }

        public static bool ClipSubcycle(List<Edge> cycle)
        {
            Dictionary<Vector3, int> posToIndex = new Dictionary<Vector3, int>();
            int idx = 0;
            foreach (Edge e in cycle)
            {
                if(!posToIndex.ContainsKey(e.Src.Vert.Position))
                {
                    posToIndex.Add(e.Src.Vert.Position, idx);
                    idx++;
                }
                else
                {
                    //Found two subcycles, clip the smaller one
                    int smallerIndex = posToIndex[e.Src.Vert.Position];
                    int largerIndex = idx;
                    if (largerIndex - smallerIndex < cycle.Count / 2)
                    {
                        cycle.RemoveRange(smallerIndex, largerIndex - smallerIndex);
                    } else
                    {
                        cycle.RemoveRange(largerIndex, cycle.Count - largerIndex);
                        cycle.RemoveRange(0, smallerIndex);
                    }
                    return true;
                }
            }
            return false;
        }


        public static bool IsCCW(List<Edge> polygon)
        {
            VertexNode right = polygon[0].Src;
            foreach (Edge e in polygon)
            {
                if (e.Src.Vert.Position.X > right.Vert.Position.X)
                {
                    right = e.Src;
                }
            }

            Edge edgeIn = polygon.Where(e => e.Dst == right).First();
            Edge edgeOut = polygon.Where(e => e.Src == right).First();
            return Edge.IsLeftTurn(edgeIn, edgeOut, 0);
        }

        public static void EnsureCCW(List<Edge> polygon)
        {        
            if (!IsCCW(polygon))
            {
                polygon.ForEach(e =>
                {
                    var tmp = e.Src;
                    e.Src = e.Dst;
                    e.Dst = tmp;
                });
                polygon.Reverse();
            }
        }

        /// <summary>
        /// Returns the largest (by 2d XY bounding box) closed polygonal boundary
        /// </summary>
        /// <param name="trimSubcycles"></param>
        /// <returns></returns>
        public List<Edge> GetLargestPolygonalBoundary(bool trimSubcycles = true)
        {
            var edges = GetPerimeterEdges();
            List<Edge> currentGroup;
            HashSet<Edge> usedEdges;
            List<Edge> perimeterEdges = null;
            double maxArea = 0.0;
            foreach (Edge firstEdge in edges)
            {
                if (!firstEdge.IsPerimeterEdge)
                {
                    continue;
                }
                currentGroup = new List<Edge> { firstEdge };
                usedEdges = new HashSet<Edge>() { firstEdge };
                List<Edge> splits = new List<Edge>();
                List<int> splitIdxs = new List<int>();
                Edge current = firstEdge;
                bool closed = false;
                //search for a closed loop of perimeter edges
                while (!closed)
                {
                    bool foundNextEdge = false;
                    foreach (Edge other in current.Dst.AdjacentEdges)
                    {
                        if (other.Dst != current.Src && other.Left != null && other.IsPerimeterEdge && !usedEdges.Contains(other))
                        {
                            if (foundNextEdge)
                            {
                                //if already found a next edge, save this as an option to backtrack to
                                splits.Add(other);
                                splitIdxs.Add(currentGroup.Count - 1);
                            }
                            else
                            {
                                //add the next edge to the group
                                foundNextEdge = true;
                                currentGroup.Add(other);
                                usedEdges.Add(other);
                                current = other;
                            }
                        }
                    }
                    if (!foundNextEdge)
                    {
                        //Backtrack to last split
                        current = null;
                        while (splits.Count > 0)
                        {
                            current = splits.Last();
                            if (usedEdges.Contains(current))
                            {
                                current = null;
                                splits.RemoveAt(splits.Count - 1);
                                splitIdxs.RemoveAt(splitIdxs.Count - 1);
                                continue;
                            }
                            int idx = splitIdxs.Last();
                            currentGroup = currentGroup.Take(idx).ToList();
                            currentGroup.Add(current);
                        }
                        if (current == null)
                        {
                            //Failed to find a closed loop
                            currentGroup = null;
                            break;
                        }
                    }
                    closed = (current.Dst == firstEdge.Src);
                }
                if (currentGroup != null)
                {
                    foreach (Edge e in currentGroup)
                    {
                        e.IsPerimeterEdge = false; //Flag as used
                    }
                    //Keep the largest (area) group of edges
                    var size = BoundingBox.CreateFromPoints(currentGroup.Select(e => e.Src.Vert.Position)).Size();
                    var area = size.X * size.Y;
                    if (area > maxArea)
                    {
                        maxArea = area;
                        perimeterEdges = currentGroup;
                    }
                }
            }

            //Clip subcycles
            if (trimSubcycles)
            {
                while (EdgeGraph.ClipSubcycle(perimeterEdges)) { }
            }

            return perimeterEdges;
        }

        /// <summary>
        /// Returns the nodes that fall on the mesh perimeter, note that non-perimeter edges can exist between two nodes on the perimeter
        /// </summary>
        /// <returns></returns>
        public List<VertexNode> GetPerimeterNodes()
        {
            var res = new List<VertexNode>();
            foreach (VertexNode v in VertNodes)
            {
                if (v.IsActive && v.IsOnPerimeter)
                {
                    res.Add(v);
                }
            }
            return res;
        }

        /// <summary>
        /// Returns the edges on the mesh perimeter
        /// </summary>
        /// <returns></returns>
        public List<Edge> GetPerimeterEdges()
        {
            var res = new List<Edge>();
            foreach(VertexNode v in VertNodes)
            {
                if(v.IsActive)
                {
                    foreach (Edge e in v.AdjacentEdges)
                    {
                        if(e.IsPerimeterEdge && e.Left != null)
                        {
                            res.Add(e);
                        }
                    }
                }
            }
            return res;
        }
    }
}
