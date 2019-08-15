using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;

namespace OPS.Imaging
{
    /// <summary>
    /// This is the primary image class.  It stores data in a floating point format
    /// to enable generalized operations on a large variety of image types.
    /// 
    /// Common image operations should be implemented here
    /// 
    /// Normalized forms:
    /// RGB values are represented in normalized 0-1 form
    /// LAB values are represented in their own wierd range
    /// Position values are represented as XYZ coordinates
    /// Grayscale values are represented 0-1 and may optionally be replicated between bands
    /// 
    /// </summary>
    public class Image : GenericImage<float>
    {
        protected Image() { }

        /// <summary>
        /// Creates a new blank image with the specified resolution and bands
        /// </summary>
        /// <param name="bands"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public Image(int bands, int width, int height) : base(bands, width, height) { }

        public Image(ImageMetadata metadata) : base(metadata) { }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="that"></param>
        public Image(Image that) : base(that) { }

        /// <summary>
        /// Performs a deep copy of the image and all associated objects
        /// </summary>
        /// <returns></returns>
        public override object Clone()
        {
            return new Image(this);
        }

        public static string CheckSize(int bands, int width, int height)
        {
            return CheckSize<float>(bands, width, height);
        }

        public virtual Image Instantiate(int bands, int width, int height)
        {
            return new Image(bands, width, height);
        }

        public virtual BinaryImage InstantiateBinaryImage(int width, int height)
        {
            return new BinaryImage(width, height);
        }

        /// <summary>
        /// Load an image using default serializer and converter
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static Image Load(string filename)
        {
            string ext = Path.GetExtension(filename);
            ImageSerializer s = ImageSerializers.Instance.GetSerializer(ext);
            if (s == null)
            {
                throw new ImageSerializationException("Image format not supported");
            }
            return s.Read(filename);
        }

        /// <summary>
        /// Load an image using with the given converter
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static Image Load(string filename, IImageConverter converter)
        {
            string ext = Path.GetExtension(filename);
            ImageSerializer s = ImageSerializers.Instance.GetSerializer(ext);
            if (s == null)
            {
                throw new ImageSerializationException("Image format not supported");
            }
            return s.Read(filename, converter);
        }

        /// <summary>
        /// Loads a new image with the given serializer and converter
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="serializer"></param>
        /// <param name="converter"></param>
        public static Image Load(string filename, ImageSerializer serializer, IImageConverter converter)
        {
            return serializer.Read(filename, converter);
        }

        /// <summary>
        /// Saves image to disk using gdal and convert from normalzied values to value range
        /// </summary>
        /// <param name="filename"></param>
        public Image Save<T>(string filename)
        {
            string ext = Path.GetExtension(filename);
            ImageSerializer s = ImageSerializers.Instance.GetSerializer(ext);
            if (s == null)
            {
                throw new ImageSerializationException("Image format not supported");
            }
            s.Write<T>(filename, this);
            return this;
        }

        /// <summary>
        /// Saves image to disk with the provided converter
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="serializer"></param>
        /// <param name="converter"></param>
        public Image Save<T>(string filename, IImageConverter converter)
        {
            string ext = Path.GetExtension(filename);
            ImageSerializer s = ImageSerializers.Instance.GetSerializer(ext);
            if (s == null)
            {
                throw new ImageSerializationException("Image format not supported");
            }
            s.Write<T>(filename, this, converter);
            return this;
        }

        /// <summary>
        /// Saves image to disk
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="serializer"></param>
        /// <param name="converter"></param>
        public Image Save<T>(string filename, ImageSerializer serializer, IImageConverter converter)
        {
            serializer.Write<T>(filename, this, converter);
            return this;
        }

        /// <summary>
        /// Reflects an image vertically in place
        /// </summary>
        public Image FlipVertical()
        {
            int swapRow = this.Height - 1;
            for (int r = 0; r < swapRow; r++, swapRow--)
            {
                for (int b = 0; b < this.Bands; b++)
                {
                    for (int c = 0; c < this.Width; c++)
                    {
                        float curV = this[b, r, c];
                        this[b, r, c] = this[b, swapRow, c];
                        this[b, swapRow, c] = curV;
                    }
                }
            }
            return this;
        }

        /// <summary>
        /// Rotate an image 90 degrees clockwise
        /// </summary>
        /// <returns></returns>
        public Image Rotate90Clockwise()
        {
            Image result = Instantiate(Bands, Height, Width);
            for (int r = 0; r < Height; r++)
            {
                for (int c = 0; c < Width; c++)
                {
                    result.SetBandValues(c, Height - 1 - r, GetBandValues(r, c));
                }
            }
            return result;
        }

        /// <summary>
        /// Crop the source image to the specified dimensions.  Return a new image of the cropped area.
        /// This method does not retain metadata or camera model.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="startRow"></param>
        /// <param name="startCol"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public Image Crop(int startRow, int startCol, int newWidth, int newHeight)
        {
            Image result = Instantiate(Bands, newWidth, newHeight);
            if (HasMask)
            {
                result.CreateMask();
            }
            foreach (ImageCoordinate ic in result.Coordinates(true))
            {
                result[ic.Band, ic.Row, ic.Col] = this[ic.Band, ic.Row + startRow, ic.Col + startCol];
                if (HasMask)
                {
                    result.SetMaskValue(ic.Row, ic.Col, !IsValid(ic.Row + startRow, ic.Col + startCol));
                }
            }
            return result;
        }

