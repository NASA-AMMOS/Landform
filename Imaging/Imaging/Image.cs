using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Microsoft.Xna.Framework;
using OPS.MathExtensions;

namespace OPS.Imaging
{
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
        /// Linearly scale values in a band in place
        /// </summary>
        /// <param name="band">band to scale</param>
        /// <param name="beforeMin">any pixles currently at this value will be mapped to afterMin</param>
        /// <param name="beforeMax">any pixels currently at this value will be mapped to afterMax</param>
        /// <param name="afterMin">the new min value for this band</param>
        /// <param name="afterMax">the new max value for this band</param>
        public Image ScaleValues(int band, float beforeMin, float beforeMax, float afterMin, float afterMax)
        {
            if (beforeMax == beforeMin)
            {
                throw new Exception("Cannot ScaleValues when beforeMin and beforeMax are the same");
            }
            float beforeRange = beforeMax - beforeMin;
            float afterRange = afterMax - afterMin;
            ApplyInPlace(band, x =>
            {
                float amount = (x - beforeMin) / beforeRange;
                float result = MathE.Clamp(afterMin + afterRange * amount, afterMin, afterMax);
                return result;
            });
            return this;
        }

        public void ScaleValues(float scalar, bool applyToMaskedValues = true)
        {
            ApplyInPlace(v =>
            {
                return v * scalar;
            },
            applyToMaskedValues);           
        }

        /// <summary>
        /// Linearly scales values in the image from [beforeMin, beforeMax] to [afterMin, afterMax] in place
        /// Scaling is applied uniformly to all bands of the image.
        /// Result values are clamped to afterMin and afterMax in the case that input values are outside
        /// beforeMin and beforeMax
        /// 
        /// For example, you might do the following to convert RGB values from 16-bit to normalzied 0-1 form
        /// ScaleValues(0, ushort.MaxValue, 0, 1);
        /// </summary>
        /// <param name="beforeMin">min value in original imge</param>
        /// <param name="beforeMax">max value in original image</param>
        /// <param name="afterMin">min value in result image</param>
        /// <param name="afterMax">max value in result image</param>
        public Image ScaleValues(float beforeMin, float beforeMax, float afterMin, float afterMax)
        {
            for (int b = 0; b < this.Bands; b++)
            {
                ScaleValues(b, beforeMin, beforeMax, afterMin, afterMax);
            }
            return this;
        }

        /// <summary>
        /// Given an image with a mask, extend the image and the mask by border pixels in place
        /// If border is negative (the default) continue inpainting until there are no
        /// masked pixels left.  Inpainted pixels are an average of their non-masked neighbors
        /// </summary>
        /// <param name="border"></param>
        /// <param name="preserveMask">inpainting usually destroys the mask where pixels were inpainted, setting to true will preserve the original mask</param>
        public Image Inpaint(int border = -1,bool preserveMask = false)
        {
            if (HasMask && preserveMask)
            {
                SaveMask();
            }

            Inpainter.Apply(this, border);

            if (HasMask && preserveMask)
            {
                RestoreMask();
            }

            return this;
        }

        /// <summary>
        /// Stretch the color channles of an image based the standard deviation of its values in place
        /// The resulting image will have its values normalzied between 0 and 1
        /// NOTE that bands with no variance (ie all the same value) will not be scaled and could be outside the 0-1 range
        /// NOTE masked values are also not scaled and could remain outside the 0-1 range
        /// </summary>
        /// <param name="nStdev">Number of standard deviations from the mean to place the upper and lower values of the stretch</param>
        public Image ApplyStdDevStretch(double nStdev = 3)
        {
            ImageStatistics stats = new ImageStatistics(this);
            for (int b = 0; b < this.Bands; b++)
            {
                // Cannot apply streatch with 1 or fewer values
                if (stats.Average(b).Count <= 1)
                {
                    continue;
                }
                double stdev = stats.Average(b).StandardDeviation;
                double mean = stats.Average(b).Mean;

                double min = Math.Max(mean - stdev * nStdev, stats.Average(b).Min);
                double max = Math.Min(mean + stdev * nStdev, stats.Average(b).Max);
                // Scaling values is invalid if min and max are the same
                if (min != max)
                {
                    ScaleValues(b, (float)min, (float)max, 0, 1);
                }
            }
            return this;
        }

