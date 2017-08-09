using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OPS.MathExtensions;
using Microsoft.Xna.Framework;
using OPS.Geometry;

namespace OPS.Geometry
{
    /// <summary>
    /// Class to store s, t coordinates of a point within a triangle (using triangle edges as basis vectors relative to one corner)
    /// </summary>
    public class BarycentricPoint
    {
        bool isST; // Was this created using ST or b0,b1,b2

        double s; // normalized along V0 -> V1
        double t; // normalized along V0 -> V2

        double b0; // barycentric coordinates
        double b1;
        double b2;

        public Triangle tri;

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
                    return tri.V0.Position + s * (tri.V1.Position - tri.V0.Position) + t * (tri.V2.Position - tri.V0.Position);
                else
                    return b0 * tri.V0.Position + b1 * tri.V1.Position + b2 * tri.V2.Position;
            }
        }

        public Vector3 Normal
        {
            get
            {
                if (isST)
                    return tri.V0.Normal + s * (tri.V1.Normal - tri.V0.Normal) + t * (tri.V2.Normal - tri.V0.Normal);
                else
                    return b0 * tri.V0.Normal + b1 * tri.V1.Normal + b2 * tri.V2.Normal;
            }
        }

        public Vector2 UV
        {
            get
            {
                if (isST)
                    return tri.V0.UV + s * (tri.V1.UV - tri.V0.UV) + t * (tri.V2.UV - tri.V0.UV);
                else
                    return b0 * tri.V0.UV + b1 * tri.V1.UV + b2 * tri.V2.UV;
            }
        }

        public Vector4 Color
        {
            get
            {
                if (isST)
                    return tri.V0.Color + s * (tri.V1.Color - tri.V0.Color) + t * (tri.V2.Color - tri.V0.Color);
                else
                    return b0 * tri.V0.Color + b1 * tri.V1.Color + b2 * tri.V2.Color;
            }
        }
    }
}
