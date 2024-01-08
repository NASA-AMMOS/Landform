using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using JPLOPS.Util;

namespace JPLOPS.Imaging
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

        //TODO: (architecture) Metadata.{Bands, Width, Height} violates SSOT
        public int Bands { get; protected set; }
        public int Width { get; protected set; }
        public int Height { get; protected set; }

        public int Area
        {
            get
            {
                return Width * Height;
            }
        }

        private T[][] data;

        /// <summary>
        /// A mask value of true indicates that the value is masked out
        /// A mask value of false indicates that the value is valid
        /// A null mask means that this image does not have a mask
        /// </summary>
        private bool[] mask;
        private bool[] savedMask;

        public virtual bool HasMask
        {
            get
            {
                return mask != null;
            }
        }

        protected GenericImage() { }

        /// <summary>
        /// Create a new, blank image.
        /// </summary>
        /// <param name="bands">Number of bands in the image.</param>
        /// <param name="width">Width of the image.</param>
        /// <param name="height">Height of the image.</param>
        public GenericImage(int bands, int width, int height)
        {
            Initialize(bands, width, height);
            this.Metadata = new ImageMetadata(bands, width, height);
        }

        public GenericImage(ImageMetadata metadata)
        {
            Initialize(metadata.Bands, metadata.Width, metadata.Height);
            this.Metadata = metadata;
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="toCopy"></param>
        public GenericImage(GenericImage<T> that)
        {
            Initialize(that.Bands, that.Width, that.Height);

            if (that.Metadata != null)
            {
                Metadata = (ImageMetadata)that.Metadata.Clone();
            }

            if (that.CameraModel != null)
            {
                CameraModel = (CameraModel)that.CameraModel.Clone();
            }

            that.CopyDataTo(this);

            if (that.HasMask)
            {
                CreateMask();
                that.CopyMaskTo(this);
            }
        }

        protected virtual void CopyDataTo<TT>(GenericImage<TT> that)
        {
            if (!(typeof(TT).IsAssignableFrom(typeof(T))))
            {
                throw new ArgumentException("failed to copy image data: type mismatch");
            }

            if (that.Bands != Bands)
            {
                throw new ArgumentException("failed to copy image data: bands do not match");
            }

            for (int b = 0; b < Bands; b++)
            {
                if (that.data[b].Length != data[b].Length)
                {
                    throw new ArgumentException("failed to copy image data: band lengths do not match");
                }
                Array.Copy(data[b], that.data[b], data[b].Length);
            }
        }

        protected virtual void CopyMaskTo<TT>(GenericImage<TT> that)
        {
            if (!HasMask || !that.HasMask || that.mask.Length != mask.Length)
            {
                throw new ArgumentException("failed to copy image mask");
            }
            Array.Copy(mask, that.mask, mask.Length);
        }

        /// <summary>
        /// Performs a deep copy of the image
        /// </summary>
        /// <returns></returns>
        public virtual object Clone()
        {
            return new GenericImage<T>(this);
        }

        public static string CheckSize<TT>(int bands, int width, int height)
        {
            //reference for C# array limits when gcAllowVeryLargeObjects is enabled:
            //https://docs.microsoft.com/en-us/dotnet/framework/configure-apps/file-schema/runtime/gcallowverylargeobjects-element?view=netframework-4.8
            ulong elts = (ulong)width * (ulong)height;
            ulong maxElts = 0X7FEFFFFFul;
            if (elts > maxElts)
            {
                return string.Format("cannot create {0}x{1} image, {2} elements per band exceeds max array length {3}",
                                     width, height, elts, maxElts);
            }

            ulong bytes = elts * (ulong)Sizeof.GetManagedSize(typeof(T));
            ulong maxBytes = uint.MaxValue;
            if (bytes > maxBytes)
            {
                return string.Format("cannot create {0}x{1} {2} image, {3} bytes per band exceeds max array length {4}",
                                     width, height, typeof(T).Name, bytes, maxBytes);
            }

            return null;
        }

        private void Initialize(int bands, int width, int height)
        {
            string err = CheckSize<T>(bands, width, height);
            if (!string.IsNullOrEmpty(err))
            {
                throw new ArgumentException(err);
            }

            this.Bands = bands;
            this.Width = width;
            this.Height = height;

            this.data = new T[bands][];
            for (int c = 0; c < bands; c++)
            {
                data[c] = new T[width * height];
            }
        }

        /// <summary>
        /// Creates a mask for this image and sets all pixels to the initial value specifed
        /// </summary>
        /// <param name="initialValue">false means all pixels will be valid at the end of initilization</param>
        public virtual void CreateMask(bool initialValue = false)
        {
            mask = new bool[Width * Height];
            if (initialValue)
            {
                for (int i = 0; i < mask.Length; i++)
                {
                    mask[i] = initialValue;
                }
            }
        }

        public void FillMask(bool maskValue)
        {
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = maskValue;
            }
        }

        /// <summary>
        /// Creates a mask and maskes out all pixels with the matching per-band values
        /// </summary>
        /// <param name="perBandValues"></param>
        public void CreateMask(T[] perBandValues)
        {
            UnionMask(this, perBandValues);
        }

        /// <summary>
        /// Removes the mask if there is one
        /// </summary>
        public virtual void DeleteMask()
        {
            mask = savedMask = null;
        }

        public virtual void SaveMask()
        {
            if (!HasMask || savedMask != null)
            {
                throw new InvalidOperationException();
            }
            savedMask = (bool[])mask.Clone();
        }

        public virtual void RestoreMask()
        {
            if (savedMask == null)
            {
                throw new InvalidOperationException();
            }
            mask = savedMask;
            savedMask = null;
        }

        /// <summary>
        /// Creates a mask using the provided image.
        /// In the normal case: Zero valued pixels are valid, non-zero pixels are invalid.
        /// In the inverted case: Zero value pixels are invalid, non-zero are valid
        /// If the provided image has more than one band, the first band will be used
        /// </summary>
        public void SetMask(Image mask, bool inverted = false)
        {
            if (Width != mask.Width || Height != mask.Height)
            {
                throw new ImageException("mask resolution must match image resolution");
            }
            if (!HasMask)
            {
                CreateMask();
            }
            bool valueForZero = inverted ? true : false;
            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    SetMaskValue(row, col, mask.GetBandValues(row, col)[0] == 0 ? valueForZero : !valueForZero);
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
                CreateMask();
            }
            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    if (!other.IsValid(row, col))
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
                CreateMask();
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
        /// Returns true if the value at row and column should be masked out (ignored)
        /// If a mask is not defined for this image this will always return false
        /// </summary>
        /// <param name="row"></param>
        /// <param name="column"></param>
        /// <returns></returns>
        public virtual bool IsValid(int row, int column)
        {
            return IsValid((row * Width) + column);
        }

        public int CountValid()
        {
            if (!HasMask)
            {
                return Width * Height;
            }
            int numValid = 0;
            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    if (IsValid(row, col))
                    {
                        numValid++;
                    }
                }
            }
            return numValid;
        }

        /// <summary>
        /// Returns true if the value at the given index should be masked out (ignored)
        /// If a mask is not defined for this image this will always return false
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public virtual bool IsValid(int i)
        {
            return mask == null || !mask[i];
        }

        /// <summary>
        /// Set the mask value for this row and column
        /// Create mask must have called on this image prior to setting values
        /// </summary>
        /// <param name="row"></param>
        /// <param name="column"></param>
        /// <param name="value"></param>
        public virtual void SetMaskValue(int row, int column, bool value)
        {
            mask[(row * Width) + column] = value;
        }

        public virtual void SetMaskValue(int i, bool value)
        {
            mask[i] = value;
        }

        /// <summary>
        /// Sets the per-band values fro all masked out pixels
        /// </summary>
        /// <param name="perBandValues"></param>
        public void SetValuesForMaskedData(T[] perBandValues)
        {
            for (int r = 0; r < Height; r++)
            {
                for (int c = 0; c < Width; c++)
                {
                    if (!IsValid(r, c))
                    {
                        SetBandValues(r, c, perBandValues);
                    }
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
            for (int b = 0; b < Bands; b++)
            {
                if (!this[b, row, column].Equals(perBandValues[b]))
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
            for (int b = 0; b < Bands; b++)
            {
                this[b, row, column] = perBandValues[b];
            }
        }

        /// <summary>
        /// Return the per band values for a pixel
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public T[] GetBandValues(int row, int col)
        {
            T[] result = new T[Bands];
            for (int b = 0; b < Bands; b++)
            {
                result[b] = this[b, row, col];
            }
            return result;
        }

        /// <summary>
        /// Returns true if the values for each band for the given data index match perBandValues
        /// perBandValues.length must be equal to Image.Bands
        /// </summary>
        /// <param name="i"></param>
        /// <param name="perBandValues"></param>
        /// <returns></returns>
        public virtual bool BandValuesEqual(int i, T[] perBandValues)
        {
            for (int b = 0; b < Bands; b++)
            {
                if (!data[b][i].Equals(perBandValues[b]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Sets the per band values for this data index.  
        /// perBandValues.length must be equal to Image.Bands
        /// </summary>
        /// <param name="i"></param>
        /// <param name="perBandValues"></param>
        public virtual void SetBandValues(int i, T[] perBandValues)
        {
            for (int b = 0; b < Bands; b++)
            {
                data[b][i] = perBandValues[b];
            }
        }

        /// <summary>
        /// Return the per band values for a pixel
        /// </summary>
        /// <param name="i"></param>
        /// <returns></returns>
        public virtual T[] GetBandValues(int i)
        {
            T[] result = new T[Bands];
            for (int b = 0; b < Bands; b++)
            {
                result[b] = data[b][i];
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
            for (int i = 0; i < Width * Height; i++)
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
            for (int b = 0; b < Bands; b++)
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
        public virtual void ApplyInPlace(int band, Func<T, T> f, bool applyToMaskedValues = false)
        {
            for (int i = 0; i < data[band].Length; i++)
            {
                if (applyToMaskedValues || IsValid(i))
                {
                    data[band][i] = f(data[band][i]);
                }
            }
        }

        public void Fill(T[] bandValues, bool applyToMaskedValues = false)
        {
            for (int b = 0; b < Bands; b++)
            {
                for (int i = 0; i < data[b].Length; i++)
                {
                    if (applyToMaskedValues || IsValid(i))
                    {
                        data[b][i] = bandValues[b];
                    }
                }
            }
        }

        /// <summary>
        /// Iterates over all values across all bands in the image
        /// </summary>
        /// <returns></returns>
        public virtual IEnumerator<T> GetEnumerator(bool includeInvalidValues)
        {
            for (int b = 0; b < Bands; b++)
            {
                for (int i = 0; i < data[b].Length; i++)
                {
                    if (includeInvalidValues || IsValid(i))
                    {
                        yield return data[b][i];
                    }
                }
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            return GetEnumerator(false);
        }

        /// <summary>
        /// Returns a coordinate for each pixel in the image and for each band
        /// </summary>
        /// <returns></returns>
        public IEnumerable<ImageCoordinate> Coordinates(bool includeInvalidValues = false)
        {
            for (int b = 0; b < Bands; b++)
            {
                for (int r = 0; r < Height; r++)
                {
                    for (int c = 0; c < Width; c++)
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
            return GetEnumerator();
        }

        public virtual T[] GetBandData(int band)
        {
            return data[band];
        }

        public virtual int AddBand()
        {
            T[][] origData = data;

            Bands += 1;
            if (Metadata != null)
            {
                Metadata.Bands = Bands;
            }

            data = new T[Bands][];

            for (int b = 0; b < Bands - 1; b++)
            {
                data[b] = origData[b];
            }

            data[Bands - 1] = new T[Width * Height];

            return Bands - 1;
        }

        public virtual void RemoveBand(int dead)
        {
            T[][] origData = data;

            Bands -= 1;
            if (Metadata != null)
            {
                Metadata.Bands = Bands;
            }

            data = new T[Bands][];

            for (int b = 0; b < Bands + 1; b++)
            {
                if (b < dead)
                {
                    data[b] = origData[b];
                }
                else if (b > dead)
                {
                    data[b - 1] = origData[b];
                }
            }
        }

        /// <summary>
        /// Convenience accessor for reading image data.  This is slower
        /// than directly accessing the data array with data[b][row*Width + col]
        /// but is also less prone to error. 
        ///
        /// Also, this is correctly overridden by SparseImage.cs, whereas directly accessing data there does not work.
        ///
        /// </summary>
        /// <param name="band">Channel index</param>
        /// <param name="row">Y index</param>
        /// <param name="column">X index</param>
        /// <returns></returns>
        public virtual T this[int band, int row, int column]
        {
            get
            {
                return data[band][(row * Width) + column];
            }

            set
            {
                data[band][(row * Width) + column] = value;
            }
        }

        /// <summary>
        /// Convert a pixel coordinate to a uv coordinate
        /// </summary>
        /// <param name="pixelCoordinate"></param>
        /// <returns></returns>
        public Vector2 PixelToUV(Vector2 pixelCoordinate)
        {
            //pixel origin is top left of image, uv origin is lower left (opengl), requires a y flip
            return new Vector2(pixelCoordinate.X / Width, 1 - (pixelCoordinate.Y / Height));
        }

        /// <summary>
        /// Convert a pixel coordinate to a uv coordinate
        /// </summary>
        static public Vector2 PixelToUV(Vector2 pixelCoordinate, int widthPixels, int heightPixels)
        {
            //pixel origin is top left of image, uv origin is lower left (opengl), requires a y flip
            return new Vector2(pixelCoordinate.X / widthPixels, 1 - (pixelCoordinate.Y / heightPixels));
        }

        /// <summary>
        /// Converts a pixel from its addressing scheme (upper left corner) to it sampling point (pixel center)
        /// </summary>
        static private Vector2 halfVec2 = Vector2.One * 0.5;
        static public Vector2 ApplyHalfPixelOffset(int row, int col)
        {
            Vector2 pixelUpperLeftCorner = new Vector2(col, row);
            return pixelUpperLeftCorner + halfVec2;
        }

        /// <summary>
        /// Pixels are addressed by their upper left corner in landform, when sampling texture data like a renderer would
        /// you want to read from the center of the pixel (by incrementing a half pixel). The vertical direction is reversed
        /// because pixel origin is the top left of the image, and uv origin is lower left
        /// </summary>
        static public Vector2 ApplyHalfPixelOffsetToUV(Vector2 pixelUpperLeftUV, int widthPixels, int heightPixels)
        {
            return pixelUpperLeftUV + new Vector2(0.5 / widthPixels, -0.5 / heightPixels);
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
        /// Convert a uv coordinate to a pixel coordinate
        /// </summary>
        static public Vector2 UVToPixel(Vector2 uvCoordinate, int widthPixels, int heightPixels)
        {          
            return new Vector2(uvCoordinate.X * widthPixels, (1 - uvCoordinate.Y) * heightPixels);
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
        public BoundingBox PixelToUV(BoundingBox pixelBounds)
        {
            BoundingBox uvBounds = new BoundingBox();
            // Swap max and min because UV corrdintes flip the vertical component
            uvBounds.Min = new Vector3(PixelToUV(new Vector2(pixelBounds.Min.X, pixelBounds.Max.Y)), 0);
            uvBounds.Max = new Vector3(PixelToUV(new Vector2(pixelBounds.Max.X, pixelBounds.Min.Y)), 0);
            return uvBounds;
        }
    }
}
