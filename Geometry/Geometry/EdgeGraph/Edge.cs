using System;

using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Stores two VertexNodes, the third VertexNode of its left face (for winding order).
    /// </summary>
    public class Edge
    {
        private VertexNode _src;
        private VertexNode _dst;
        private VertexNode _left;
        public virtual VertexNode Src
        {
            get { return _src; }
            set { _src = value; }
        }
        public virtual VertexNode Dst
        {
            get { return _dst; }
            set { _dst = value; }
        }
        public virtual VertexNode Left
        {
            get { return _left; }
            set { _left = value; }
        }
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
            return Src.GetHashCode() + Dst.GetHashCode();
        }

        public static bool IsLeftTurn(Edge e1, Edge e2, double eps)
        {            
            Vector3 a = e2.Dst.Position - e2.Src.Position;
            a.Z = 0;
            Vector3 b = e1.Src.Position - e1.Dst.Position;
            b.Z = 0;
            return Vector3.Cross(a, b).Z > 0 - eps;
        }

        public static bool IsCollinear(Edge e1, Edge e2, double eps = 1E-8)
        {
            Vector3 a = e2.Dst.Position - e2.Src.Position;
            a.Z = 0;
            Vector3 b = e1.Src.Position - e1.Dst.Position;
            b.Z = 0;
            return Math.Abs(Vector3.Cross(a, b).Z) <= eps;
        }
    }
}
