using System;

namespace JPLOPS.Geometry
{
    public class MeshException : Exception
    {
        public MeshException() { }
        public MeshException(string message) : base(message) { }
        public MeshException(string message, Exception inner) : base(message, inner) { }
    }
}
