using System;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace JPLOPS.MathExtensions
{
    /// Also see Microsoft.Xna.Framework.Extensions and OPS.Geometry.BoundingBoxExtensions
    public static class XNAExtensions
    {
        /// <summary>
        /// https://computergraphics.stackexchange.com/a/1719
        /// </summary>
        public static double Curvature(Vector3 p1, Vector3 p2, Vector3 n1, Vector3 n2)
        {
            var d = p2 - p1;
            return (n2 - n1).Dot(d) / d.LengthSquared();
        }

        /// <summary>
        /// convert XNA Matrix to row major 16 element array
        /// </summary>
        public static double[] ToDoubleArray(this Matrix mat)
        {
            return Matrix.TodoubleArray(mat); //sic
        }

        /// <summary>
        /// convert row major 16 element array to XNA Matrix  
        /// </summary>
        public static Matrix MatrixFromArray(double[] mat)
        {
            return new Matrix(mat[0], mat[1], mat[2], mat[3],
                              mat[4], mat[5], mat[6], mat[7],
                              mat[8], mat[9], mat[10], mat[11],
                              mat[12], mat[13], mat[14], mat[15]);
        }

        public static Vector3 ToYawPitchRoll(this Quaternion q)
        {
            //yaw Y, pitch X, roll Z
            //CreateFromYawPitchRoll(yaw, pitch, roll)
		    //sr = Math.Sin(roll * 0.5);
		    //cr = Math.Cos(roll * 0.5);
		    //sp = Math.Sin(pitch * 0.5);
		    //cp = Math.Cos(pitch * 0.5);
		    //sy = Math.Sin(yaw * 0.5);
		    //cy = Math.Cos(yaw * 0.5);
		    //W = cy * cp * cr + sy * sp * sr;
		    //X = cy * sp * cr + sy * cp * sr;
		    //Y = sy * cp * cr - cy * sp * sr;
		    //Z = cy * cp * sr - sy * sp * cr;

            //swap y and r
		    //W = cr * cp * cy + sr * sp * sy;
		    //X = cr * sp * cy + sr * cp * sy;
		    //Y = sr * cp * cy - cr * sp * sy;
		    //Z = cr * cp * sy - sr * sp * cy;

            //swap Y and X
		    //W = cr * cp * cy + sr * sp * sy;
		    //X = sr * cp * cy - cr * sp * sy;
		    //Y = cr * sp * cy + sr * cp * sy;
		    //Z = cr * cp * sy - sr * sp * cy;
            //matches https://en.wikipedia.org/wiki/Conversion_between_quaternions_and_Euler_angles#Source_Code

            // ---

            //https://en.wikipedia.org/wiki/Conversion_between_quaternions_and_Euler_angles#Source_Code_2
            //sr * cp = 2 * (W * X + Y * Z)
            //cr * cp = 1 - 2 * (X * X + Y * Y)
            //     sp = 2 * (W * Y - Z * X)
            //sy * cp = 2 * (W * Z + X * Y)
            //cy * cp = 1 - 2 * (Y * Y + Z * Z)

            //swap Y and X

            //sr * cp = 2 * (W * Y + X * Z)
            //cr * cp = 1 - 2 * (Y * Y + X * X)
            //     sp = 2 * (W * X - Z * Y)
            //sy * cp = 2 * (W * Z + Y * X)
            //cy * cp = 1 - 2 * (X * X + Z * Z)

            //swap y and r
            //sy * cp = 2 * (W * Y + X * Z)
            //cy * cp = 1 - 2 * (Y * Y + X * X)
            //     sp = 2 * (W * X - Z * Y)
            //sr * cp = 2 * (W * Z + Y * X)
            //cr * cp = 1 - 2 * (X * X + Z * Z)

            double siny_cosp = 2 * (q.W * q.Y + q.X * q.Z);
            double cosy_cosp = 1 - 2 * (q.Y * q.Y + q.X * q.X);
            double yaw = Math.Atan2(siny_cosp, cosy_cosp);
            
            double sinp = 2 * (q.W * q.X - q.Z * q.Y);
            double pitch = Math.Abs(sinp) >= 1 ? 0.5 * Math.PI * Math.Sign(sinp) : Math.Asin(sinp);
            
            double sinr_cosp = 2 * (q.W * q.Z + q.Y * q.X);
            double cosr_cosp = 1 - 2 * (q.X * q.X + q.Z * q.Z);
            double roll = Math.Atan2(sinr_cosp, cosr_cosp);

            return new Vector3(yaw, pitch, roll);
        }

        public static string ToStringEuler(this Quaternion q, string fmt = "f3")
        {
            double rad2deg = 180 / Math.PI;
            var ypr = q.ToYawPitchRoll();
            return string.Format($"({{0:{fmt}}}, {{1:{fmt}}}, {{2:{fmt}}})deg",
                                 ypr.X * rad2deg, ypr.Y * rad2deg, ypr.Z * rad2deg);
        }

        public static string ToStringEuler(this Matrix m, string fmt = "f3")
        {
            if (m == Matrix.Identity)
            {
                return "identity";
            }

            m.Decompose(out Vector3 s, out Quaternion q, out Vector3 t);

            string ret = "";
            if (Math.Abs(1 - s.X) > 1e-6 || Math.Abs(1 - s.Y) > 1e-6 || Math.Abs(1 - s.Z) > 1e-6)
            {
                ret = string.Format($"(sx, sy, sz) = ({{0:{fmt}}}, {{1:{fmt}}}, {{2:{fmt}}})", s.X, s.Y, s.Z);
            }
            if (q.ToRotationVector().Length() > 1e-6)
            {
                ret += (!string.IsNullOrEmpty(ret) ? " " : "") + $"(y, p, r) = {q.ToStringEuler(fmt)}";
            }
            if (t.Length() > 1e-6)
            {
                ret += (!string.IsNullOrEmpty(ret) ? " " : "") +
                    string.Format($"(tx, ty, tz) = ({{0:{fmt}}}, {{1:{fmt}}}, {{2:{fmt}}})", t.X, t.Y, t.Z);
            }

            return ret;
        }

        public static Vector3 ToRotationVector(this Quaternion q)
        {
            return q.ToRodriguesVector() * 2;
        }

        public static Vector2 XY(this Vector3 v)
        {
            return new Vector2(v.X, v.Y);
        }

        public static Vector3 Invert(this Vector3 v)
        {
            return new Vector3(1 / v.X, 1 / v.Y, 1 / v.Z);
        }

        public static Vector2 Invert(this Vector2 v)
        {
            return new Vector2(1 / v.X, 1 / v.Y);
        }

        public static Vector2 Swap(this Vector2 v)
        {
            return new Vector2(v.Y, v.X);
        }

        public static bool IsFinite(this Vector3 v)
        {
            return MathE.IsFinite(v.X) && MathE.IsFinite(v.Y) && MathE.IsFinite(v.Z);
        }

        public static bool IsFinite(this Vector2 v)
        {
            return MathE.IsFinite(v.X) && MathE.IsFinite(v.Y);
        }

        public static RTree.Rectangle ToRectangle(this Vector3 v)
        {
            return new RTree.Rectangle((float)v.X, (float)v.Y, //x1, y1
                                       (float)v.X, (float)v.Y, //x2, y2
                                       (float)v.Z, (float)v.Z); //z1, z2 (yes, z last)
        }

        public static RTree.Rectangle ToRectangle(this Vector3 v, double minSize)
        {
            float r = (float)(0.5 * minSize);
            return new RTree.Rectangle((float)v.X - r, (float)v.Y - r, //x1, y1
                                       (float)v.X + r, (float)v.Y + r, //x2, y2
                                       (float)v.Z - r, (float)v.Z + r); //z1, z2 (yes, z last)
        }

        public static RTree.Point ToRTreePoint(this Vector3 v)
        {
            return new RTree.Point((float)v.X, (float)v.Y, (float)v.Z);
        }
    }

    public class XNAMatrixJsonConverter : JsonConverter
    {
        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return true; } }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Matrix);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, ((Matrix)value).ToDoubleArray());
        }
        
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        {
            return XNAExtensions.MatrixFromArray(serializer.Deserialize<double[]>(reader));
        }
    }
}
