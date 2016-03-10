using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging.Imaging
{
    /// <summary>
    /// Base class for Image Metadata
    /// </summary>
    public class ImageMetadata
    {
        public int Bands;
        public int Width;
        public int Height;

        public ImageMetadata(int b, int w, int h)
        {
            Bands = b;
            Width = w;
            Height = h;
        }
    }
}
