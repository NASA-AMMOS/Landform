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
    public class PDSSeralizer : IImageSeralizer
    {
        public Image Read(string filename, IImageConverter converter)
        {
            throw new NotImplementedException();
        }

        public void Write<T>(string filename, Image image, IImageConverter converter)
        {
            throw new NotImplementedException();
        }
    }
}
