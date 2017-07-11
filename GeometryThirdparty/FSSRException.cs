using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    public class FSSRException : Exception
    {
        public FSSRException() { }
        public FSSRException(string message) : base(message) { }
        public FSSRException(string message, Exception inner) : base(message, inner) { }
    }
}
