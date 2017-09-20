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
    /// Stores two VertexData, and the third VertexData of its left face to store winding order
    /// </summary>
    public class Edge
    {
        public VertexNode Src, Dst;
        public VertexNode Left;
        public bool IsPerimeterEdge;

        public Edge(VertexNode v1, VertexNode v2, VertexNode left, bool isPerimeterEdge = false)
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
        public double QEM(Vertex vert)
        {
            Matrix v = new Matrix(vert.Position.X, 0, 0, 0, vert.Position.Y, 0, 0, 0, vert.Position.Z, 0, 0, 0, 1, 0, 0, 0);
            return (Matrix.Transpose(v) * (Src.Q + Dst.Q) * v).M11;
        }

        /// <summary>
        /// Compares the error cost of collapsing this edge to either or the two end points, or the midpoint. Returns the best option.
        /// </summary>
        /// <returns></returns>
        public Vertex GetNewVertPosSimple()
        {
            if (Src.IsOnPerimeter && !Dst.IsOnPerimeter || !Src.IsTouchable && Dst.IsTouchable)
            {
                return Src.Vert;
            }
            if (Dst.IsOnPerimeter && !Src.IsOnPerimeter || !Dst.IsTouchable && Src.IsTouchable)
            {
                return Dst.Vert;
            }
            Vertex v1 = this.Src.Vert;
            Vertex best = v1;
            double minCost = QEM(v1);
            Vertex v2 = this.Dst.Vert;
            if (QEM(v2) < minCost)
            {
                best = v2;
                minCost = QEM(v2);
            }
            Vertex mid = new Vertex((Src.Vert.Position.X + Dst.Vert.Position.X) / 2, (Src.Vert.Position.Y + Dst.Vert.Position.Y) / 2, (Src.Vert.Position.Z + Dst.Vert.Position.Z) / 2);
            if (QEM(mid) < minCost)
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
            if (Src.IsOnPerimeter && !Dst.IsOnPerimeter || !Src.IsTouchable && Dst.IsTouchable)
            {
                return Src.Vert;
            }
            if (Dst.IsOnPerimeter && !Src.IsOnPerimeter || !Dst.IsTouchable && Src.IsTouchable)
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
}
