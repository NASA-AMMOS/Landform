using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JPLOPS.Geometry
{
    public class MeshLabException : Exception
    {
        public MeshLabException() { }
        public MeshLabException(string message) : base(message) { }
        public MeshLabException(string message, Exception inner) : base(message, inner) { }
    }
}
