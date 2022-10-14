using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework;
using JPLOPS.Util;

namespace JPLOPS.Imaging
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

        private static readonly Vector2[] PixelCorners =
        {
            new Vector2(  0.0,  0.0), // upper left: 0
            new Vector2(  0.0,  1.0), // upper right: 1
            new Vector2(  1.0,  1.0), // lower right: 2
            new Vector2(  1.0,  0.0)  // lower left: 3
                
        };

        public static Vector2[] GetPixelCorners(Vector2 srcPixel)
        {
            //maps subpixel address to integer pixel address (upper left corner)
            Vector2 pixelAddress = new Vector2((int)srcPixel.X, (int)srcPixel.Y);

            Vector2[] corners = new Vector2[4];
            for (int idxCorner = 0; idxCorner < 4; idxCorner++)
            {
                corners[idxCorner] = pixelAddress + PixelCorners[idxCorner];
            }
            return corners;
        }

        private static readonly Vector2[] NeighborPixelsOffsets4Centered =
        {
            new Vector2( -1.0,  0.0),
            new Vector2(  0.0, -1.0),
            new Vector2(  0.0,  1.0),
            new Vector2(  1.0,  0.0)
        };

        public static List<Vector2> GetOffsetPixels(Vector2 srcPixel, double offset)
        {
            List<Vector2> result = new List<Vector2>();
            for (int idxNeighbor = 0; idxNeighbor < 4; idxNeighbor++)
            {
                result.Add(srcPixel + NeighborPixelsOffsets4Centered[idxNeighbor] * offset);
            }
            return result;
        }

        //float pixel addresses are the top left corners of pixel. if you intend to include
        // the full contents of a pixel in the area, be sure to pad by 1. For example, a 2x2 pixel grid is
        //
        //    0,0----1,0----2,0
        //     |      |      |
        //    0,1----1,1----2,1
        //     |      |      |
        //    0,2----1,2----2,2
        //
        // To find the triangle that covers half of that grid, pass (0,0) (0,2) (2,0)
        public static double CalculateTriPixelArea(Vector2[] pixels)
        {
            if (pixels.Length != 3)
            {
                throw new Exception("Need three pixels for area");
            }

            Vector2 pxA = pixels[0];
            Vector2 pxB = pixels[1];
            Vector2 pxC = pixels[2];
           
            //area when you know the three side lengths heron's formula
            //https://en.wikipedia.org/wiki/Heron%27s_formula
            double lenAB = (pxB - pxA).Length();
            double lenAC = (pxC - pxA).Length();
            double lenCB = (pxB - pxC).Length();
            double s = (lenAB + lenAC + lenCB)/2.0;
            return Math.Sqrt(s * (s - lenAB) * (s - lenAC) * (s - lenCB));
        }

        //float pixel addresses are the top left corners of pixel. if you intend to include
        // the full contents of a pixel in the area, be sure to pad by 1
        public static double CalculateQuadPixelArea(Vector2[] pixels)
        {
            if (pixels.Length != 4)
            {
                throw new Exception("Need four pixels for area");
            }

            Vector2 ab = (pixels[1] - pixels[0]);
            Vector2 ad = (pixels[3] - pixels[0]);
            Vector2 cb = (pixels[1] - pixels[2]);
            Vector2 cd = (pixels[3] - pixels[2]);

            double area1 = 0.5 * Vector3.Cross(new Vector3(ab, 0), new Vector3(ad, 0)).Length();
            double area2 = 0.5 * Vector3.Cross(new Vector3(cb, 0), new Vector3(cd, 0)).Length();
            return area1 + area2;
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
        /// Saves image to disk and convert from normalzied values to value range
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
        /// pixel at (row, col) in original goes to newRow=col, newCol=(Height - row - 1)
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

        public Image Crop(Subrect subrect)
        {
            return Crop(subrect.MinY, subrect.MinX, subrect.Width, subrect.Height);
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
        /// This method does not retain metadata.
        /// It only retains camera model for ConformalCameraModel which implements Decimated().
        /// </summary>
        public Image Decimated(int blocksize, bool average = true, Action<string> progress = null)
        {
            if (blocksize == 1)
            {
                return (Image)Clone();
            }

            int targetWidth = Width / blocksize; //integer math
            int targetHeight = Height / blocksize; //integer math

            Image result = Instantiate(Bands, targetWidth, targetHeight);
            if (HasMask)
            {
                result.CreateMask();
            }

            long total = (long)Bands * targetHeight * targetWidth;
            long current = 0;
            long lastSpew = 0, spewChunk = (long)(total / (100 * 10.0));
            int np = 0;

            for (int band = 0; band < Bands; band++)
            {
                //for (int dstRow = 0; dstRow < targetHeight; dstRow++)
                CoreLimitedParallel.For(0, targetHeight, dstRow =>
                {
                    Interlocked.Increment(ref np);
                    for (int dstCol = 0; dstCol < targetWidth; dstCol++)
                    {
                        int n = 0;
                        float sum = 0;
                        for (int srcRow = dstRow * blocksize; srcRow < (dstRow + 1) * blocksize; srcRow++)
                        {
                            if (srcRow >= 0 && srcRow < Height)
                            {
                                for (int srcCol = dstCol * blocksize; srcCol < (dstCol + 1) * blocksize; srcCol++)
                                {
                                    if (srcCol >= 0 && srcCol < Width)
                                    {
                                        if (!HasMask || IsValid(srcRow, srcCol))
                                        {
                                            sum += this[band, srcRow, srcCol];
                                            n++;
                                        }
                                    }
                                    if (!average && n > 0)
                                    {
                                        break;
                                    }
                                }
                            }
                            if (!average && n > 0)
                            {
                                break;
                            }
                        }
                        if (n > 0)
                        {
                            result[band, dstRow, dstCol] = sum / n;
                        }
                        else if (HasMask)
                        {
                            result.SetMaskValue(dstRow, dstCol, true);
                        }
                        long cur = Interlocked.Increment(ref current);
                        if (progress != null)
                        {
                            if (cur - Interlocked.Read(ref lastSpew) > spewChunk)
                            {
                                Interlocked.Exchange(ref lastSpew, cur);
                                double pct = 100 * (double)cur / total;
                                progress($"decimating {Width}x{Height} to {targetWidth}x{targetHeight}, " +
                                         $"processing {np} output rows in parallel, ({pct:F1}%)");
                            }
                        }
                    }
                    Interlocked.Decrement(ref np);
                });
            }

            if (CameraModel is ConformalCameraModel)
            {
                result.CameraModel = ((ConformalCameraModel)CameraModel).Decimated(blocksize);
            }

            return result;
        }

        // blit another image or a subframe thereof onto this image in place  
        // if srcImg has a different number of bands than this one then only the shared bands will be copied
        public Image Blit(Image srcImg, int dstCol, int dstRow, int srcCol = 0, int srcRow = 0,
                          int srcWidth = -1, int srcHeight = -1, bool unmask = false)
        {
            int minBands = Math.Min(Bands, srcImg.Bands);
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
            for (int band = 0; band < minBands; band++)
            {
                for (int r = 0; r < nr; r++)
                {
                    for (int c = 0; c < nc; c++)
                    {
                        this[band, dstRow + r, dstCol + c] = srcImg[band, srcRow + r, srcCol + c];
                        if (unmask && HasMask)
                        {
                            SetMaskValue(dstRow + r, dstCol + c, false);
                        }
                    }
                }
            }
            return this;
        }

        public Image Blit(Image srcImg,  int dstCol, int dstRow, Subrect srcSubrect)
        {
            return Blit(srcImg, dstCol, dstRow, srcSubrect.MinX, srcSubrect.MinY, srcSubrect.Width, srcSubrect.Height);
        }

        public class Subrect
        {
            public int MinX, MinY, MaxX, MaxY;

            public int Width { get { return MaxX - MinX + 1; } }
            public int Height { get { return MaxY - MinY + 1; } }

            public int Area { get { return Width * Height; } }

            public Vector2 Min { get { return new Vector2(MinX, MinY); } }
            public Vector2 Max { get { return new Vector2(MaxX, MaxY); } }

            public Vector2 Center { get { return 0.5 * (Min + Max); } }

            public bool Contains(Vector2 pixel, double eps = 0)
            {
                return Contains(pixel.X, pixel.Y, eps);
            }

            public bool Contains(double x, double y, double eps = 0)
            {
                return x >= (MinX - eps) && x <= (MaxX + eps) && y >= (MinY - eps) && y <= (MaxY + eps);
            }

            public bool ContainsProper(Vector2 pixel, double eps = 0)
            {
                return ContainsProper(pixel.X, pixel.Y, eps);
            }

            public bool ContainsProper(double x, double y, double eps = 0)
            {
                return x > (MinX + eps) && x < (MaxX - eps) && y > (MinY + eps) && y < (MaxY - eps);
            }

            public Vector2 Linterp(Vector2 at)
            {
                return Linterp(at.X, at.Y);
            }

            public Vector2 Linterp(double atX, double atY)
            {
                return new Vector2(MinX * (1 - atX) + MaxX * atX, MinY * (1 - atY) + MaxY * atY);
            }

            public Vector2 ReverseLinterp(double x, double y)
            {
                return new Vector2((x - MinX) / (MaxX - MinX), (y - MinY) / (MaxY - MinY));
            }

            public override string ToString()
            {
                return string.Format("MinX={0}, MaxX={1}, MinY={2}, MaxY={3}", MinX, MaxX, MinY, MaxY);
            }
        }

        /// <summary>
        /// If radius is non-positive then subrect covers whole image.
        /// </summary>
        public Subrect GetSubrect(Vector2 center, double radiusPixels)
        {
            return GetSubrect(center, new Vector2(radiusPixels, radiusPixels));
        }

        /// <summary>
        /// If either is non-positive then subrect covers whole image in that direction.
        /// </summary>
        public Subrect GetSubrect(Vector2 center, Vector2 halfExtentPixels)
        {
            var ret = new Subrect();

            if (halfExtentPixels.X > 0)
            {
                ret.MinX = (int)Math.Max(0, Math.Floor(center.X - halfExtentPixels.X));
                ret.MaxX = (int)Math.Min(Width - 1, Math.Ceiling(center.X + halfExtentPixels.X));
            }
            else
            {
                ret.MinX = 0;
                ret.MaxX = Width - 1;
            }

            if (halfExtentPixels.Y > 0)
            {
                ret.MinY = (int)Math.Max(0, Math.Floor(center.Y - halfExtentPixels.Y));
                ret.MaxY = (int)Math.Min(Height - 1, Math.Ceiling(center.Y + halfExtentPixels.Y));
            }
            else
            {
                ret.MinY = 0;
                ret.MaxY = Height - 1;
            }

            if (ret.MinX >= Width || ret.MinY >= Height || ret.MaxX < 0 || ret.MaxY < 0)
            {
                ret.MinX = ret.MinY = 0;
                ret.MaxX = ret.MaxY = -1;
            }
            return ret;
        }
    }
}
