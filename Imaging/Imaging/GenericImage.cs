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
    public class GenericImage<T> : ICloneable, IEnumerable<T>
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
        /// It is recommended that you use the helper methods HasMask, GetMask, and SetMask
        /// </summary>
        protected bool[] Mask;

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
                Array.Copy(that.Data[b], this.Data[b], that.Data[b].Length);
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
        /// Creates a mask for this image and sets all pixels to the initial value specifed
        /// </summary>
        /// <param name="initialValue"></param>
        public void CreateMask(bool initialValue = false)
        {
            this.Mask = new bool[Width * Height];
            if(initialValue)
            {
                for(int i = 0; i < this.Mask.Length; i++)
                {
                    this.Mask[i] = initialValue;
                }
            }
        }

        /// <summary>
        /// Creates a mask and maskes out all pixels with the matching per-band values
        /// </summary>
        /// <param name="perBandValues"></param>
        public void CreateMask(T[] perBandValues)
        {
            this.Mask = new bool[Width * Height];
            for (int i = 0; i < Width * Height; i++)
            {
                if (BandValuesEqual(i, perBandValues))
                {
                    SetMaskValue(i, true);
                }
            }           
        }

        /// <summary>
        /// Returns true if this image has a mask
        /// </summary>
        public bool HasMask
        {
            get
            {
                return this.Mask != null;
            }
        }

        /// <summary>
        /// Returns true if the value at row and column should be masked out (ignored)
        /// If a mask is not defined for this image this will always return false
        /// </summary>
        /// <param name="row"></param>
        /// <param name="column"></param>
        /// <returns></returns>
        public bool IsMasked(int row, int column)
        {
            return this.Mask != null && this.Mask[(row * Width) + column];
        }

        /// <summary>
        /// Returns true if the value at the given index should be masked out (ignored)
        /// If a mask is not defined for this image this will always return false
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public bool IsMasked(int i)
        {
            return this.Mask != null && this.Mask[i];
        }

        /// <summary>
        /// Set the mask value for this row and column
        /// Create mask must have called on this image prior to setting values
        /// </summary>
        /// <param name="row"></param>
        /// <param name="column"></param>
        /// <param name="value"></param>
        public void SetMaskValue(int row, int column, bool value)
        {
            this.Mask[(row * Width) + column] = value;
        }

        /// <summary>
        /// Set the mask value for the value at this data index
        /// Create mask must have called on this image prior to setting values
        /// </summary>
        /// <param name="row"></param>
        /// <param name="column"></param>
        /// <param name="value"></param>
        public void SetMaskValue(int i, bool value)
        {
            this.Mask[i] = value;
        }

        /// <summary>
        /// Sets the per-band values fro all masked out pixels
        /// </summary>
        /// <param name="perBandValues"></param>
        public void SetValuesForMaskedData(T[] perBandValues)
        {
            for(int i = 0; i < Width*Height; i++)
            {
                if(IsMasked(i))
                {
                    SetBandValues(i, perBandValues);
                }
            }
        }

        /// <summary>
        /// Returns true if the values for each band for the given row and column match perBandValues
        /// perBandValues.length must be equal to Image.Bands
        /// </summary>
        /// <param name="row"></param>
        /// <param name="column"></param>
        /// <param name="perBandValues"></param>
        /// <returns></returns>
        public bool BandValuesEqual(int row, int column, T[] perBandValues)
        {
            for(int b = 0; b < this.Bands; b++)
            {
                if(!this[b,row,column].Equals(perBandValues[b]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Returns true if the values for each band for the given data index match perBandValues
        /// perBandValues.length must be equal to Image.Bands
        /// </summary>
        /// <param name="i"></param>
        /// <param name="perBandValues"></param>
        /// <returns></returns>
        public bool BandValuesEqual(int i, T[] perBandValues)
        {
            for (int b = 0; b < this.Bands; b++)
            {
                if (!this.Data[b][i].Equals(perBandValues[b]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Sets the values for each band for the given row and column.  
        /// perBandValues.length must be equal to Image.Bands
        /// </summary>
        /// <param name="row"></param>
        /// <param name="column"></param>
        /// <param name="perBandValues"></param>
        public void SetBandValues(int row, int column, T[] perBandValues)
        {
            for (int b = 0; b < this.Bands; b++)
            {
                this[b, row, column] = perBandValues[b];
            }
        }

        /// <summary>
        /// Sets the per band values for this data index.  
        /// perBandValues.length must be equal to Image.Bands
        /// </summary>
        /// <param name="i"></param>
        /// <param name="perBandValues"></param>
        public void SetBandValues(int i, T[] perBandValues)
        {
            for (int b = 0; b < this.Bands; b++)
            {
                this.Data[b][i] = perBandValues[b];
            }
        }

        /// <summary>
        /// Return the per band values for a pixel
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public T[] GetBandValues(int row, int col)
        {
            T[] result = new T[this.Bands];
            for (int b = 0; b < this.Bands; b++)
            {
                result[b] = this[b, row, col];
            }
            return result;
        }

        /// <summary>
        /// Return the per band values for a pixel
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public T[] GetBandValues(int i)
        {
            T[] result = new T[this.Bands];
            for(int b= 0; b < this.Bands; b++)
            {
                result[b] = this.Data[b][i];
            }
            return result;
        }

        /// <summary>
        /// Finds all pixels with currentBandValues and sets them to desiredPerBandValues 
        /// </summary>
        /// <param name="currentPerBandValues"></param>
        /// <param name="desiredPerBandValues"></param>
        public void ReplaceBandValues(T[] currentPerBandValues, T[] desiredPerBandValues)
        {
            for (int i = 0; i < Width*Height; i++)
            {
                if (BandValuesEqual(i, currentPerBandValues))
                {
                    SetBandValues(i, desiredPerBandValues);
                }            
            }
        }

        /// <summary>
        /// Applys a function to every value in every band of the image
        /// The result is written back to the array in place
        /// Ignores masked values by default
        /// </summary>
        /// <param name="f"></param>
        public void ApplyInPlace(Func<T, T> f, bool applyToMaskedValues = false)
        {
            for (int b = 0; b < Data.Length; b++)
            {
                for (int i = 0; i < Data[b].Length; i++)
                {
                    if (applyToMaskedValues || !IsMasked(i))
                    {
                        this.Data[b][i] = f(this.Data[b][i]);
                    }
                }
            }
        }

        /// <summary>
        /// Iterates over all non masked values in the image
        /// </summary>
        /// <param name="applyToMaskedValues"></param>
        /// <returns></returns>
        public IEnumerator<T> GetEnumerator()
        {
            for (int b = 0; b < this.Data.Length; b++)
            {
                for (int i = 0; i < this.Data[b].Length; i++)
                {
                    if (!IsMasked(i))
                    {
                        yield return this.Data[b][i];
                    }
                }
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
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
