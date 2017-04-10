using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OPS.MathExtensions
{
    public class MathE
    {

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
    }
}
