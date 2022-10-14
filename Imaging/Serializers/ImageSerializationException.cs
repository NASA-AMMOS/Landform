using System;

namespace JPLOPS.Imaging
{
    public class ImageSerializationException : Exception
    {
        public ImageSerializationException() { }
        public ImageSerializationException(string message) : base(message) { }
        public ImageSerializationException(string message, Exception inner) : base(message, inner) { }
    }
}
