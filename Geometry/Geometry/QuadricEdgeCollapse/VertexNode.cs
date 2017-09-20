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
    /// Stores a vertex with its associated error matrix, edges, and flags
    /// </summary>
    public class VertexNode
    {
        public Vertex Vert;
        public Matrix Q;
        public List<Edge> AdjacentEdges;
        public bool IsOnPerimeter;
        public bool IsTouchable;
        public bool IsActive;
        public int AdjFaceCount;
        public int ID;

        public VertexNode(Vertex vert, int id)
        {
            this.Vert = vert;
            this.ID = id;
            this.Q = new Matrix();
            this.AdjFaceCount = 0;
            this.AdjacentEdges = new List<Edge>();
            this.IsOnPerimeter = false;
            this.IsTouchable = true;
            this.IsActive = true;
        }

        public VertexNode(Vertex vert, int id, Matrix Q, int adjFaceCount, List<Edge> adjacentEdges, bool isOnPerimeter, bool isTouchable)
        {
            this.Vert = vert;
            this.ID = id;
            this.Q = Q;
            this.AdjFaceCount = adjFaceCount;
            this.AdjacentEdges = adjacentEdges;
            this.IsOnPerimeter = isOnPerimeter;
            this.IsTouchable = isTouchable;
            this.IsActive = true;
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
