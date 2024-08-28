using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace JPLOPS.Util
{
    //https://stackoverflow.com/questions/13042045
    public class InterlockedExtensions
    {
        public static int Min(ref int target, int newValue)
        {
            return Compare(ref target, newValue, (nv, v) => nv < v);
        }

        public static int Max(ref int target, int newValue)
        {
            return Compare(ref target, newValue, (nv, v) => nv > v);
        }

        public static long Min(ref long target, long newValue)
        {
            return Compare(ref target, newValue, (nv, v) => nv < v);
        }

        public static long Max(ref long target, long newValue)
        {
            return Compare(ref target, newValue, (nv, v) => nv > v);
        }

        public static float Min(ref float target, float newValue)
        {
            return Compare(ref target, newValue, (nv, v) => nv < v);
        }

        public static float Max(ref float target, float newValue)
        {
            return Compare(ref target, newValue, (nv, v) => nv > v);
        }

        public static double Min(ref double target, double newValue)
        {
            return Compare(ref target, newValue, (nv, v) => nv < v);
        }

        public static double Max(ref double target, double newValue)
        {
            return Compare(ref target, newValue, (nv, v) => nv > v);
        }

        public static int Compare(ref int target, int newValue, Func<int, int, bool> func)
        {
            int val;
            bool newWins;
            do
            {
                val = target;
                newWins = func(newValue, val);
            } while (newWins && Interlocked.CompareExchange(ref target, newValue, val) != val);
            return newWins ? newValue : val;
        }

        public static long Compare(ref long target, long newValue, Func<long, long, bool> func)
        {
            long val;
            bool newWins;
            do
            {
                val = target;
                newWins = func(newValue, val);
            } while (newWins && Interlocked.CompareExchange(ref target, newValue, val) != val);
            return newWins ? newValue : val;
        }

        public static float Compare(ref float target, float newValue, Func<float, float, bool> func)
        {
            float val;
            bool newWins;
            do
            {
                val = target;
                newWins = func(newValue, val);
            } while (newWins && Interlocked.CompareExchange(ref target, newValue, val) != val);
            return newWins ? newValue : val;
        }

        public static double Compare(ref double target, double newValue, Func<double, double, bool> func)
        {
            double val;
            bool newWins;
            do
            {
                val = target;
                newWins = func(newValue, val);
            } while (newWins && Interlocked.CompareExchange(ref target, newValue, val) != val);
            return newWins ? newValue : val;
        }
    }

    public class Serial
    {
        /// <summary>
        /// Like Parallel.ForEach but not multi-threaded.  Useful drop in replacement for when you want to test a
        /// parallel algorithm serially.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        /// <param name="action"></param>
        public static void ForEach<T>(IEnumerable<T> list, Action<T> action)
        {
            foreach(T item in list)
            {
                action(item);
            }
        }

        public static void ForEach<T>(IEnumerable<T> list, Action<T, ParallelLoopState, long> action)
        {
            int i = 0;
            foreach (T item in list)
            {
                action(item, null, i++);
            }
        }


        public static void ForEach<T>(IEnumerable<T> list, ParallelOptions options, Action<T> action)
        {
            foreach (T item in list)
            {
                action(item);
            }
        }

        public static void ForEach<T,TLocal>(IEnumerable<T> list, Func<TLocal> localInit,
                                             Func<T,TLocal,TLocal> action, Action<TLocal> localFinally)
        {
            TLocal tls = localInit();
            foreach (T item in list)
            {
                tls = action(item, tls);
            }
            localFinally(tls);
        }

        public static void For(int fromInclusive, int toExclusive, Action<int> action)
        {            
            for (int i = fromInclusive; i < toExclusive; i++ )
            {
                action(i);
            }
        }

        public static void For<TLocal>(int fromInclusive, int toExclusive, Func<TLocal> localInit,
                                       Func<int,TLocal,TLocal> action, Action<TLocal> localFinally)
        {            
            TLocal tls = localInit();
            for (int i = fromInclusive; i < toExclusive; i++ )
            {
                tls = action(i, tls);
            }
            localFinally(tls);
        }
    }

    public class CoreLimitedParallel
    {
        //negative = use all available cores
        private static int maxParallelism = -1;

        public static int GetMaxDegreeOfParallelism()
        {
            return maxParallelism;
        }

        public static int GetAvailableCores()
        {
            return Environment.ProcessorCount;
        }

        public static int GetMaxCores()
        {
            return maxParallelism <= 0 ? GetAvailableCores() : maxParallelism;
        }

        //0 to use all available cores, N to use up to N, -M to reserve M
        public static void SetMaxCores(int maxCores)
        {
            if (maxCores == 0)
            {
                maxParallelism = GetAvailableCores();
            }
            else if (maxCores > 0)
            {
                maxParallelism = Math.Min(GetAvailableCores(), maxCores);
            }
            else
            {
                maxParallelism = Math.Max(GetAvailableCores() + maxCores, 1);
            }
        }

        public static void ForEach<T>(IEnumerable<T> list, Action<T> action)
        {
            Parallel.ForEach<T>(list, new ParallelOptions() { MaxDegreeOfParallelism = maxParallelism }, action);
        }

        public static void ForEachNoPartition<T>(IEnumerable<T> list, Action<T> action)
        {
            //https://stackoverflow.com/a/16427390
            Parallel.ForEach<T>(Partitioner.Create(list, EnumerablePartitionerOptions.NoBuffering),
                                new ParallelOptions() { MaxDegreeOfParallelism = maxParallelism },
                                action);
        }

        public static void ForEach<T>(IEnumerable<T> list, Action<T, ParallelLoopState, long> action)
        {
            Parallel.ForEach<T>(list, new ParallelOptions() { MaxDegreeOfParallelism = maxParallelism }, action);
        }

        public static void ForEach<T>(IEnumerable<T> list, ParallelOptions options, Action<T> action)
        {
            if (options.MaxDegreeOfParallelism < 0)
            {
                options.MaxDegreeOfParallelism = maxParallelism;
            }
            else if (maxParallelism > 0)
            {
                options.MaxDegreeOfParallelism = Math.Min(options.MaxDegreeOfParallelism, maxParallelism);
            }
            Parallel.ForEach<T>(list, options, action);
        }

        //parallel foreach with thread local data
        public static void ForEach<T,TLocal>(IEnumerable<T> list, Func<TLocal> localInit,
                                             Func<T,TLocal,TLocal> action, Action<TLocal> localFinally)
        {            
            Parallel.ForEach(list, new ParallelOptions() { MaxDegreeOfParallelism = maxParallelism }, localInit,
                             (i, opts, local) => action(i, local), localFinally);
        }

        public static void For(int fromInclusive, int toExclusive, Action<int> action)
        {            
            int i = fromInclusive - 1;
            Parallel.For(fromInclusive, toExclusive,
                         new ParallelOptions() { MaxDegreeOfParallelism = maxParallelism },
                         randomIndex => action(Interlocked.Increment(ref i)));
        }

        //parallel for with thread local data
        public static void For<TLocal>(int fromInclusive, int toExclusive, Func<TLocal> localInit,
                                       Func<int,TLocal,TLocal> action, Action<TLocal> localFinally)
        {            
            int i = fromInclusive - 1;
            Parallel.For(fromInclusive, toExclusive,
                         new ParallelOptions() { MaxDegreeOfParallelism = maxParallelism }, localInit,
                         (randomIndex, opts, local) => action(Interlocked.Increment(ref i), local), localFinally);
        }
    }
}
