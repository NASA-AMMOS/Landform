using System;

namespace JPLOPS.Geometry
{
    public class MeshLabException : Exception
    {
        public MeshLabException() { }
        public MeshLabException(string message) : base(message) { }
        public MeshLabException(string message, Exception inner) : base(message, inner) { }
    }
}
