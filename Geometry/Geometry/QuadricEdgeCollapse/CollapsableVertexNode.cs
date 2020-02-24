using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace OPS.Geometry
{
    /// <summary>
    /// Stores a vertex with its associated error matrix, edges, and flags for representing meshes as node-edge graphs when doing edge collapses
    /// </summary>
    public class CollapsableVertexNode : VertexNode
    {
        public new List<CollapsableEdge> AdjacentEdges = new List<CollapsableEdge>();
        public Matrix Q;
        public bool IsTouchable;
        public bool IsActive;
        public double cost;

        public CollapsableVertexNode(Vertex vert, int id) : base (vert, id)
        {
            this.Q = new Matrix();
            this.IsTouchable = true;
            this.IsActive = true;
            this.cost = 0;
        }

        public CollapsableVertexNode(Vertex vert, int id, Matrix Q, int adjFaceCount, List<Edge> adjacentEdges, bool isOnPerimeter, bool isTouchable) 
            : base(vert, id, adjFaceCount, adjacentEdges, isOnPerimeter)
        {
            this.Q = Q;
            this.IsTouchable = isTouchable;
            this.IsActive = true;
            this.cost = 0;
        }
    }
}
