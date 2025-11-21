using Microsoft.Xna.Framework;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Class to store barycentric coordinates of a point within a triangle
    /// </summary>
    public class BarycentricPoint
    {
        private bool isST; // Was this created using ST or b0,b1,b2

        private double s; // normalized along V0 -> V1
        private double t; // normalized along V0 -> V2

        private double b0; // barycentric coordinates
        private double b1;
        private double b2;

        public Triangle tri;

        public bool OnTriEdgeUV(out Vector2 intersectedEdge, out Vector2 otherEdge)
        {
            if(isST && s < 1E-8 || !isST && b2 < 1E-8)
            {
                intersectedEdge = tri.V1.UV - tri.V0.UV;
                otherEdge = tri.V2.UV - tri.V0.UV;
                return true;
            }
            else if (isST && t < 1E-8 || !isST && b1 < 1E-8)
            {
                intersectedEdge = tri.V2.UV - tri.V0.UV;
                otherEdge = tri.V1.UV - tri.V0.UV;
                return true;
            }
            else if (isST && s + t > 1 - 1E-8 || !isST && b0 < 1E-8)
            {
                intersectedEdge = tri.V2.UV - tri.V1.UV;
                otherEdge = tri.V0.UV - tri.V1.UV;
                return true;
            }
            intersectedEdge = new Vector2(0, 0);
            otherEdge = intersectedEdge;
            return false;
        }

        /// <summary>
        /// Create a new point
        /// </summary>
        /// <param name="s">Ratio along V0->V1 between 0-1</param>
        /// <param name="t">Ratio along V0->V2 between 0-1</param>
        /// <param name="tri"></param>
        public BarycentricPoint(double s, double t, Triangle tri)
        {
            this.isST = true;
            this.s = s;
            this.t = t;
            this.tri = tri;
        }

        /// <summary>
        /// Create a new point using area barycentric coordinates
        /// </summary>
        /// <param name="b0">V0 normalzied 0-1</param>
        /// <param name="b1">V1 normalzied 0-1</param>
        /// <param name="b2">V2 normalzied 0-1</param>
        /// <param name="tri"></param>
        public BarycentricPoint(double b0, double b1, double b2, Triangle tri)
        {
            this.isST = false;
            this.b0 = b0;
            this.b1 = b1;
            this.b2 = b2;
            this.tri = tri;
        }

        public Vector3 Position
        {
            get
            {
                if (isST)
                {
                    return tri.V0.Position +
                        s * (tri.V1.Position - tri.V0.Position) + t * (tri.V2.Position - tri.V0.Position);
                }
                else
                {
                    return b0 * tri.V0.Position + b1 * tri.V1.Position + b2 * tri.V2.Position;
                }
            }
        }

        public Vector3 Normal
        {
            get
            {
                if (isST)
                {
                    return tri.V0.Normal + s * (tri.V1.Normal - tri.V0.Normal) + t * (tri.V2.Normal - tri.V0.Normal);
                }
                else
                {
                    return b0 * tri.V0.Normal + b1 * tri.V1.Normal + b2 * tri.V2.Normal;
                }
            }
        }

        public Vector2 UV
        {
            get
            {
                if (isST)
                {
                    return tri.V0.UV + s * (tri.V1.UV - tri.V0.UV) + t * (tri.V2.UV - tri.V0.UV);
                }
                else
                {
                    return b0 * tri.V0.UV + b1 * tri.V1.UV + b2 * tri.V2.UV;
                }
            }
        }

        public Vector4 Color
        {
            get
            {
                if (isST)
                {
                    return tri.V0.Color + s * (tri.V1.Color - tri.V0.Color) + t * (tri.V2.Color - tri.V0.Color);
                }
                else
                {
                    return b0 * tri.V0.Color + b1 * tri.V1.Color + b2 * tri.V2.Color;
                }
            }
        }
    }
}
