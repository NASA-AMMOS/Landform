using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    /// <summary>
    /// A generic image container class that supports many different 
    /// basic types of gridded data including float, int, short, ushort, byte ect.
    /// In most cases this class does not need to be used directly but instead the float
    /// based Image class.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class GenericImage<T>
    {
        public T[][] Data;
        public int Bands;
        public int Width;
        public int Height;
        public bool[] Mask;

        protected GenericImage()
        {

        }

        /// <summary>
        /// Create a new, blank image.
        /// </summary>
        /// <param name="bands">Number of bands in the image.</param>
        /// <param name="width">Width of the image.</param>
        /// <param name="height">Height of the image.</param>
        public GenericImage(int bands, int width, int height)
        {
            Initalize(bands, width, height);
        }

        protected void Initalize(int bands, int width, int height)
        {
            this.Bands = bands;
            this.Width = width;
            this.Height = height;
            this.Data = new T[bands][];
            for (int c = 0; c < bands; c++)
            {
                Data[c] = new T[width * height];
            }
        }

        /// <summary>
        /// Convenience accessor for reading image data.  This is slower
        /// than directly accessing the data array with Data[b][row*Width + col]
        /// but is also less prone to error. 
        /// </summary>
        /// <param name="band">Channel index</param>
        /// <param name="row">Y index</param>
        /// <param name="column">X index</param>
        /// <returns></returns>
        public T this[int band, int row, int column]
        {
            get
            {              
                return this.Data[band][(row * Width) + column];
            }

            set
            {
                this.Data[band][(row * Width) + column] = value;
            }
        }

    }
}
