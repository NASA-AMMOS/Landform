using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Cloud
{
    public class CloudException : Exception
    {
        public CloudException() { }
        public CloudException(string message) : base(message) { }
        public CloudException(string message, Exception inner) : base(message, inner) { }
    }
}
