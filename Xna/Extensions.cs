using System;


namespace Microsoft.Xna.Framework
{ 
    /// <summary>
    /// Extensions to the Xna framework classes
    /// </summary>
    public static class Extensions
    {

        /*
         *  Vector Max and Min
         */
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

        /*
         * Vector cross dot products
         */
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
        /*
         * Almost Equals Methods
         */
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

        public static bool AlmostEqual(this double a, double b, double eps = MathHelper.Epsilon)
        {
            return Math.Abs(a - b) < eps;
        }
        /*
         * Bounding Box
         */
        public static Vector3 Extent(this BoundingBox a)
        {
            return a.Max - a.Min;
        }

        private static void FindMinMax(double x0, double x1, double x2, out double min, out double max)
        {
          min = max = x0;
          if (x1 < min) min = x1;
          if (x1 > max) max = x1;
          if (x2 < min) min = x2;
          if (x2 > max) max = x2;
        }

        private static bool PlaneBoxOverlap(ref Vector3 normal, ref Vector3 vert, ref Vector3 maxbox)
        {
          Vector3 vmin;
          Vector3 vmax;

          if (normal.X > 0.0)
          {
              vmin.X = -maxbox.X - vert.X;
              vmax.X = maxbox.X - vert.X;
          }
          else
          {
              vmin.X = maxbox.X - vert.X;
              vmax.X = -maxbox.X - vert.X;
          }

          if (normal.Y > 0.0)
          {
              vmin.Y = -maxbox.Y - vert.Y;
              vmax.Y = maxbox.Y - vert.Y;
          }
          else
          {
              vmin.Y = maxbox.Y - vert.Y;
              vmax.Y = -maxbox.Y - vert.Y;
          }

          if (normal.Z > 0.0)
          {
              vmin.Z = -maxbox.Z - vert.Z;
              vmax.Z = maxbox.Z - vert.Z;
          }
          else
          {
              vmin.Z = maxbox.Z - vert.Z;
              vmax.Z = -maxbox.Z - vert.Z;
          }

          if (Vector3.Dot(normal, vmin) > 0.0) return false;
          if (Vector3.Dot(normal, vmax) >= 0.0) return true;

          return false;
        }
    }
}
