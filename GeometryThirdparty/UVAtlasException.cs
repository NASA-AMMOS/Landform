using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    public class UVAtlasException : Exception
    {
        public UVAtlasException() { }
        public UVAtlasException(string message) : base(message) { }
        public UVAtlasException(string message, Exception inner) : base(message, inner) { }
    }
}
