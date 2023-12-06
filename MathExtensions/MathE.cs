using System;
using System.Linq;

namespace JPLOPS.MathExtensions
{
    public class MathE
    {
        public const double EPSILON = 1e-7;
        public const double SQRT_3 = 1.73205080757;

        public static byte Clamp(byte v, byte min, byte max)
        {
            if (v < min)
            {
                return min;
            }
            if (v > max)
            {
                return max;
            }
            return v;
        }

        public static int Clamp(int v, int min, int max)
        {
            if (v < min)
            {
                return min;
            }
            if (v > max)
            {
                return max;
            }
            return v;
        }

        public static long Clamp(long v, long min, long max)
        {
            if (v < min)
            {
                return min;
            }
            if (v > max)
            {
                return max;
            }
            return v;
        }

        public static float Clamp(float v, float min, float max)
        {
            if (v < min)
            {
                return min;
            }
            if (v > max)
            {
                return max;
            }
            return v;
        }

        public static double Clamp(double v, double min, double max)
        {
            if (v < min)
            {
                return min;
            }
            if (v > max)
            {
                return max;
            }
            return v;
        }

        public static float Clamp01(float v)
        {
            return Clamp(v, 0.0f, 1.0f);
        }

        public static double Clamp01(double v)
        {
            return Clamp(v, 0.0, 1.0);
        }

        public static float Lerp(float start, float end, float amount)
        {
            return start + (end - start) * amount;
        }

        public static double Lerp(double start, double end, double amount)
        {
            return start + (end - start) * amount;
        }

        public static int Max(params int[] values)
        {
            return Enumerable.Max(values);
        }

        public static int Min(params int[] values)
        {
            return Enumerable.Min(values);
        }

        public static float Max(params float[] values)
        {
            return Enumerable.Max(values);
        }

        public static float Min(params float[] values)
        {
            return Enumerable.Min(values);
        }

        public static double Max(params double[] values)
        {
            return Enumerable.Max(values);
        }

        public static double Min(params double[] values)
        {
            return Enumerable.Min(values);
        }

        /// <summary>
        /// Find the nearest integer power of 2 that is less than or equal to value
        /// </summary>
        /// <param name="value">Valid range 0 - Int32.MaxValue</param>
        /// <returns></returns>
        public static int FloorPowerOf2(double value)
        {
            if (value < 0 || value > Int32.MaxValue)
            {
                throw new Exception("Cannot floor negative numbers to power of 2");
            }
            uint v = (uint)value;
            if (v == 0)
            {
                return 0;
            }
            v |= (v >> 1);
            v |= (v >> 2);
            v |= (v >> 4);
            v |= (v >> 8);
            v |= (v >> 16);
            return (int)(v - (v >> 1));
        }

        /// <summary>
        /// Find the nearest integer power of 2 that is greather than or equal to value 
        /// </summary>
        /// <param name="value">Valid range 0 - (Int32.MaxValue/2)</param>
        /// <returns></returns>
        public static int CeilPowerOf2(double value)
        {
            if(value < 0 || value > Int32.MaxValue / 2)
            {
                throw new Exception("Value must be between 0 and Int32.MaxValue / 2");
            }
            uint v = (uint)Math.Ceiling(value);
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;
            return (int)v;
        }


        public static double RMS(double[] values)
        {
            if (values.Length == 0)
            {
                return 0;
            }
            return Math.Sqrt(values.Select(x => x * x).Sum() / values.Length);
        }

        public static double Average(double[] values)
        {
            if(values.Length == 0)
            {
                return 0;
            }
            return values.Sum() / values.Length;
        }

        /// <summary>
        /// Note that this is a sample (not population) variance and uses (N-1) and not N as the denominator 
        /// </summary>
        /// <param name="values"></param>
        /// <returns></returns>
        public static double SampleVariance(double[] values)
        {
            if (values.Length <= 1)
            {
                return 0;
            }
            var average = Average(values);
            return values.Select(v => (v - average) * (v - average)).Sum() / (values.Length - 1);                
        }

        /// <summary>
        /// Normalize an angle in radians to the range [0, 2 * PI)
        /// </summary>
        public static double NormalizeAngle(double radians)
        {
            while (radians < 0)
            {
                radians += 2 * Math.PI;
            }
            while (radians >= 2 * Math.PI)
            {
                radians -= 2 * Math.PI;
            }
            return Clamp(radians, 0, 2 * Math.PI); //clamp accounts for small numerical error
        }

        public static bool IsFinite(double v)
        {
            //for some reason double.IsFinite() won't compile
            //also I'm not sure if it checks for NaN
            return !double.IsNaN(v) && v > double.NegativeInfinity && v < double.PositiveInfinity;
        }
    }
}
