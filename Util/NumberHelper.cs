using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace OPS.Util
{
    public class NumberHelper
    {
        public static bool IsNumeric(object value)
        {
            return value is sbyte
                || value is byte
                || value is short
                || value is ushort
                || value is int
                || value is uint
                || value is long
                || value is ulong
                || value is float
                || value is double
                || value is decimal;
        }

        public static string NumberToString(object value)
        {
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static int? RandomSeed = null;

        public static Random MakeRandomGenerator()
        {
            if (RandomSeed.HasValue)
            {
                return new Random(RandomSeed.Value);
            }
            else
            {
                return new Random();
            }
        }

        public static bool IsPowerOfTwo(int value)
        {
            value = Math.Abs(value);
            return (value & (value - 1)) == 0;
        }
    }
}
