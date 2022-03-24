using System;

namespace JPLOPS.Imaging
{
    /// <summary>
    /// Base class for Image Metadata
    /// </summary>
    public class ImageMetadata : ICloneable
    {
        public int Bands;
        public int Width;
        public int Height;

        protected ImageMetadata() { }

        public ImageMetadata(int b, int w, int h)
        {
            Bands = b;
            Width = w;
            Height = h;
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="that"></param>
        public ImageMetadata(ImageMetadata that)
        {
            this.Bands = that.Bands;
            this.Width = that.Width;
            this.Height = that.Height;
        }

        public virtual object Clone()
        {
            return new ImageMetadata(this);
        }
    }
}
