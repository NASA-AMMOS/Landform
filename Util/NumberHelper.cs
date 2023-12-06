using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace JPLOPS.Util
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

        //thread safe update of location to newValue only if comparison > location
        //https://stackoverflow.com/a/13056904
        public static bool InterlockedExchangeIfGreaterThan(ref int location, int comparison, int newValue)
        {
            int initialValue;
            do
            {
                initialValue = location;
                if (initialValue >= comparison) return false;
            }
            while (Interlocked.CompareExchange(ref location, newValue, initialValue) != initialValue);
            return true;
        }

        //thread safe update of location to newValue only if newValue > location
        public static bool InterlockedExchangeIfGreaterThan(ref int location, int newValue)
        {
            return InterlockedExchangeIfGreaterThan(ref location, newValue, newValue);
        }

        //Fisher-Yates shuffle
        public static List<T> Shuffle<T>(List<T> list, Random rng = null)
        {
            rng = rng ?? MakeRandomGenerator();
            for (int i = 0; i < list.Count - 1; i++)
            {
                int j = rng.Next(i, list.Count);
                T t = list[i];
                list[i] = list[j];
                list[j] = t;
            }
            return list;
        }

        public static T[] Shuffle<T>(T[] arr, Random rng = null)
        {
            rng = rng ?? MakeRandomGenerator();
            for (int i = 0; i < arr.Length - 1; i++)
            {
                int j = rng.Next(i, arr.Length);
                T t = arr[i];
                arr[i] = arr[j];
                arr[j] = t;
            }
            return arr;
        }
    }
}
