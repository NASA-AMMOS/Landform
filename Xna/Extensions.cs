using System;

namespace Microsoft.Xna.Framework
{ 
    /// <summary>
    /// Extensions to the Xna framework classes
    /// Also see OPS.MathExtensions.XNAExtensions and OPS.Geometry.BoundingBoxExtensions
    /// </summary>
    public static class Extensions
    {
        public static Vector2 Max(this Vector2 a, Vector2 b)
        {
            return Vector2.Max(a, b);
        }

        public static Vector2 Min(this Vector2 a, Vector2 b)
        {
            return Vector2.Min(a, b);
        }

        public static Vector3 Max(this Vector3 a, Vector3 b)
        {
            return Vector3.Max(a, b);
        }

        public static Vector3 Min(this Vector3 a, Vector3 b)
        {
            return Vector3.Min(a, b);
        }

        public static Vector4 Max(this Vector4 a, Vector4 b)
        {
            return Vector4.Max(a, b);
        }

        public static Vector4 Min(this Vector4 a, Vector4 b)
        {
            return Vector4.Min(a, b);
        }

        public static double Dot(this Vector2 a, Vector2 b)
        {
            return Vector2.Dot(a, b);
        }

        public static double Dot(this Vector3 a, Vector3 b)
        {
            return Vector3.Dot(a, b);
        }

        public static Vector3 Cross(this Vector3 a, Vector3 b)
        {
            return Vector3.Cross(a, b);
        }

        public static bool AlmostEqual(this Vector2 a, Vector2 b, double eps = MathHelper.Epsilon)
        {
            return Vector2.AlmostEqual(a, b, eps);
        }

        public static bool AlmostEqual(this Vector3 a, Vector3 b, double eps = MathHelper.Epsilon)
        {
            return Vector3.AlmostEqual(a,b, eps);
        }

        public static bool AlmostEqual(this Vector4 a, Vector4 b, double eps = MathHelper.Epsilon)
        {
            return Vector4.AlmostEqual(a, b, eps);
        }

        public static bool AlmostEqual(this Matrix a, Matrix b, double eps = MathHelper.Epsilon)
        {
            double[] aa = Matrix.TodoubleArray(a);
            double[] ba = Matrix.TodoubleArray(b);
            for (int i = 0; i < aa.Length; i++)
            {
                var err = (aa[i] - ba[i]);
                if (Math.Abs(err) > eps) return false;
            }
            return true;
        }

        public static bool AlmostEqual(this double a, double b, double eps = MathHelper.Epsilon)
        {
            return Math.Abs(a - b) < eps;
        }
    }
}
