using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;
using OPS.Geometry;
using Priority_Queue;

namespace OPS.Geometry
{
    /// <summary>
    /// Stores two VertexNodes, the third VertexNode of its left face (for winding order), and the location of the collapsed vertex
    /// </summary>
    public class Edge
    {
        public VertexNode Src, Dst;
        public VertexNode Left;
        public Vertex VNew;
        public bool IsPerimeterEdge;

        public Edge(VertexNode v1, VertexNode v2, VertexNode left, bool isPerimeterEdge = false)
        {
            this.Src = v1;
            this.Dst = v2;
            this.Left = left;
            this.IsPerimeterEdge = isPerimeterEdge;
            SetNewVertPos();
        }

        public Edge(VertexNode v1, VertexNode v2, VertexNode left, Vertex vNew, bool isPerimeterEdge = false)
        {
            this.Src = v1;
            this.Dst = v2;
            this.Left = left;
            this.IsPerimeterEdge = isPerimeterEdge;
            this.VNew = vNew;
        }

        public override bool Equals(object obj)
        {
            if (obj == null || this.GetType() != obj.GetType())
            {
                return false;
            }
            Edge edgeObj = (Edge)obj;
            if (Object.ReferenceEquals(this.Src, edgeObj.Src) && Object.ReferenceEquals(this.Dst, edgeObj.Dst))
            {
                return true;
            }
            if (Object.ReferenceEquals(this.Src, edgeObj.Dst) && Object.ReferenceEquals(this.Dst, edgeObj.Src))
            {
                return true;
            }
            return false;
        }

        public static bool operator ==(Edge lhs, Edge rhs)
        {
            if (Object.ReferenceEquals(lhs, null))
            {
                return Object.ReferenceEquals(rhs, null);
            }
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

        public static bool IsLeftTurn(Edge e1, Edge e2, double eps)
        {            
            Vector3 a = e2.Dst.Vert.Position - e2.Src.Vert.Position;
            a.Z = 0;
            Vector3 b = e1.Src.Vert.Position - e1.Dst.Vert.Position;
            b.Z = 0;
            return Vector3.Cross(a, b).Z > 0 - eps;
        }

        public static bool IsColinear(Edge e1, Edge e2, double eps = 1E-8)
        {
            Vector3 a = e2.Dst.Vert.Position - e2.Src.Vert.Position;
            a.Z = 0;
            Vector3 b = e1.Src.Vert.Position - e1.Dst.Vert.Position;
            b.Z = 0;
            return Math.Abs(Vector3.Cross(a, b).Z) <= eps;
        }

        /// <summary>
        /// Compute Quadric Error Metric (QEM) for a new vertex position using sum of Q matrices
        /// </summary>
        /// <param name="vert"></param>
        /// <param name="Q"></param>
        /// <returns></returns>
        public double QEM(Vertex vNew)
        {
            Matrix v = new Matrix(vNew.Position.X, 0, 0, 0, vNew.Position.Y, 0, 0, 0, vNew.Position.Z, 0, 0, 0, 1, 0, 0, 0);
            return (Matrix.Transpose(v) * (Src.Q + Dst.Q) * v).M11;
        }

        /// <summary>
        /// Compares the error cost of collapsing this edge to either or the two end points, or the midpoint. Returns the best option.
        /// </summary>
        /// <returns></returns>
        public void SetNewVertPosSimple()
        {
            if (Src.IsOnPerimeter && !Dst.IsOnPerimeter || !Src.IsTouchable && Dst.IsTouchable)
            {
                VNew = Src.Vert;
                return;
            }
            if (Dst.IsOnPerimeter && !Src.IsOnPerimeter || !Dst.IsTouchable && Src.IsTouchable)
            {
                VNew = Dst.Vert;
                return;
            }
            Vertex v1 = this.Src.Vert;
            VNew = v1;
            double minCost = QEM(v1);
            Vertex v2 = this.Dst.Vert;
            if (QEM(v2) < minCost)
            {
                VNew = v2;
                minCost = QEM(v2);
            }
            Vertex mid = new Vertex((Src.Vert.Position.X + Dst.Vert.Position.X) / 2, (Src.Vert.Position.Y + Dst.Vert.Position.Y) / 2, (Src.Vert.Position.Z + Dst.Vert.Position.Z) / 2);
            if (QEM(mid) < minCost)
            {
                VNew = mid;
            }
        }

        /// <summary>
        /// Returns the position of the Vertex to create upon collapsing this edge. Attempts to find local minimum in error, otherwise defaults to comparing ends and midpoint. Restriced for edges and user-specified vertices.
        /// </summary>
        /// <returns></returns>
        public void SetNewVertPos()
        {
            if (Src.IsOnPerimeter && !Dst.IsOnPerimeter || !Src.IsTouchable && Dst.IsTouchable)
            {
                VNew = Src.Vert;
                return;
            }
            if (Dst.IsOnPerimeter && !Src.IsOnPerimeter || !Dst.IsTouchable && Src.IsTouchable)
            {
                VNew = Dst.Vert;
                return;
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
                    VNew = new Vertex(res.M11, res.M21, res.M31);
                    return;
                }
            }
            SetNewVertPosSimple();
        }
    }
}
