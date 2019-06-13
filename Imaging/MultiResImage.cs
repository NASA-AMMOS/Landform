using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OPS.Imaging
{
    /// <summary>
    /// This class should only be used for loading in an Image with dynamic resolution and reading from that Image. Modifying the image via inherited Image class methods will produce unwanted results.
    /// </summary>
    public class MultiResImage : Image
    {
        private Image[] DownsampledImages;
        private BoundingBox[] ImageBounds;
        private int[] DownsampleRatios;
        private double MeterPerPixel;

        public IEnumerable<Image> Images { get { return DownsampledImages.Select(img => (Image)img.Clone()); } }
        public IEnumerable<BoundingBox> Bounds { get { return (IEnumerable<BoundingBox>)ImageBounds.Clone(); } }
        public IEnumerable<int> Ratios { get { return (IEnumerable<int>)DownsampleRatios.Clone(); } }

        /// <summary>
        /// Creates a new blank image with the specified resolution and bands
        /// </summary>
        /// <param name="bands"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public MultiResImage(int bands, int width, int height) : base(bands, width, height) {}

        /// <summary>
        /// Create a multi res image by downsampling regions from Image source. Regions should contain mapping from image regions to their true meter per pixel resolution
        /// </summary>
        /// <param name="source"></param>
        /// <param name="regions"></param>
        public MultiResImage(Image source, double meterPerPixel, Dictionary<BoundingBox, double> regions) : base(source.Bands, source.Width, source.Height)
        {
            Init(new Func<int, int, int, int, Image> ((x, y, w, h) => source.Crop(y,x,w,h)), meterPerPixel, regions);
        }

        /// <summary>
        /// Create a MultiResImage directly from image on disk
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public MultiResImage(string filename, double meterPerPixel, Dictionary<BoundingBox, double> regions) : base(0,0,0)
        {
            string ext = Path.GetExtension(filename);
            ImageSerializer s = ImageSerializers.Instance.GetSerializer(ext);
            if (s == null)
            {
                throw new ImageSerializationException("Image format not supported");
            }
            if (s.GetType() != typeof(GDALSerializer))
            {
                throw new NotImplementedException("Only GDAL Serializer supported for MultiResImage Load");
            }

            Vector3 metadata = ((GDALSerializer)s).GetMetadata(filename);
            this.Bands = (int)metadata[0];
            this.Width = (int)metadata[1];
            this.Height = (int)metadata[2];

            Init(new Func<int, int, int, int, Image>( (x, y, w, h) => ((GDALSerializer)s).PartialRead(filename, x, y, w, h, s.DefaultReadConverter())), meterPerPixel, regions);
        }

        private void Init(Func<int, int, int, int, Image> imageRegion, double meterPerPixel, Dictionary<BoundingBox, double> regions)
        {
            this.MeterPerPixel = meterPerPixel;

            List<KeyValuePair<BoundingBox, double>> pairList = regions.ToList();
            pairList.Sort((p1, p2) => p1.Value.CompareTo(p2.Value));

            List<Image> images = new List<Image>();
            List<BoundingBox> bounds = new List<BoundingBox>();
            List<int> ratios = new List<int>();

            foreach (var p in pairList)
            {
                BoundingBox b = p.Key;

                if (b.Max.X != Math.Floor(b.Max.X) || b.Max.Y != Math.Floor(b.Max.Y) ||
                    b.Min.X != Math.Floor(b.Min.X) || b.Min.Y != Math.Floor(b.Min.Y))
                {
                    throw new Exception("Non integer pixel bounds passed to MultiResImage.");
                }

                //Get downsample ratio
                int gridCellSize = (int)Math.Floor(p.Value / meterPerPixel);
                gridCellSize = Math.Max(gridCellSize, 1);

                //Correct bounding box dimensions to a multiple of computed grid cell size
                Vector2 newColBounds = FitBoundsToGridCellSize((int)b.Min.X, (int)b.Max.X, gridCellSize);
                Vector2 newRowBounds = FitBoundsToGridCellSize((int)b.Min.Y, (int)b.Max.Y, gridCellSize);

                //Internally store bounds at half pixel offset so BoundingBox.Contains returns inclusive min, exclusive max
                bounds.Add(new BoundingBox(new Vector3(newColBounds.X - 0.5, newRowBounds.X - 0.5, -1), new Vector3(newColBounds.Y - 0.5, newRowBounds.Y - 0.5, 1)));

                Image tmp = imageRegion((int)newColBounds.X, (int)newRowBounds.X, (int)(newColBounds.Y - newColBounds.X), (int)(newRowBounds.Y - newRowBounds.X));
                images.Add(tmp.Resize(tmp.Width / gridCellSize, tmp.Height / gridCellSize));
                ratios.Add(gridCellSize);
            }

            DownsampledImages = images.ToArray();
            ImageBounds = bounds.ToArray();
            DownsampleRatios = ratios.ToArray();
        }

        /// <summary>
        /// Get/set the pixel from the highest level of detail region that contains it. See Warning about set.
        /// </summary>
        /// <param name="band"></param>
        /// <param name="row"></param>
        /// <param name="column"></param>
        /// <returns></returns>
        public override float this[int band, int row, int column]
        {
            get
            {
                for(int i = 0; i < DownsampledImages.Count(); i++)
                {
                    BoundingBox bounds = ImageBounds[i];
                    if (bounds.Contains(new Vector3(column, row, 0)) == ContainmentType.Contains) {
                        int gridCellSize = DownsampleRatios[i];
                        int mappedRow = (int)((row - Math.Ceiling(bounds.Min.Y)) / gridCellSize);
                        int mappedCol = (int)((column - Math.Ceiling(bounds.Min.X)) / gridCellSize);
                        return DownsampledImages[i][band, mappedRow, mappedCol];
                    }
                }
                return 0;
            }
            //Warning: At lower resolution regions, this will write the entire grid cell; it does not reinterpolate based on source image data!
            //If precise update needed, the source image must be modified and new MultiResImage created.
            set
            {
                for(int i = 0; i < DownsampledImages.Count(); i++)
                {
                    BoundingBox bounds = ImageBounds[i];
                    if(bounds.Contains(new Vector3(column, row, 0)) == ContainmentType.Contains)
                    {
                        int gridCellSize = DownsampleRatios[i];
                        int mappedRow = (int)((row - bounds.Min.Y) / gridCellSize);
                        int mappedCol = (int)((column - bounds.Min.X) / gridCellSize);
                        DownsampledImages[i][band, mappedRow, mappedCol] = value;
                        //Only need to update highest resolution image
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Flatten a multi res image
        /// </summary>
        /// <returns></returns>
        public Image Flatten() {
            Image ret = new Image(this.Bands, this.Width, this.Height);
            for (int b = 0; b < this.Bands; b++) {
                for (int r = 0; r < this.Height; r++)
                {
                    for (int c = 0; c < this.Width; c++)
                    {
                        ret[b, r, c] = this[b, r, c];
                    }
                }
            }
            return ret;
        }

        /// <summary>
        /// Find the best centered containing interval that has a size divisible by gridCellSize. If this fails (by hitting source image bounds), find the largest centered interval possible.
        /// </summary>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <param name="gridCellSize"></param>
        /// <returns></returns>
        private Vector2 FitBoundsToGridCellSize(int min, int max, int gridCellSize)
        {
            int width = max - min;
            int expand = width % gridCellSize;
            bool progress;
            int newMin = min;
            int newMax = max;
            while (expand > 0)
            {
                progress = false;
                //Add a row on each side
                if (newMax < Height)
                {
                    newMax += 1;
                    expand -= 1;
                    progress = true;
                }
                if (expand > 0 && newMin > 0)
                {
                    newMin -= 1;
                    expand -= 1;
                    progress = true;
                }
                if (!progress)
                {
                    //Contract bounding box if expanding on both sides fails
                    int cut = width % gridCellSize;
                    int shiftMin = cut / 2;
                    int shiftMax = cut - shiftMin;
                    //TODO: log warning?
                    return new Vector2(min + shiftMin, max - shiftMax);
                }
            }
            return new Vector2(newMin, newMax);
        }
    }
}
