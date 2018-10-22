using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    public class CloudException : Exception
    {
        public CloudException(string msg) : base(msg) { }
        public CloudException(string msg, Exception inner) : base(msg, inner) { }
    }
}