        /// <summary>
        /// Crop this image to the smallest subframe that contains all valid pixels.
        /// Returns a new image of the cropped area.
        /// If there is no mask the return will just be a copy of this image.
        /// If there are no valid pixels the return will be a zero-size image.
        /// This method does not retain metadata or camera model.
        /// </summary>
        public Image Trim(out Vector2 upperLeftCorner)
        {
            int minValidRow = int.MaxValue;
            int maxValidRow = 0;
            int minValidCol = int.MaxValue;
            int maxValidCol = 0;
            foreach (ImageCoordinate ic in Coordinates(includeInvalidValues: false))
            {
                minValidRow = Math.Min(minValidRow, ic.Row);
                maxValidRow = Math.Max(maxValidRow, ic.Row);
                minValidCol = Math.Min(minValidCol, ic.Col);
                maxValidCol = Math.Max(maxValidCol, ic.Col);
            }
            upperLeftCorner.X = minValidCol;
            upperLeftCorner.Y = minValidRow;
            if (maxValidRow >= minValidRow && maxValidCol >= minValidCol)
            {
                return Crop(minValidRow, minValidCol, maxValidCol - minValidCol + 1, maxValidRow - minValidRow + 1);
            }
            else
            {
                var ret = Instantiate(Bands, 0, 0);
                if (HasMask)
                {
                    ret.CreateMask();
                }
                return ret;
            }
        }

        public Image Trim()
        {
            return Trim(out Vector2 upperLeftCorner);
        }

        /// <summary>
        /// decimate by averaging square blocks
        /// respects image mask, if any
        /// resulting image will have mask set for any source block that had no valid pixels
        /// does not mutate source image
        /// This method does not retain metadata or camera model.
        /// </summary>
        public Image Decimated(int blocksize)
        {
            if (blocksize == 1)
            {
                return (Image)Clone();
            }

            int targetWidth = Width / blocksize; //integer math
            int targetHeight = Height / blocksize; //integer math

            Image result = Instantiate(Bands, targetWidth, targetHeight);
            result.CreateMask();

            for (int band = 0; band < Bands; band++)
            {
                for (int dstRow = 0; dstRow < targetHeight; dstRow++)
                {
                    for (int dstCol = 0; dstCol < targetWidth; dstCol++)
                    {
                        int n = 0;
                        float sum = 0;
                        for (int srcRow = dstRow * blocksize; srcRow < (dstRow + 1) * blocksize; srcRow++)
                        {
                            if (srcRow >= 0 && srcRow < this.Height)
                            {
                                for (int srcCol = dstCol * blocksize; srcCol < (dstCol + 1) * blocksize; srcCol++)
                                {
                                    if (srcCol >= 0 && srcCol < this.Width)
                                    {
                                        if (IsValid(srcRow, srcCol))
                                        {
                                            sum += this[band, srcRow, srcCol];
                                            n++;
                                        }
                                    }
                                }
                            }
                        }
                        if (n > 0)
                        {
                            result[band, dstRow, dstCol] = sum / n;
                        }
                        else
                        {
                            result.SetMaskValue(dstRow, dstCol, true);
                        }
                    }
                }
            }
            return result;
        }

        /// blit another image or a subframe thereof onto this image in place  
        public Image Blit(Image srcImg, int dstCol, int dstRow, int srcCol = 0, int srcRow = 0,
                          int srcWidth = -1, int srcHeight = -1)
        {
            if (srcImg.Bands != Bands)
            {
                throw new ArgumentException("cannot blit images with different numbers of bands");
            }
            int nr = srcHeight >= 0 ? srcHeight : srcImg.Height;
            int nc = srcWidth >= 0 ? srcWidth : srcImg.Width;
            if (srcCol < 0 || srcRow < 0 || srcCol + nc > srcImg.Width || srcRow + nr > srcImg.Height)
            {
                throw new ArgumentException("source region out of bounds");
            }
            if (dstCol < 0 || dstRow < 0 || dstCol + nc > Width || dstRow + nr > Height)
            {
                throw new ArgumentException("target region out of bounds");
            }
            for (int band = 0; band < Bands; band++)
            {
                for (int r = 0; r < nr; r++)
                {
                    for (int c = 0; c < nc; c++)
                    {
                        this[band, dstRow + r, dstCol + c] = srcImg[band, srcRow + r, srcCol + c];
                    }
                }
            }
            return this;
        }
    }

    public class BinaryImage
    {
        protected bool[,] data;

        protected BinaryImage() { }

        public BinaryImage(int width, int height)
        {
            data = new bool[height, width];
        }

        public virtual bool this[int row, int column]
        {
            get
            {
                return data[row, column];
            }

            set
            {
                data[row, column] = value;
            }
        }
    }
}
