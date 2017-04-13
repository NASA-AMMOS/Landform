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

        public static void For(int startInclusive, int endExclusive, Action<int> action)
        {            
            for(int i = startInclusive; i < endExclusive; i++ )
            {
                action(i);
            }
        }
    }
}
