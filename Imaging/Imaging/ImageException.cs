using System;

namespace JPLOPS.Imaging
{

    public class ImageException : Exception
    {
        public ImageException() { }
        public ImageException(string message) : base(message) { }
        public ImageException(string message, Exception inner) : base(message, inner) { }
    }
}
