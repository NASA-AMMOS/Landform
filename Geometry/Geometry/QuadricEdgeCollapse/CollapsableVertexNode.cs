using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Stores a vertex with its associated error matrix, edges, and flags for representing meshes as node-edge graphs when doing edge collapses
    /// </summary>
    public class CollapsableVertexNode : VertexNode
    {
        private List<CollapsableEdge> AdjacentEdges = new List<CollapsableEdge>();
        public Matrix Q;
        public bool IsTouchable;
        public bool IsActive;
        public double cost;

        public CollapsableVertexNode(Vertex vert, int id) : base(vert, id)
        {
            this.Q = new Matrix();
            this.IsTouchable = true;
            this.IsActive = true;
            this.cost = 0;
        }

        public CollapsableVertexNode(Vertex vert, int id, Matrix Q, int adjFaceCount, List<CollapsableEdge> adjacentEdges, bool isOnPerimeter, bool isTouchable)
            : base(vert, id, adjFaceCount, null, isOnPerimeter)
        {
            this.AdjacentEdges = adjacentEdges;
            this.Q = Q;
            this.IsTouchable = isTouchable;
            this.IsActive = true;
            this.cost = 0;
        }

        public override void AddEdge(Edge e)
        {
            AdjacentEdges.Add((CollapsableEdge)e);
        }

        public override bool ContainsEdge(Edge e)
        {
            return AdjacentEdges.Contains((CollapsableEdge)e);
        }

        public override Edge FindEdge(Predicate<Edge> p)
        {
            return AdjacentEdges.Find(p);
        }

        public override IEnumerable<Edge> FindAllEdges(Predicate<Edge> p)
        {
            return AdjacentEdges.FindAll(p);
        }

        public override void RemoveEdge(Edge e)
        {
            AdjacentEdges.Remove((CollapsableEdge)e);
        }

        public override IEnumerable<Edge> GetAdjacentEdges()
        {
            foreach (CollapsableEdge e in AdjacentEdges)
            {
                yield return e;
            }
        }

        public override void FilterEdges(Func<Edge, bool> p)
        {
            AdjacentEdges = AdjacentEdges.Where(e => p(e)).ToList();
        }
    }
}
