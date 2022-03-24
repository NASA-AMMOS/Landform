using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JPLOPS.Imaging
{
    
    public class ImageException : Exception
    {
        public ImageException() { }
        public ImageException(string message) : base(message) { }
        public ImageException(string message, Exception inner) : base(message, inner) { }
    }
}
