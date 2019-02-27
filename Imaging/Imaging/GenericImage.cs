using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

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
        /// <param name="initialValue">false means all pixels will be valid at the end of initilization</param>
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
        /// Removes the mask if there is one
        /// </summary>
        public void DeleteMask()
        {
            this.Mask = null;
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
        /// Mask any pixels in this image that are masked in other.
        /// Both images must be the same size.
        /// Adds mask to this image if it doesn't already have one.
        /// Any pixels that are already masked in this image will remain masked.
        /// </summary>
        public void UnionMask<TT>(GenericImage<TT> other)
        {
            if (Width != other.Width || Height != other.Height)
            {
                throw new ArgumentException("can only union mask with another image of same size");
            }
            if (!HasMask)
            {
                CreateMask(false);
            }
            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    if (other.IsInvalid(row, col))
                    {
                        SetMaskValue(row, col, true);
                    }
                }
            }
        }

        /// <summary>
        /// Mask any pixels in this image that correspond to pixels in other which match the passed band values.
        /// Both images must be the same size.
        /// Adds mask to this image if it doesn't already have one.
        /// Any pixels that are already masked in this image will remain masked.
        /// </summary>
        public void UnionMask<TT>(GenericImage<TT> other, TT[] perBandValues)
        {
            if (Width != other.Width || Height != other.Height)
            {
                throw new ArgumentException("can only union mask with another image of same size");
            }
            if (!HasMask)
            {
                CreateMask(false);
            }
            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    if (other.BandValuesEqual(row, col, perBandValues))
                    {
                        SetMaskValue(row, col, true);
                    }
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
        public bool IsInvalid(int row, int column)
        {
            return this.Mask != null && this.Mask[(row * Width) + column];
        }

        /// <summary>
        /// Returns true if the value at the given index should be masked out (ignored)
        /// If a mask is not defined for this image this will always return false
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public bool IsInvalid(int i)
        {
            return this.Mask != null && this.Mask[i];
        }

        /// <summary>
        /// Returns true if the value at row and column should be masked out (ignored)
        /// If a mask is not defined for this image this will always return false
        /// </summary>
        /// <param name="row"></param>
        /// <param name="column"></param>
        /// <returns></returns>
        public bool IsValid(int row, int column)
        {
            return this.Mask == null || !this.Mask[(row * Width) + column];
        }

        /// <summary>
        /// Returns true if the value at the given index should be masked out (ignored)
        /// If a mask is not defined for this image this will always return false
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public bool IsValid(int i)
        {
            return this.Mask == null || !this.Mask[i];
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
                if(IsInvalid(i))
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
                ApplyInPlace(b, f, applyToMaskedValues);
            }
        }

        /// <summary>
        /// Apply a function to all values in the specified band. 
        /// Result is written back to the array in place
        /// Ignores masked values by default
        /// </summary>
        /// <param name="band"></param>
        /// <param name="f"></param>
        /// <param name="applyToMaskedValues"></param>
        public void ApplyInPlace(int band, Func<T, T> f, bool applyToMaskedValues = false)
        {
            for (int i = 0; i < Data[band].Length; i++)
            {
                if (applyToMaskedValues || !IsInvalid(i))
                {
                    this.Data[band][i] = f(this.Data[band][i]);
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
                    if (!IsInvalid(i))
                    {
                        yield return this.Data[b][i];
                    }
                }
            }
        }

        /// <summary>
        /// Returns a coordinate for each pixel in the image and for each band
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ImageCoordinate> Coordinates(bool includeInvalidValues)
        {
            for (int b = 0; b < this.Bands; b++)
            {
                for (int r = 0; r < this.Height; r++)
                {
                    for (int c = 0; c < this.Width; c++)
                    {
                        if (includeInvalidValues || IsValid(r, c))
                        {
                            yield return new ImageCoordinate(b, r, c);
                        }
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
        public virtual T this[int band, int row, int column]
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

        /// <summary>
        /// Convert a pixel coordinate to a uv coordinate
        /// </summary>
        /// <param name="pixelCoordinate"></param>
        /// <returns></returns>
        public Vector2 PixelToUV(Vector2 pixelCoordinate)
        {
            return new Vector2(pixelCoordinate.X / Width, 1 - (pixelCoordinate.Y / Height));
        }

        /// <summary>
        /// Convert a uv coordinate to a pixel coordinate
        /// </summary>
        /// <param name="uvCoordinate"></param>
        /// <returns></returns>
        public Vector2 UVToPixel(Vector2 uvCoordinate)
        {
            return new Vector2(uvCoordinate.X * Width, (1 - uvCoordinate.Y) * Height);
        }

        /// <summary>
        /// Convert a bounding box in uv space to pixel space
        /// Ignores Z
        /// </summary>
        /// <param name="uvBounds"></param>
        /// <returns></returns>
        public BoundingBox UVToPixel(BoundingBox uvBounds)
        {
            BoundingBox pixelBounds = new BoundingBox();
            // Swap max and min because UV corrdintes flip the vertical component
            pixelBounds.Min = new Vector3(UVToPixel(new Vector2(uvBounds.Min.X, uvBounds.Max.Y)), 0);
            pixelBounds.Max = new Vector3(UVToPixel(new Vector2(uvBounds.Max.X, uvBounds.Min.Y)), 0);
            return pixelBounds;
        }

        /// <summary>
        /// Convert a bouding box in pixel space to uv space
        /// Ignore Z
        /// </summary>
        /// <param name="pixelBounds"></param>
        /// <returns></returns>
        public BoundingBox PixelToUv(BoundingBox pixelBounds)
        {
            BoundingBox uvBounds = new BoundingBox();
            // Swap max and min because UV corrdintes flip the vertical component
            uvBounds.Min = new Vector3(PixelToUV(new Vector2(pixelBounds.Min.X, pixelBounds.Max.Y)), 0);
            uvBounds.Max = new Vector3(PixelToUV(new Vector2(pixelBounds.Max.X, pixelBounds.Min.Y)), 0);
            return uvBounds;
        }
    }
}
