using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    /// <summary>
    /// Used to determine structure of a file without opening it
    /// </summary>
    struct ImageInfo
    {
        public int Bands;
        public int Width;
        public int Height;

        public ImageInfo(int b, int w, int h)
        {
            Bands = b;
            Width = w;
            Height = h;
        }
    }
}
