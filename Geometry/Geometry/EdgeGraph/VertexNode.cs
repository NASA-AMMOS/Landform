using System;
using System.Collections.Generic;
using System.Linq;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Stores a vertex with its associated error matrix, edges, and flags for representing meshes as node-edge graphs when doing edge collapses
    /// </summary>
    public class VertexNode : Vertex
    {
        private List<Edge> AdjacentEdges = new List<Edge>();
        public bool IsOnPerimeter;
        public int AdjFaceCount;
        public int ID;

        public VertexNode(Vertex vert, int id) : base(vert)
        {
            this.ID = id;
            this.AdjFaceCount = 0;
            this.IsOnPerimeter = false;
        }

        public VertexNode(Vertex vert, int id, int adjFaceCount, List<Edge> adjacentEdges, bool isOnPerimeter) : base(vert)
        {
            this.ID = id;
            this.AdjFaceCount = adjFaceCount;
            this.AdjacentEdges = adjacentEdges;
            this.IsOnPerimeter = isOnPerimeter;
        }

        public virtual void AddEdge(Edge e)
        {
            AdjacentEdges.Add(e);
        }

        public virtual bool ContainsEdge(Edge e)
        {
            return AdjacentEdges.Contains(e);
        }

        public virtual Edge FindEdge(Predicate<Edge> p)
        {
            return AdjacentEdges.Find(p);
        }

        public virtual IEnumerable<Edge> FindAllEdges(Predicate<Edge> p)
        {
            return AdjacentEdges.FindAll(p);
        }

        public virtual void RemoveEdge(Edge e)
        {
            AdjacentEdges.Remove(e);
        }

        public virtual IEnumerable<Edge> GetAdjacentEdges()
        {
            foreach (Edge e in AdjacentEdges)
            {
                yield return e;
            }
        }

        public virtual void FilterEdges(Func<Edge, bool> p)
        {
            AdjacentEdges = AdjacentEdges.Where(p).ToList();
        }

        public static bool operator <(VertexNode v1, VertexNode v2)
        {
            return v1.ID < v2.ID;
        }

        public static bool operator >(VertexNode v1, VertexNode v2)
        {
            return v1.ID > v2.ID;
        }
    }
}