        /// <summary>
        /// Normalize the color channels of this image to 0-1
        /// NOTE that bands with no variance (ie all the same value) will not be scaled and could be outside the 0-1 range
        /// NOTE masked values are also not scaled and could remain outside the 0-1 range
        /// </summary>
        public Image Normalize()
        {
            ImageStatistics stats = new ImageStatistics(this);
            for (int b = 0; b < this.Bands; b++)
            {
                if (stats.Average(b).Count <= 1)
                {
                    continue;
                }
                double min = stats.Average(b).Min;
                double max = stats.Average(b).Max;
                // Scaling values is invalid if min and max are the same
                if (min != max)
                {
                    ScaleValues(b, (float)min, (float)max, 0, 1);
                }
            }
            return this;
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

        public delegate void InvalidBlock(int blockRow, int blockCol, double validRatio);

        /// <summary>
        /// Count the number of valid (i.e. un-masked) pixels in each blocksize x blocksize chunk of this image.
        /// For each chunk where the ratio of the number of valid pixels to total in block is less than minValidRatio,
        /// invalidate (i.e. mask) all pixels in the block.
        /// Operates on the image in-place.
        /// If callback is provided then it is called for each invalid block instead of actually invalidating the block.
        /// </summary>
        public Image InvalidateSparseBlocks(int blocksize, double minValidRatio, InvalidBlock callback = null)
        {
            if (HasMask)
            {
                int hBlocks = (int)Math.Ceiling(((double)Width) / blocksize);
                int vBlocks = (int)Math.Ceiling(((double)Height) / blocksize);
                for (int vBlock = 0; vBlock < vBlocks; vBlock++)
                {
                    int maxR = Math.Min(Height, (vBlock + 1) * blocksize);
                    for (int hBlock = 0; hBlock < hBlocks; hBlock++)
                    {
                        int maxC = Math.Min(Width, (hBlock + 1) * blocksize);
                        int numValid = 0, numTotal = 0;
                        for (int r = vBlock * blocksize; r < maxR; r++)
                        {
                            for (int c = hBlock * blocksize; c < maxC; c++)
                            {
                                numTotal++;
                                if (IsValid(r, c))
                                {
                                    numValid++;
                                }
                            }
                        }

                        if (numTotal > 0)
                        {
                            double ratio = ((double)numValid) / numTotal;
                            if (ratio < minValidRatio)
                            {
                                if (callback != null)
                                {
                                    callback(vBlock, hBlock, ratio);
                                }
                                else
                                {
                                    for (int r = vBlock * blocksize; r < maxR; r++)
                                    {
                                        for (int c = hBlock * blocksize; c < maxC; c++)
                                        {
                                            SetMaskValue(r, c, true);
                                        }
                                    }
                                }
                            }
                        }
                    } //for each block in row
                } //for each row of blocks
            } //has mask
            return this;
        }

        /// <summary>
        /// invalidate sparse blocks that are not fully surrounded by valid blocks
        /// </summary>
        public Image InvalidateSparseExternalBlocks(int blocksize, double minValidRatio)
        {
            if (!HasMask)
            {
                return this;
            }

            blocksize = Math.Max(blocksize, 1);

            int hBlocks = (int)Math.Ceiling(((double)Width) / blocksize);
            int vBlocks = (int)Math.Ceiling(((double)Height) / blocksize);

            //marked[row, col] = false means block is not invalid or has already been invalidated
            var marked = new bool[vBlocks, hBlocks];
            var seeds = new Queue<Pixel>(); //invalid border blocks

            //mark all invalid blocks and collect seeds
            InvalidateSparseBlocks(blocksize, minValidRatio,
                                   (row, col, ratio) => {
                                       marked[row, col] = true;
                                       if (row == 0 || row == vBlocks - 1 || col == 0 || col == hBlocks - 1)
                                       {
                                           seeds.Enqueue(new Pixel(row, col));
                                       }
                                   });

            var offsets = new Pixel[] { new Pixel(-1, 0), new Pixel(1, 0), new Pixel(0, -1), new Pixel(0, 1) };

            //DFS from each seed to invalidate blocks reachable from an invalid block on the image border
            while (seeds.Count > 0)
            {
                var seed = seeds.Dequeue();
                if (marked[seed.Row, seed.Col])
                {
                    marked[seed.Row, seed.Col] = false;
                    int maxR = Math.Min(Height, (seed.Row + 1) * blocksize);
                    int maxC = Math.Min(Width, (seed.Col + 1) * blocksize);
                    for (int r = seed.Row * blocksize; r < maxR; r++)
                    {
                        for (int c = seed.Col * blocksize; c < maxC; c++)
                        {
                            SetMaskValue(r, c, true);
                        }
                    }
                    foreach (var offset in offsets)
                    {
                        var n = seed + offset;
                        if (n.Row >= 0 && n.Row < vBlocks && n.Col >= 0 && n.Col < hBlocks && marked[n.Row, n.Col])
                        {
                            seeds.Enqueue(n);
                        }
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// invalidate all but the largest blob of valid (i.e. un-masked) pixels
        /// operates on the image in-place
        /// </summary>
        public Image InvalidateAllButLargestValidBlob(out int largestBlobSize)
        {
            if (!HasMask)
            {
                largestBlobSize = Width * Height;
                return this;
            }

            var marked = InstantiateBinaryImage(Width, Height);

            var seeds = new Queue<Pixel>();
            var offsets = new Pixel[] { new Pixel(-1, 0), new Pixel(1, 0), new Pixel(0, -1), new Pixel(0, 1) };
            int markBlob(Pixel seed)
            {
                int size = 0;
                seeds.Enqueue(seed);
                while (seeds.Count > 0)
                {
                    var px = seeds.Dequeue();
                    if (!marked[px.Row, px.Col])
                    {
                        size++;
                        marked[px.Row, px.Col] = true;
                        foreach (var offset in offsets)
                        {
                            var n = px + offset;
                            if (n.Row >= 0 && n.Row < Height && n.Col >= 0 && n.Col < Width && !marked[n.Row, n.Col] &&
                                IsValid(n.Row, n.Col))
                            {
                                seeds.Enqueue(n);
                            }
                        }
                    }
                }
                return size;
            }

            largestBlobSize = 0;
            Pixel seedOfLargestBlob = new Pixel();
            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    if (IsValid(row, col) && !marked[row, col])
                    {
                        var seed = new Pixel(row, col);
                        int size = markBlob(seed);
                        if (size > largestBlobSize)
                        {
                            largestBlobSize = size;
                            seedOfLargestBlob = seed;
                        }
                    }
                }
            }

            if (largestBlobSize > 0)
            {
                for (int row = 0; row < Height; row++)
                {
                    for (int col = 0; col < Width; col++)
                    {
                        marked[row, col] = false;
                    }
                }
                markBlob(seedOfLargestBlob);
                for (int row = 0; row < Height; row++)
                {
                    for (int col = 0; col < Width; col++)
                    {
                        if (!marked[row, col])
                        {
                            SetMaskValue(row, col, true);
                        }
                    }
                }
            }
            return this;
        }

        public Image InvalidateAllButLargestValidBlob()
        {
            return InvalidateAllButLargestValidBlob(out int largestBlobSize);
        }

        /// <summary>
        /// Simulate a Gaussian blur with a series of box blurs in place
        /// </summary>
        /// <param name="r"></param>
        public Image GaussianBoxBlur(int r)
        {
            Blur.GaussianBoxBlur(this, r);
            return this;
        }

        public float BilinearSample(int band, float row, float col)
        {
            int irow, icol;
            float rfrac, cfrac;
            float row1 = 0, row2 = 0;

            irow = (int)row;
            icol = (int)col;

            if (irow < 0 || irow >= Height || icol < 0 || icol >= Width) { return 0; }

            row = Math.Min(row, Height - 1);
            col = Math.Min(col, Width - 1);

            rfrac = (float)(1.0 - (row - irow));
            cfrac = (float)(1.0 - (col - icol));

            if (cfrac < 1)
            {
                row1 = cfrac * this[band, irow, icol] + (1.0f - cfrac) * this[band, irow, icol + 1];
            }
            else
            {
                row1 = this[band, irow, icol];
            }

            if (rfrac < 1)
            {
                if (cfrac < 1)
                {
                    row2 = cfrac * this[band, irow + 1, icol] + (1.0f - cfrac) * this[band, irow + 1, icol + 1];
                }
                else
                {
                    row2 = this[band, irow + 1, icol];
                }
            }
            return rfrac * row1 + (1f - rfrac) * row2;
        }

        /// <summary>
        /// Image resizing based on 
        /// http://entropymine.com/imageworsener/resample/
        /// TODO this does not respect the image mask, if any
        /// https://github.jpl.nasa.gov/OnSight/Landform/issues/430
        /// </summary>
        public Image Resize(int targetWidth, int targetHeight, FilterDelegate filter = null)
        {
            if (filter == null)
            {
                // Default to Catmull-Rom for downsampling, Mitchell for upsampling
                if (targetWidth <= Width && targetHeight <= Height)
                {
                    filter = CatmullRomFilter;
                }
                else
                {
                    filter = MitchellFilter;

                }
            }

            Image horizontalResult = Instantiate(Bands, targetWidth, Height);

            List<Weight> weights = GetResizeWeights(targetWidth, Width, 2, filter);

            for (int band = 0; band < Bands; band++)
            {
                for (int row = 0; row < Height; row++)
                {
                    foreach (Weight w in weights)
                    {
                        float source = ReadClampedToBounds(band, w.inPixel, row);
                        horizontalResult[band, row, w.outPixel] += source * (float)w.weight;
                    }
                }
            }

            //resize vertically 
            Image result = Instantiate(Bands, targetWidth, targetHeight);

            weights = GetResizeWeights(targetHeight, Height, 2, filter);

            for (int band = 0; band < Bands; band++)
            {
                for (int col = 0; col < horizontalResult.Width; col++)
                {
                    foreach (Weight w in weights)
                    {
                        float source = horizontalResult.ReadClampedToBounds(band, col, w.inPixel);
                        result[band, w.outPixel, col] += source * (float)w.weight;
                    }
                }
            }

            return result;
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
        /// Helper class containing data for each mapping between input and output pixels
        /// </summary>
        private class Weight
        {
            public int inPixel;
            public int outPixel;
            public double weight;
        }

        /// <summary>
        /// Function type for resize filters
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        public delegate double FilterDelegate(double x);

        /// <summary>
        /// Gets weights for resizing in one dimension. 
        /// Each row (for horizontal resizing) or column (vertical resizing) will have the same weights. 
        /// This way, output values for each row/col can be computed without recomputing the filter for each pixel. 
        /// </summary>
        /// <param name="targetWidth"></param>
        /// <param name="targetHeight"></param>
        /// <param name="radius"></param>
        /// <param name="f"></param>
        /// <returns></returns>
        List<Weight> GetResizeWeights(int target, int current, int radius, FilterDelegate f)
        {
            List<Weight> weights = new List<Weight>();

            //If we are enlarging, the filter is scaled off the source pixels; for shrinking, off the target pixels 
            //This causes differences in which source pixels are sampled and what the weight for each source pixel is 
            bool enlarging = target > current;

            double ratio = (target - 1) / ((double)current - 1); // old/new

            for (int targetPixel = 0; targetPixel < target; targetPixel++)
            {
                int startingIndex = weights.Count; //start of weights for this target pixel

                //consider all source pixels for this target pixel 
                double zero = targetPixel / ratio; //current position in old coordinate system is filter's zero point
                int firstpix = enlarging ? (int)(zero - radius) + 1 : (int)((targetPixel - 2) / ratio) + 1; //casting truncates, but we want to round up 
                int lastpix = enlarging ? (int)(zero + radius) : lastpix = (int)((targetPixel + 2) / ratio);
                double norm = 0;
                for (int pixel = firstpix; pixel <= lastpix; pixel++) //iterate through x pixels of old picture 
                {
                    double filterx = enlarging ? zero - pixel : (zero - pixel) * ratio; //x in the filter coordinate system 
                    norm += f(filterx);
                    weights.Add(new Weight { inPixel = pixel, outPixel = targetPixel, weight = f(filterx) });
                }

                //normalize weights for this target pixel
                if (norm != 1)
                {
                    if (norm != 0)
                    {
                        for (int i = startingIndex; i < weights.Count; i++)
                        {
                            weights[i].weight /= norm;
                        }
                    }
                    else //weights sum to zero, so set all to 0
                    {
                        for (int i = startingIndex; i < weights.Count; i++)
                        {
                            weights[i].weight = 0;
                        }
                    }
                }

            }

            return weights;
        }

        #region Filters
        public double QuadraticFilter(double x)
        {
            x = Math.Abs(x);
            if (x < 0.5) return 0.75 - x * x;
            if (x < 1.5) return 0.50 * (x - 1.5) * (x - 1.5);
            return 0.0;
        }

        public double BoxFilter(double x)
        {
            x = Math.Abs(x);
            if (x <= 0.5) return 1;
            return 0;
        }

        double TriangleFilter(double xval)
        {
            xval = Math.Abs(xval);
            if (xval <= 1)
            {
                return 1 - xval;
            }
            return 0;
        }

        public static FilterDelegate MakeCubicFilter(double B, double C)
        {
            FilterDelegate res = (x) =>
            {
                x = Math.Abs(x);
                double x2 = x * x;
                double x3 = x2 * x;
                if (x < 1)
                {
                    return ((12 - 9 * B - 6 * C) * x3 + (-18 + 12 * B + 6 * C) * x2 + (6 - 2 * B)) / 6;
                }
                else if (x < 2)
                {
                    return ((-B - 6 * C) * x3 + (6 * B + 30 * C) * x2 + (-12 * B - 48 * C) * x + (8 * B + 24 * C)) / 6;
                }
                else
                {
                    return 0;
                }
            };
            return res;
        }
        public static readonly FilterDelegate CatmullRomFilter = MakeCubicFilter(0, 0.5);
        public static readonly FilterDelegate MitchellFilter = MakeCubicFilter(1 / 3.0, 1 / 3.0);
        public static readonly FilterDelegate BSplineFilter = MakeCubicFilter(1, 0);

        #endregion Filters

        /// <summary>
        /// Resize an image to the target width using a simple bicubic function
        /// Considering using Resize() instead
        /// TODO this does not respect the image mask, if any
        /// https://github.jpl.nasa.gov/OnSight/Landform/issues/430
        /// </summary>
        /// <param name="targetWidth"></param>
        /// <param name="targetHeight"></param>
        /// <returns></returns>
        public Image ResizeSimpleBicubic(int targetWidth, int targetHeight)
        {
            Image result = Instantiate(Bands, targetWidth, targetHeight);
            float wRatio = (Width - 1) / ((float)result.Width - 1);
            float hRatio = (Height - 1) / ((float)result.Height - 1);
            foreach (ImageCoordinate ic in result.Coordinates(true))
            {
                result[ic.Band, ic.Row, ic.Col] = BicubicSample(ic.Band, ic.Row * hRatio, ic.Col * wRatio);
            }
            return result;
        }

        /// <summary>
        /// Sample a pixel
        /// </summary>
        /// <param name="b"></param>
        /// <param name="row"></param>
        /// <param name="col"></param>
        /// <returns></returns>
        public float BicubicSample(int b, float row, float col)
        {
            var x = col;
            var y = row;

            var x1 = (int)x;
            var y1 = (int)y;
            var x2 = x1 + 1;
            var y2 = y1 + 1;

            var p00 = ReadClampedToBounds(b, x1 - 1, y1 - 1);
            var p01 = ReadClampedToBounds(b, x1 - 1, y1);
            var p02 = ReadClampedToBounds(b, x1 - 1, y2);
            var p03 = ReadClampedToBounds(b, x1 - 1, y2 + 1);

            var p10 = ReadClampedToBounds(b, x1, y1 - 1);
            var p11 = ReadClampedToBounds(b, x1, y1);
            var p12 = ReadClampedToBounds(b, x1, y2);
            var p13 = ReadClampedToBounds(b, x1, y2 + 1);

            var p20 = ReadClampedToBounds(b, x2, y1 - 1);
            var p21 = ReadClampedToBounds(b, x2, y1);
            var p22 = ReadClampedToBounds(b, x2, y2);
            var p23 = ReadClampedToBounds(b, x2, y2 + 1);

            var p30 = ReadClampedToBounds(b, x2 + 1, y1 - 1);
            var p31 = ReadClampedToBounds(b, x2 + 1, y1);
            var p32 = ReadClampedToBounds(b, x2 + 1, y2);
            var p33 = ReadClampedToBounds(b, x2 + 1, y2 + 1);

            return Bicubic(
                x - x1
              , y - y1
              , p00, p10, p20, p30
              , p01, p11, p21, p31
              , p02, p12, p22, p32
              , p03, p13, p23, p33
            );
        }

        /// <summary>
        /// Helper method for bicubic interpolation
        /// https://github.com/hughsk/bicubic
        /// https://github.com/hughsk/bicubic-sample/blob/master/index.js
        /// </summary>
        /// <param name="xf"></param>
        /// <param name="yf"></param>
        /// <param name="p00"></param>
        /// <param name="p01"></param>
        /// <param name="p02"></param>
        /// <param name="p03"></param>
        /// <param name="p10"></param>
        /// <param name="p11"></param>
        /// <param name="p12"></param>
        /// <param name="p13"></param>
        /// <param name="p20"></param>
        /// <param name="p21"></param>
        /// <param name="p22"></param>
        /// <param name="p23"></param>
        /// <param name="p30"></param>
        /// <param name="p31"></param>
        /// <param name="p32"></param>
        /// <param name="p33"></param>
        /// <returns></returns>
        float Bicubic(float xf, float yf,
                      float p00, float p01, float p02, float p03,
                      float p10, float p11, float p12, float p13,
                      float p20, float p21, float p22, float p23,
                      float p30, float p31, float p32, float p33
)
        {
            var yf2 = yf * yf;
            var xf2 = xf * xf;
            var xf3 = xf * xf2;

            var x00 = p03 - p02 - p00 + p01;
            var x01 = p00 - p01 - x00;
            var x02 = p02 - p00;
            var x0 = x00 * xf3 + x01 * xf2 + x02 * xf + p01;

            var x10 = p13 - p12 - p10 + p11;
            var x11 = p10 - p11 - x10;
            var x12 = p12 - p10;
            var x1 = x10 * xf3 + x11 * xf2 + x12 * xf + p11;

            var x20 = p23 - p22 - p20 + p21;
            var x21 = p20 - p21 - x20;
            var x22 = p22 - p20;
            var x2 = x20 * xf3 + x21 * xf2 + x22 * xf + p21;

            var x30 = p33 - p32 - p30 + p31;
            var x31 = p30 - p31 - x30;
            var x32 = p32 - p30;
            var x3 = x30 * xf3 + x31 * xf2 + x32 * xf + p31;

            var y0 = x3 - x2 - x0 + x1;
            var y1 = x0 - x1 - y0;
            var y2 = x2 - x0;

            return y0 * yf * yf2 + y1 * yf2 + y2 * yf + x1;
        }

        /// <summary>
        /// Read a value but clamp x and y to valid bounds
        /// </summary>
        /// <param name="b"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        float ReadClampedToBounds(int b, float x, float y)
        {
            int row = (int)MathE.Clamp(y, 0, this.Height - 1);
            int col = (int)MathE.Clamp(x, 0, this.Width - 1);
            return this[b, row, col];
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

        /// <summary>
        /// converts the floating point values in the source image to colorized values. the previewBucketdistances
        /// are the boundaries for the colors in colorsLowToHigh. There should be one more color than distance to catch
        /// the distances that are larger than the final bucket cutoff
        /// </summary>
        /// <param name="colorCutoffValues">the floating point values that represent the upper bound of that color (eg. cutoffvalue 0.2, all values less than 0.2 get that value.</param>
        /// <param name="colorsLowToHigh">colors intended to be paired with colorCutoffValues. each color in R, G, B order, range 0 to 1. Should be 1 more color than cutoff values as the upper-end catchall color (greater than the last color cutoff value)</param>
        /// <param name="bgColor">color in R, G, B order, range 0 to 1</param>
        /// <returns>3 band colorized image</returns>
        public Image ColorizeScalarImage(float[] colorCutoffValues, float[][] colorsLowToHigh, float[] bgColor)
        {
            if (Bands != 1)
            {
                throw new InvalidDataException("expecting a single band image to be colorized");
            }

            Image result = Instantiate(3, Width, Height);
            if (HasMask)
            {
                result.CreateMask(true);
            }

            for (int idxRow = 0; idxRow < Height; idxRow++)
            {
                for (int idxCol = 0; idxCol < Width; idxCol++)
                {
                    if (HasMask && !IsValid(idxRow, idxCol))
                    {
                        result.SetBandValues(idxRow, idxCol, bgColor);
                        continue;
                    }

                    float val = this[0, idxRow, idxCol];
                    float[] color = colorsLowToHigh.Last(); //catchall for values > final cuttoff.
                    for (int idxColor = 0; idxColor < colorCutoffValues.Length; idxColor++)
                    {
                        if (val < colorCutoffValues[idxColor])
                        {
                            color = colorsLowToHigh[idxColor];
                            break;
                        }
                    }

                    result.SetBandValues(idxRow, idxCol, color);

                    if (HasMask)
                    {
                        result.SetMaskValue(idxRow, idxCol, false);
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

        public Image MaskToImage(float valid = 0, float invalid = 1)
        {
            var ret = Instantiate(1, Width, Height);
            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    ret[0, row, col] = IsValid(row, col) ? valid : invalid;
                }
            }
            return ret;
        }

        /// <summary>
        /// count valid pixels
        /// if mask image is provided then any pixels which are 0 there are also considered invalid
        /// </summary>
        public int CountValid(Image mask = null)
        {
            int valid = 0;
            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    if (IsValid(row, col) && (mask == null || mask[0, row, col] != 0))
                    {
                        valid++;
                    }
                }
            }
            return valid;
        }

        /// <summary>
        /// flood fill mask from each invalid pixel on the border of this mask
        /// </summary>
        public Image AddOuterRegionsToMask(Image mask, float invalid = 1)
        {
            if (!HasMask)
            {
                return mask;
            }
            void floodFill(int row, int col)
            {
                if (IsValid(row, col) || mask[0, row, col] == invalid) return;
                mask[0, row, col] = invalid;
                var queue = new Queue<Pixel>();
                queue.Enqueue(new Pixel(row, col));
                var offsets = new Pixel[] { new Pixel(-1, 0), new Pixel(1, 0), new Pixel(0, -1), new Pixel(0, 1) };
                while (queue.Count > 0)
                {
                    var px = queue.Dequeue();
                    foreach (var offset in offsets)
                    {
                        var tgt = px + offset;
                        if (tgt.Row >= 0 && tgt.Row < Height && tgt.Col >= 0 && tgt.Col < Width &&
                            !IsValid(tgt.Row, tgt.Col) && mask[0, tgt.Row, tgt.Col] != invalid)
                        {
                            mask[0, tgt.Row, tgt.Col] = invalid;
                            queue.Enqueue(tgt);
                        }
                    }
                }
            }
            for (int row = 0; row < Height; row++)
            {
                floodFill(row, 0);
                floodFill(row, Width - 1);
            }
            for (int col = 0; col < Width; col++)
            {
                floodFill(0, col);
                floodFill(Height - 1, col);
            }
            return mask;
        }

        public static float[] MonoToColor(float mono)
        {
            return new float[3] { mono, mono, mono };
        }

        public enum LuminanceMode { AVERAGE, MAX, ITU_BT709, RED, GREEN, BLUE };

        public static float ColorToMono(float r, float g, float b, LuminanceMode mode = LuminanceMode.AVERAGE)
        {
            switch (mode)
            {
                case LuminanceMode.AVERAGE: return (r + g + b) / 3;
                case LuminanceMode.MAX: return Math.Max(r, Math.Max(g,  b));
                case LuminanceMode.ITU_BT709: return  0.2126f * r + 0.7152f * g + 0.0722f * b;
                case LuminanceMode.RED: return r;
                case LuminanceMode.GREEN: return g;
                case LuminanceMode.BLUE: return b;
                default: throw new ArgumentException("unhandled mode: " + mode);
            }
        }

        /// <summary>
        /// bilinearly sample the image and return a 3 channel color
        /// </summary>
        public float[] SampleAsColor(Vector2 srcPixel)
        {
            if (Bands != 3 && Bands != 1)
                throw new NotImplementedException("Only expecting 3 bands source or single band to convert to 3 band color");

            float[] samples = null;
            if (Bands == 3)
            {
                samples = new float[3];
                for (int idxBand = 0; idxBand < Bands; idxBand++)
                {
                    samples[idxBand] = BilinearSample(idxBand, (float)srcPixel.Y, (float)srcPixel.X);
                }
            }
            else if (Bands == 1)
            {
                samples = MonoToColor(BilinearSample(0, (float)srcPixel.Y, (float)srcPixel.X));
            }

            return samples;
        }

        /// <summary>
        /// bilinearly sample the image and return a single channel color
        /// </summary>
        public float SampleAsMono(Vector2 srcPixel, LuminanceMode mode = LuminanceMode.AVERAGE)
        {
            if (Bands == 3)
            {
                float[] samples = new float[3];
                for (int idxBand = 0; idxBand < Bands; idxBand++)
                {
                    samples[idxBand] = BilinearSample(idxBand, (float)srcPixel.Y, (float)srcPixel.X);
                }

                return ColorToMono(samples[0], samples[1], samples[2], mode); //NOTE: implies RGB ordering
            }
            else if (Bands == 1)
            {
                return BilinearSample(0, (float)srcPixel.Y, (float)srcPixel.X);
            }
            else
            {
                throw new NotImplementedException("Only expecting a single or 3 bands to convert to 3 band color");
            }
        }

        /// <summary>
        /// fill destination with samples from source texture (eg. replicate a single band to 3 if needed)
        /// </summary>
        public void SetAsColor(float[] samples, int destRow, int destCol)
        {
            if (Bands != 3)
            {
                throw new NotImplementedException("set as color requires a 3 band destination");
            }

            if (samples.Length == 3)
            {
                for (int idxBand = 0; idxBand < Bands; idxBand++)
                {
                    this[idxBand, destRow, destCol] = samples[idxBand];
                }
            }
            else if (samples.Length == 1)
            {
                for (int idxBand = 0; idxBand < Bands; idxBand++)
                {
                    this[idxBand, destRow, destCol] = samples[0];
                }
            }
            else
            {
                throw new NotImplementedException("Only expecting 3 bands or a single band to convert to 3 band color");
            }
        }

        /// <summary>
        /// fill destination with samples from source texture (eg. replicate a single band to 3 if needed)
        /// </summary>
        public void SetAsMono(float[] samples, int destRow, int destCol, LuminanceMode mode = LuminanceMode.AVERAGE)
        {
            if (Bands != 1)
            {
                throw new NotImplementedException("set as mono requires a single band destination");
            }

            if (samples.Length == 3)
            {
                this[0, destRow, destCol] = ColorToMono(samples[0], samples[1], samples[2], mode); //implies RGB ordering on samples             
            }
            else if (samples.Length == 1)
            {
                this[0, destRow, destCol] = samples[0];
            }
            else
            {
                throw new NotImplementedException("Only expecting a single band or  3 bands to convert to a single band color");
            }
        }
    }
}
