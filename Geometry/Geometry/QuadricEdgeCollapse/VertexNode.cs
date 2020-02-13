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
    public class VertexNode : Vertex
    {
        public List<Edge> AdjacentEdges = new List<Edge>();
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
