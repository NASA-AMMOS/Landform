using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace OPS.Util
{
    //this is available in .NET Core 2.1+
    public class HashCombiner
    {
        public static int Combine(object a, object b)
        {
            return Combine(a.GetHashCode(), b.GetHashCode());
        }

        public static int Combine(int a, int b)
        {
            //https://stackoverflow.com/questions/1646807/quick-and-simple-hash-code-combinations
            int hash = 17;
            hash = hash * 31 + a;
            hash = hash * 31 + b;
            return hash;
        }
    }
}
