using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Util
{
    public class Serial
    {
        /// <summary>
        /// Like Parallel.ForEach but not multi-threaded.  Useful drop in replacement for when you want to test a parallel algorithm serially.
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

        public static void For(int fromInclusive, int toExclusive, Action<int> action)
        {            
            for(int i = fromInclusive; i < toExclusive; i++ )
            {
                action(i);
            }
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
            return maxParallelism < 0 ? GetAvailableCores() : maxParallelism;
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
            else
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
            Parallel.For(fromInclusive, toExclusive,
                         new ParallelOptions() { MaxDegreeOfParallelism = maxParallelism }, action);
        }

        //parallel for with thread local data
        public static void For<TLocal>(int fromInclusive, int toExclusive, Func<TLocal> localInit,
                                       Func<int,TLocal,TLocal> action, Action<TLocal> localFinally)
        {            
            Parallel.For(fromInclusive, toExclusive,
                         new ParallelOptions() { MaxDegreeOfParallelism = maxParallelism }, localInit,
                         (i, opts, local) => action(i, local), localFinally);
        }
    }
}
