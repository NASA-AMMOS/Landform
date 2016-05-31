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
    public class GenericImage<T> : ICloneable
    {
        public ImageMetadata Metadata;
        public CameraModel CameraModel;

        public T[][] Data;        
        public int Bands;
        public int Width;
        public int Height;
        /// <summary>
        /// A mask value of true indicates that the value is masked out
        /// A mask value of false indicates that the value is valid
        /// A null mask means that this image does not have a mask
        /// </summary>
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

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="toCopy"></param>
        public GenericImage(GenericImage<T> that)
        {
            this.Initalize(that.Bands, that.Width, that.Height);
            for (int b = 0; b < that.Data.Length; b++)
            {
                Array.Copy(this.Data[b], that.Data[b], that.Data[b].Length);
            }
            if (that.Mask != null)
            {
                this.Mask = new bool[that.Mask.Length];
                Array.Copy(that.Mask, this.Mask, that.Mask.Length);
            }
            if (that.Metadata != null)
            {
                this.Metadata = (ImageMetadata)that.Metadata.Clone();
            }
            if (that.CameraModel != null)
            {
                this.CameraModel = (CameraModel)that.CameraModel.Clone();
            }
        }

        /// <summary>
        /// Performas a deep copy of the image
        /// </summary>
        /// <returns></returns>
        public object Clone()
        {
            return new GenericImage<T>(this);
        }

        protected void Initalize(int bands, int width, int height)
        {
            Metadata = new ImageMetadata(bands, width, height);
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
        /// Applys a function to every value in every band of the image
        /// The result is written back to the array in place
        /// </summary>
        /// <param name="f"></param>
        public void ApplyInPlace(Func<T, T> f)
        {
            for (int b = 0; b < Data.Length; b++)
            {
                for (int i = 0; i < Data[b].Length; i++)
                {
                    this.Data[b][i] = f(this.Data[b][i]);
                }
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
