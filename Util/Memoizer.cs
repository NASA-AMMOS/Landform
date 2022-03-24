using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JPLOPS.Util
{
    public class Memoizer<InT, OutT>
    {
        public readonly Func<InT, OutT> Function;
        public ConcurrentDictionary<InT, OutT> Computed;

        public OutT this[InT arg]
        {
            get
            {
                if (!Computed.ContainsKey(arg))
                {
                    Computed[arg] = Function(arg);
                }
                return Computed[arg];
            }
        }

        public Memoizer(Func<InT, OutT> function)
        {
            Function = function;
            Computed = new ConcurrentDictionary<InT, OutT>();
        }

        public bool ContainsKey(InT key)
        {
            return Computed.ContainsKey(key);
        }
    }
}
