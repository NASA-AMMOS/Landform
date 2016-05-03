using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    /// <summary>
    /// Base class for Image Metadata
    /// </summary>
    public class ImageMetadata : ICloneable
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

        public object Clone()
        {
            return new ImageMetadata(Bands, Width, Height);
        }
    }
}
