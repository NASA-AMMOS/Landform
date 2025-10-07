
using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Stores two VertexNodes, the third VertexNode of its left face (for winding order), and the location of the collapsed vertex
    /// </summary>
    public class CollapsableEdge : Edge
    {
        private CollapsableVertexNode _src;
        private CollapsableVertexNode _dst;
        private CollapsableVertexNode _left;
        public override VertexNode Src
        {
            get { return _src; }
            set { _src = (CollapsableVertexNode)value; }
        }
        public override VertexNode Dst
        {
            get { return _dst; }
            set { _dst = (CollapsableVertexNode)value; }
        }
        public override VertexNode Left
        {
            get { return _left; }
            set { _left = (CollapsableVertexNode)value; }
        }
        public Vertex VNew;

        public CollapsableEdge(CollapsableVertexNode v1, CollapsableVertexNode v2, CollapsableVertexNode left, bool isPerimeterEdge = false) 
            : base(v1, v2, left, isPerimeterEdge)
        {
            SetNewVertPos();
        }

        public CollapsableEdge(CollapsableVertexNode v1, CollapsableVertexNode v2, CollapsableVertexNode left, Vertex vNew, bool isPerimeterEdge = false)
            : base(v1, v2, left, isPerimeterEdge)
        {
            this.VNew = vNew;
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
            return (Matrix.Transpose(v) * (((CollapsableVertexNode)Src).Q + ((CollapsableVertexNode)Dst).Q) * v).M11;
        }

        /// <summary>
        /// Compares the error cost of collapsing this edge to either or the two end points, or the midpoint. Returns the best option.
        /// </summary>
        /// <returns></returns>
        public void SetNewVertPosSimple()
        {
            if (Src.IsOnPerimeter && !Dst.IsOnPerimeter || !((CollapsableVertexNode)Src).IsTouchable 
                                                         && ((CollapsableVertexNode)Dst).IsTouchable)
            {
                VNew = Src;
                return;
            }
            if (Dst.IsOnPerimeter && !Src.IsOnPerimeter || !((CollapsableVertexNode)Dst).IsTouchable
                                                         && ((CollapsableVertexNode)Src).IsTouchable)
            {
                VNew = Dst;
                return;
            }
            Vertex v1 = this.Src;
            VNew = v1;
            double minCost = QEM(v1);
            Vertex v2 = this.Dst;
            if (QEM(v2) < minCost)
            {
                VNew = v2;
                minCost = QEM(v2);
            }
            Vertex mid = new Vertex((Src.Position.X + Dst.Position.X) / 2, (Src.Position.Y + Dst.Position.Y) / 2, (Src.Position.Z + Dst.Position.Z) / 2);
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
            if (Src.IsOnPerimeter && !Dst.IsOnPerimeter || !((CollapsableVertexNode)Src).IsTouchable
                                                         && ((CollapsableVertexNode)Dst).IsTouchable)
            {
                VNew = Src;
                return;
            }
            if (Dst.IsOnPerimeter && !Src.IsOnPerimeter || !((CollapsableVertexNode)Dst).IsTouchable
                                                         && ((CollapsableVertexNode)Src).IsTouchable)
            {
                VNew = Dst;
                return;
            }
            if (!IsPerimeterEdge)
            {
                Matrix Q = ((CollapsableVertexNode)Src).Q + ((CollapsableVertexNode)Dst).Q;
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
