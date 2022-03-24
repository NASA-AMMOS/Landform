using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JPLOPS.Imaging
{
    public class ImageSerializationException : Exception
    {
        public ImageSerializationException() { }
        public ImageSerializationException(string message) : base(message) { }
        public ImageSerializationException(string message, Exception inner) : base(message, inner) { }
    }
}
