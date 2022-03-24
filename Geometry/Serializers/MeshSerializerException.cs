using System;

namespace JPLOPS.Geometry
{
    public class MeshSerializerException : Exception
    {
        public MeshSerializerException() { }
        public MeshSerializerException(string message) : base(message) { }
        public MeshSerializerException(string message, Exception inner) : base(message, inner) { }
    }
}
