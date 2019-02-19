using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace OPS.Util
{
    public class HashCombiner
    {
        //this is available stock in .NET Core 2.1+
        public static int Combine(object a, object b)
        {
            //https://stackoverflow.com/questions/1646807/quick-and-simple-hash-code-combinations
            int hash = 17;
            hash = hash * 31 + a.GetHashCode();
            hash = hash * 31 + b.GetHashCode();
            return hash;
        }
    }
}
