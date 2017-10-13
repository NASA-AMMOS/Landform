using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Geometry
{
    public class MeshException : Exception
    {
        public MeshException() { }
        public MeshException(string message) : base(message) { }
        public MeshException(string message, Exception inner) : base(message, inner) { }
    }
}
