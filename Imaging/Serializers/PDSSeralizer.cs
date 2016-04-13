using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    /// <summary>
    /// Reads PDSImages.  
    /// </summary>
    public class PDSSeralizer<T> : IImageSeralizer<T>
    {
        public GenericImage<T> Read(string filename)
        {
            throw new NotImplementedException();
        }

        public void Write(string filename, GenericImage<T> image)
        {
            throw new NotImplementedException();
        }
    }
}
