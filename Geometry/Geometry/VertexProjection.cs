using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    /// <summary>
    /// Consolidate projection logic into single class
    /// </summary>
    public static class VertexProjection
    {
        public enum ProjectionAxis
        {
            None,
            X,
            Y,
            Z
        }

        /// <summary>
        /// Return xyz projected along given axis
        /// </summary>
        /// <param name="xyz"></param>
        /// <param name="axis"></param>
        /// <returns></returns>
        public static Vector2 GetUV(Vector3 xyz, ProjectionAxis axis)
        {
            switch (axis)
            {
                case ProjectionAxis.X:
                    return new Vector2(xyz.Y, xyz.Z);
                case ProjectionAxis.Y:
                    return new Vector2(xyz.X, xyz.Z);
                case ProjectionAxis.Z:
                    return new Vector2(xyz.X, xyz.Y);
                default:
                    throw new Exception("Getting UV requires projection axis.");
            }
        }

        /// <summary>
        /// Return the height of xyz along given axis
        /// </summary>
        /// <param name="xyz"></param>
        /// <param name="axis"></param>
        /// <returns></returns>
        public static double GetHeight(Vector3 xyz, ProjectionAxis axis)
        {
            switch (axis)
            {
                case ProjectionAxis.X:
                    return xyz.X;
                case ProjectionAxis.Y:
                    return xyz.Y;
                case ProjectionAxis.Z:
                    return xyz.Z;
                default:
                    throw new Exception("Getting height requires projection axis.");
            }
        }

        /// <summary>
        /// Set the height of vertex v to h, given vertical axis
        /// </summary>
        /// <param name="v"></param>
        /// <param name="h"></param>
        /// <param name="axis"></param>
        public static void SetHeight(Vertex v, double h, ProjectionAxis axis)
        {
            switch (axis)
            {
                case ProjectionAxis.X:
                    v.Position.X = h;
                    break;
                case ProjectionAxis.Y:
                    v.Position.Y = h;
                    break;
                case ProjectionAxis.Z:
                    v.Position.Z = h;
                    break;
                default:
                    throw new Exception("Setting height requires shrinkwrap axis.");
            }
        }
    }
}
