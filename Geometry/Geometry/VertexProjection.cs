using Microsoft.Xna.Framework;
using System;

namespace JPLOPS.Geometry
{
    /// <summary>
    /// Consolidate projection logic into single class
    /// </summary>
    public static class VertexProjection
    {
        public enum ProjectionAxis { None, X, Y, Z }

        public static Func<Vector3, Vector2> MakeUVProjector(ProjectionAxis axis)
        {
            return new Func<Vector3, Vector2>(v =>
            {
                switch (axis) {
                    case ProjectionAxis.X: return new Vector2(v.Y, v.Z);
                    case ProjectionAxis.Y: return new Vector2(v.X, v.Z);
                    case ProjectionAxis.Z: return new Vector2(v.X, v.Y);
                    default: throw new Exception("unknown projection axis: " + axis);
                }
            });
        }

        public static Func<Vector3, double> MakeHeightGetter(ProjectionAxis axis)
        {
            return new Func<Vector3, double>(v =>
            {
                switch (axis)
                {
                    case ProjectionAxis.X: return v.X;
                    case ProjectionAxis.Y: return v.Y;
                    case ProjectionAxis.Z: return v.Z;
                    default: throw new Exception("unknown projection axis: " + axis);
                }
            });
        }
            
        public static Action<Vertex, double> MakeHeightSetter(ProjectionAxis axis)
        {
            return new Action<Vertex, double>((v, h) =>
            {
                switch (axis)
                {
                    case ProjectionAxis.X: v.Position.X = h; break;
                    case ProjectionAxis.Y: v.Position.Y = h; break;
                    case ProjectionAxis.Z: v.Position.Z = h; break;
                    default: throw new Exception("unknown projection axis: " + axis);
                }
            });
        }
    }
}
